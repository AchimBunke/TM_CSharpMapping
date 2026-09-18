using GBX.NET;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.MetaNotPersistent;
using GBX.NET.Engines.MwFoundations;
using GBX.NET.Engines.Plug;
using System.ComponentModel;
using System.Numerics;
using System.Reflection;
using System.Xml.Linq;
using TM_GenericMapping.Common;
using TM_GenericMapping.Messaging;
using static GBX.NET.Engines.GameData.CGameItemModel;
using static GBX.NET.Engines.Plug.CPlugPrefab;
using static GBX.NET.Engines.Plug.CPlugSkel;
using static GBX.NET.Engines.Plug.CPlugSurface;

namespace TM_GenericMapping.Items.MeshCompilation;

public class ItemParser
{
    internal class ParseContext
    {
        public NodeRefTableV3 nodeRefTable = new();
        public Dictionary<int, EntityRef> dynaIndexToEntityRef = new();
        public List<CPlugDynaObjectModel> processedDynaObjectModels = new();

        public Dictionary<int, EntityRef> waypointIndexToEntityRef = new();
        public List<NPlugTrigger_SWaypoint> processedWaypointModels = new();

        public void Reset()
        {
            nodeRefTable.Clear();
            dynaIndexToEntityRef.Clear();
            processedDynaObjectModels.Clear();
            waypointIndexToEntityRef.Clear();
            processedWaypointModels.Clear();
        }
    }
    ParseContext parseContext = new();

    public ToolResult<NormalizedItemV3> Parse(CGameItemModel item)
    {
        parseContext.Reset();

        NormalizedItemV3 normalizedItem = new NormalizedItemV3();

        ParseMetadata(item, normalizedItem);

        if (ItemExtensions.TryGetCrystal(item, out var crystal))
        {
            ParseCPlugCrystal(crystal, normalizedItem);
        }
        else if(ItemExtensions.TryGetPrefab(item, out var prefab))
        {
            var result = ParsePrefabEntityModel(prefab, null, normalizedItem);
            if (result.IsFailure)
                return ToolResult.Fail(result);
        }
        else if (ItemExtensions.TryGetCommonItemEntityModel(item, out var commonEntityModel))
        {
            var result = ParseCommonItemEntityModel(commonEntityModel, normalizedItem);
            if (result.IsFailure)
                return ToolResult.Fail(result);
        }
        else if (ItemExtensions.TryGetVariantList(item, out var variantList))
        {
            var result = ParseVariantList(variantList, normalizedItem);
            if (result.IsFailure)
                return ToolResult.Fail(result);
        }
        else
        {
            return ToolResult.Fail(nameof(ItemParser), ErrorCodes.MeshParser.UnsupportedMesh);
        }

        return ToolResult.Success(normalizedItem, nameof(ItemParser));
    }

    void ParseMetadata(CGameItemModel item, NormalizedItemV3 normalizedItem)
    {
        normalizedItem.PlacementParam = item.DefaultPlacement;
        normalizedItem.IconWebP = item.IconWebP;
        normalizedItem.Icon = item.Icon;
        normalizedItem.Name = item.Name ?? string.Empty;
        normalizedItem.Description = item.Description ?? string.Empty;
        normalizedItem.WaypointType = item.WaypointType;
    }

    class MaterialBucket
    {
        public List<Vec3> Positions = new();
        public List<Vec3> Normals = new();
        public List<Vec2> TexCoords = new();
        public List<Vec2> LightmapCoords = new();
        public List<int> Indices = new();
        public int Type;
        public bool Collidable;
        public Dictionary<(Vec3, Vec2, Vec2), int> WeldMap = new();
        public int SmoothingGroup;
    }
    
    void ParseCPlugCrystal(CPlugCrystal crystal, NormalizedItemV3 normalizedItem)
    {
        const int Mesh = 0;
        const int Trigger = 1;
        const int Collision = 2;

        List<MeshRef> meshes = new List<MeshRef>();
        List<ShapeRef> triggers = new List<ShapeRef>();
        List<ShapeRef> collisionShapes = new List<ShapeRef>();

        CPlugSpawnModel? spawnModel = null;
        List<int> smoothingGroups = crystal.GetChunk<CPlugCrystal.Chunk09003007>()?.U01?.ToList() ?? new List<int>();
        int firstSmoothingGroupIdx = 0;
        Vector3? waypointSpawnPos = null;
        Quaternion? waypointSpawnRot = null;

        foreach (var layer in crystal.Layers)
        {
            // per-material (+ per-smoothing-group, for visible geo) buckets, scoped to this layer
            var buckets = new Dictionary<(CPlugMaterialUserInst mat, int smoothingGroup), MaterialBucket>();

            MeshPropertiesV3 properties = MeshPropertiesV3.None;

            switch (layer)
            {
                case CPlugCrystal.GeometryLayer geo:
                    {
                        var sourcePositions = geo.Crystal!.Positions;
                        if (geo.IsEnabled)
                            properties |= MeshPropertiesV3.Enabled;
                        if (geo.IsVisible)
                            properties |= MeshPropertiesV3.Visible;
                        if (geo.Collidable)
                            properties |= MeshPropertiesV3.Collidable;

                        for (int faceIdx = 0; faceIdx < geo.Crystal.Faces.Length; faceIdx++)
                        {
                            var face = geo.Crystal.Faces[faceIdx];
                            var mat = face.Material!.MaterialUserInst!;

                            // per-face smoothing group — advances with faceIdx, not shared across the whole layer
                            int faceSmoothingGroup = geo.IsVisible
                                ? smoothingGroups[firstSmoothingGroupIdx + faceIdx]
                                : -1; // invisible/collision geo doesn't need shading groups

                            var bucketKey = (mat, faceSmoothingGroup);
                            if (!buckets.TryGetValue(bucketKey, out var bucket))
                            {
                                bucket = new MaterialBucket()
                                {
                                    Positions = new List<Vec3>(),
                                    Normals = geo.IsVisible ? new List<Vec3>() : null,
                                    TexCoords = geo.IsVisible ? new List<Vec2>() : null,
                                    LightmapCoords = geo.IsVisible ? new List<Vec2>() : null,
                                    Indices = new List<int>(),
                                    Type = geo.IsVisible ? Mesh : Collision,
                                    Collidable = geo.Collidable && mat.SurfacePhysicId != CPlugSurface.MaterialId.NotCollidable,
                                    WeldMap = new Dictionary<(Vec3, Vec2, Vec2), int>(),
                                    SmoothingGroup = faceSmoothingGroup
                                };
                                buckets[bucketKey] = bucket;
                            }

                            // fan triangulation — fully split vertices (per corner)
                            for (int i = 1; i < face.Vertices.Length - 1; i++)
                            {
                                var corners = new[] { face.Vertices[0], face.Vertices[i], face.Vertices[i + 1] };

                                foreach (var corner in corners)
                                {
                                    var texCoord = geo.IsVisible ? corner.TexCoord : default;
                                    var lmCoord = geo.IsVisible ? corner.LightmapCoord : default;
                                    var key = (sourcePositions[corner.Index], texCoord, lmCoord);

                                    if (!bucket.WeldMap.TryGetValue(key, out int dst))
                                    {
                                        dst = bucket.Positions.Count;
                                        bucket.WeldMap[key] = dst;
                                        bucket.Positions.Add(sourcePositions[corner.Index]);
                                        if (geo.IsVisible)
                                        {
                                            bucket.TexCoords.Add(texCoord);
                                            bucket.LightmapCoords.Add(lmCoord);
                                            bucket.Normals.Add(Vec3.Zero);
                                        }
                                    }
                                    bucket.Indices.Add(dst);
                                }
                            }
                        }

                        if (geo.IsVisible)
                            firstSmoothingGroupIdx += geo.Crystal.Faces.Length;
                    }
                    break;
                case CPlugCrystal.TriggerLayer trigger:
                    {
                        var sourcePositions = trigger.Crystal!.Positions;
                        if (trigger.IsEnabled)
                            properties |= MeshPropertiesV3.Enabled;
                        foreach (var face in trigger.Crystal.Faces)
                        {
                            var mat = face.Material!.MaterialUserInst!;
                            var bucketKey = (mat, -1); // no smoothing groups for triggers
                            if (!buckets.TryGetValue(bucketKey, out var bucket))
                            {
                                bucket = new MaterialBucket()
                                {
                                    Positions = new List<Vec3>(),
                                    Normals = new List<Vec3>(),
                                    TexCoords = new List<Vec2>(),
                                    LightmapCoords = new List<Vec2>(),
                                    Indices = new List<int>(),
                                    Type = Trigger,
                                    Collidable = false,
                                    WeldMap = new Dictionary<(Vec3, Vec2, Vec2), int>(),
                                    SmoothingGroup = -1
                                };
                                buckets[bucketKey] = bucket;
                            }

                            for (int i = 1; i < face.Vertices.Length - 1; i++)
                            {
                                var corners = new[] { face.Vertices[0], face.Vertices[i], face.Vertices[i + 1] };

                                foreach (var corner in corners)
                                {
                                    var key = (sourcePositions[corner.Index], corner.TexCoord, corner.LightmapCoord);
                                    if (!bucket.WeldMap.TryGetValue(key, out int dst))
                                    {
                                        dst = bucket.Positions.Count;
                                        bucket.WeldMap[key] = dst;
                                        bucket.Positions.Add(sourcePositions[corner.Index]);
                                        bucket.TexCoords.Add(corner.TexCoord);
                                        bucket.LightmapCoords.Add(corner.LightmapCoord);
                                        bucket.Normals.Add(Vec3.Zero);
                                    }
                                    bucket.Indices.Add(dst);
                                }
                            }
                        }
                    }
                    break;
                case CPlugCrystal.SpawnPositionLayer spawn:
                    {
                        if (!spawn.IsEnabled)
                            continue;
                        spawnModel = GbxItemUtils.CreateSpawnModel();
                        waypointSpawnPos = spawn.SpawnPosition;
                        waypointSpawnRot = Quaternion.CreateFromYawPitchRoll(spawn.HorizontalAngle * MathUtils.Deg2Rad, spawn.VerticalAngle * MathUtils.Deg2Rad, spawn.RollAngle * MathUtils.Deg2Rad);

                        var position = spawn.SpawnPosition.ToVector3();
                        spawnModel.Loc = Iso4Utils.IsoFromPitchYawRoll(position, spawn.VerticalAngle, spawn.HorizontalAngle, spawn.RollAngle);
                    }
                    break;
                default:
                    continue;
            }

            // concatenate buckets into final index buffer, recording submesh ranges
            foreach (var (bucketKey, bucket) in buckets)
            {
                var mat = bucketKey.mat;
                var posArr = bucket.Positions.ToArray();
                var idxArr = bucket.Indices.ToArray();
                var texArr = bucket.TexCoords?.ToArray() ?? [];
                var lmArr = bucket.LightmapCoords?.ToArray() ?? [];

                var normalizedModel = new NormalizedModelV3();
                if (bucket.Type == Mesh)
                {

                    var nrmArr = GbxItemUtils.ComputeFlatNormals(posArr, idxArr);
                  
                    Vector3[] tangents = [];
                    Vector3[] bitangents = [];
                    if (bucket.TexCoords.Count > 0)
                    {
                        CalculateTangents(
                            posArr.Select(v => v.AsVector3()).ToArray(), 
                            nrmArr.Select(v => v.AsVector3()).ToArray(),
                            idxArr,
                            texArr.Select(v => v.AsVector2()).ToArray(),
                            out tangents, 
                            out bitangents);
                        tangents = tangents.Select(v=>QuantizeVec3_10b(v)).Select(v=>v.ToVector3()).ToArray();
                        bitangents = bitangents.Select(v=>QuantizeVec3_10b(v)).Select(v=>v.ToVector3()).ToArray();
                    }
                    nrmArr = nrmArr.Select(QuantizeVec3_10b).ToArray();


                    normalizedModel.Type = ModelTypeV3.Static;

                    var mesh = new NormalizedMeshV3()
                    {
                        Positions = posArr,
                        Normals = nrmArr,
                        TexCoords = texArr.Length> 0 ? texArr.ToArray() : null,
                        LightmapCoords = lmArr.Length > 0 ? lmArr.ToArray() : null,
                        TangentUs = tangents.Length > 0 ? tangents.Select(v => v.ToVec3()).ToArray() : null,
                        TangentVs = bitangents.Length > 0 ? bitangents.Select(v => v.ToVec3()).ToArray() : null,
                        Colors = null,
                        Indices = idxArr,
                        Material = mat,
                        Name = GbxItemUtils.MaterialToName(mat),
                    };
                    int key = normalizedItem.MeshPool.Count;
                    normalizedItem.MeshPool.Add(key, mesh);

                    var meshRef = new MeshRef()
                    {
                        MeshKey = key,
                        Properties = properties,
                        SmoothingGroup = bucket.SmoothingGroup
                    };
                    meshes.Add(meshRef);
                }
                else if (bucket.Type == Trigger)
                {
                    var shape = new NormalizedShapeV3()
                    {
                        Positions = posArr,
                        Indices = idxArr,
                        SurfaceMaterialIds = Enumerable.Repeat(mat.SurfacePhysicId, idxArr.Length / 3).ToArray()
                    };
                    int key = normalizedItem.ShapePool.Count;
                    normalizedItem.ShapePool.Add(key, shape);
                    triggers.Add(new ShapeRef()
                    {
                        ShapeKey = key,
                        Role = ShapeRoleV3.Trigger_Waypoint,
                    });
                }
                else if (bucket.Type == Collision)
                {
                    var shape = new NormalizedShapeV3()
                    {
                        Positions = posArr,
                        Indices = idxArr,
                        SurfaceMaterialIds = Enumerable.Repeat(mat.SurfacePhysicId, idxArr.Length / 3).ToArray()
                    };
                    int key = normalizedItem.ShapePool.Count;
                    normalizedItem.ShapePool.Add(key, shape);
                    collisionShapes.Add(new ShapeRef()
                    {
                        ShapeKey = key,
                        Role = ShapeRoleV3.Static,
                    });
                }
            }
        }

        var root = new NormalizedModelV3()
        {
            Type = ModelTypeV3.Container,
        };
        normalizedItem.ModelPool.Add(normalizedItem.ModelPool.Count, root);
        if (meshes.Count > 0 || collisionShapes.Count > 0)
        {
            var model = new NormalizedModelV3()
            {
                Type = ModelTypeV3.Static,
                Meshes = meshes,
                Shapes = collisionShapes,
            };
            var key = normalizedItem.ModelPool.Count;
            normalizedItem.ModelPool.Add(key, model);
            root.Children.Add(new EntityRef() { ModelKey = key });
        }
        if (triggers.Count > 0)
        {
            var model = new NormalizedModelV3()
            {
                Type = ModelTypeV3.Trigger_Waypoint,
                Shapes = triggers,
                WaypointNoRespawn = false,
                WaypointType = normalizedItem.WaypointType,
            };
            var key = normalizedItem.ModelPool.Count;
            normalizedItem.ModelPool.Add(key, model);
            root.Children.Add(new EntityRef()
            {
                ModelKey = key,
                WaypointSpawnPosition = waypointSpawnPos,
                WaypointSpawnRotation = waypointSpawnRot,
            });
        }
        

        normalizedItem.Model = root;
    }

    public Vec3 QuantizeVec3_10b(Vec3 value)
    {
        float x = Math.Clamp(value.X, -1f, 1f);
        float y = Math.Clamp(value.Y, -1f, 1f);
        float z = Math.Clamp(value.Z, -1f, 1f);

        x = MathF.Round(x * 511f) / 511f;
        y = MathF.Round(y * 511f) / 511f;
        z = MathF.Round(z * 511f) / 511f;

        return new Vec3(x, y, z);
    }
    public static void CalculateTangents(
        Vector3[] positions,
        Vector3[] normals,
        int[] indices,
        Vector2[] texCoords,
        out Vector3[] tangents,
        out Vector3[] bitangents)
    {
        int vertexCount = positions.Length;

        tangents = new Vector3[vertexCount];
        bitangents = new Vector3[vertexCount];

        for (int i = 0; i < indices.Length; i += 3)
        {
            int i0 = indices[i + 0];
            int i1 = indices[i + 1];
            int i2 = indices[i + 2];

            Vector3 p0 = positions[i0];
            Vector3 p1 = positions[i1];
            Vector3 p2 = positions[i2];

            Vector2 uv0 = texCoords[i0];
            Vector2 uv1 = texCoords[i1];
            Vector2 uv2 = texCoords[i2];

            Vector3 edge1 = p1 - p0;
            Vector3 edge2 = p2 - p0;

            float du1 = uv1.X - uv0.X;
            float dv1 = uv1.Y - uv0.Y;
            float du2 = uv2.X - uv0.X;
            float dv2 = uv2.Y - uv0.Y;

            float det = du1 * dv2 - du2 * dv1;

            // Degenerate UV triangle
            if (MathF.Abs(det) < 1e-8f)
                continue;

            float invDet = 1.0f / det;

            Vector3 tangent =
                (edge1 * dv2 - edge2 * dv1) * invDet;

            Vector3 bitangent =
                (edge2 * du1 - edge1 * du2) * invDet;

            tangents[i0] += tangent;
            tangents[i1] += tangent;
            tangents[i2] += tangent;

            bitangents[i0] += bitangent;
            bitangents[i1] += bitangent;
            bitangents[i2] += bitangent;
        }

        // Orthogonalize and normalize per vertex.
        for (int i = 0; i < vertexCount; i++)
        {
            Vector3 n = normals[i].Normalized();

            // Gram-Schmidt: T must be perpendicular to N.
            Vector3 t = tangents[i];
            t = t - n * Vector3.Dot(n, t);

            if (t.LengthSquared() > 1e-8f)
                t = Vector3.Normalize(t);
            else
                t = Vector3.Zero;

            tangents[i] = t;

            // Reconstruct B from N × T.
            Vector3 b = Vector3.Cross(n, t);

            // Preserve the UV handedness.
            if (Vector3.Dot(b, bitangents[i]) < 0.0f)
                b = -b;

            bitangents[i] = b;
        }
    }

    ToolResult<None> ParsePrefabEntityModel(CPlugPrefab prefab, EntityRefBase? entityRef, NormalizedItemV3 normalizedItem) 
    {
        if (parseContext.nodeRefTable.TryGetKey(prefab, out var key))
        {
            entityRef!.ModelKey = key.GetHashCode();
            return ToolResult.Success(nameof(ItemParser));
        }

        key = normalizedItem.ModelPool.Count.ToString();
        var container = new NormalizedModelV3()
        {
            Type = ModelTypeV3.Container,
        };
        normalizedItem.ModelPool.Add(key.GetHashCode(), container);

        if (entityRef is null)
            normalizedItem.Model = container; // set model if entityref is null (root entityModel)
        else
            entityRef.ModelKey = key.GetHashCode(); // set model key if entityref is provided (variant)


        foreach (var ent in prefab.Ents)
        {
            var entParseResult = ParseEntRef(ent, normalizedItem, container);
            if (entParseResult.IsFailure)
                return entParseResult;
        }
        return ToolResult.Success(nameof(ItemParser));
    }
    ToolResult<None> ParsePrefab(CPlugPrefab prefab, EntityRefBase entityRef, NormalizedItemV3 normalizedItem, NormalizedModelV3 container)
    {
        if (parseContext.nodeRefTable.TryGetKey(prefab, out var key))
        {
            entityRef.ModelKey = key.GetHashCode();
            return ToolResult.Success(nameof(ItemParser));
        }
        key = normalizedItem.ModelPool.Count.ToString();


        var model = new NormalizedModelV3()
        {
            Type = ModelTypeV3.Container,
        };
        normalizedItem.ModelPool.Add(key.GetHashCode(), model);
        entityRef.ModelKey = key.GetHashCode();
        parseContext.nodeRefTable.Register(key, prefab);

        foreach (var ent in prefab.Ents)
        {
            var entParseResult = ParseEntRef(ent, normalizedItem, model);
            if (entParseResult.IsFailure)
                return entParseResult;
        }
        return ToolResult.Success(nameof(ItemParser));
    }
    ToolResult<None> ParseEntRef(EntRef entRef, NormalizedItemV3 normalizedItem, NormalizedModelV3 container)
    {
        // Special case kinematic constraints, which are not represented as a model but rather as a constraint on another entity
        if (entRef.Model is NPlugDyna_SKinematicConstraint kinematicConstraint)
        {
            return ParseKinematicConstraint(kinematicConstraint, entRef);
        }
        // Special case spawnModel, which are not represented as a model but rather as a constraint on another entity
        if (entRef.Model is CPlugSpawnModel spawnModel)
        {
            ParseSpawnModel(spawnModel, entRef);
            return ToolResult.Success(nameof(ItemParser));
        }

        
        var entityRef = new EntityRef()
        {
            Position = entRef.Position.ToVector3(),
            Rotation = entRef.Rotation.ToQuaternion(),
        };
        container.Children.Add(entityRef);

        switch (entRef.Model)
        {
            case CPlugStaticObjectModel staticObjectModel:
                return ParseStaticObjectModel(staticObjectModel, entityRef, normalizedItem);
            case CPlugDynaObjectModel dynaObjectModel:
                return ParseDynamicObjectModel(dynaObjectModel, entRef, entityRef, normalizedItem);
            case NPlugTrigger_SSpecial triggerSpecial:
                return ParseTriggerSpecial(triggerSpecial, entityRef, normalizedItem);
            case NPlugTrigger_SWaypoint triggerWaypoint:
                return ParseTriggerWaypoint(triggerWaypoint, entityRef, normalizedItem);
            case CPlugPrefab nestedPrefab:
                return ParsePrefab(nestedPrefab, entityRef, normalizedItem, container);

        }
        return ToolResult.Success(nameof(ItemParser));
    }
    ToolResult<None> ParseKinematicConstraint(NPlugDyna_SKinematicConstraint kinematicConstraint, EntRef entRef)
    {
        var constraintParams = (entRef.Params as NPlugDyna_SPrefabConstraintParams)!;
        if (constraintParams.Ent2 >= parseContext.processedDynaObjectModels.Count)
            return ToolResult.Fail(nameof(ItemParser), ErrorCodes.MeshParser.MissingDynamicConstraintTarget);
        
        if(!parseContext.dynaIndexToEntityRef.TryGetValue(constraintParams.Ent2, out var targetEntityRef))
            return ToolResult.Fail(nameof(ItemParser), ErrorCodes.MeshParser.MissingDynamicConstraintTarget);

        targetEntityRef.KinematicConstraint = kinematicConstraint;

        if (constraintParams.Ent1 >= 0)
        {
            if (constraintParams.Ent1 >= parseContext.processedDynaObjectModels.Count)
                return ToolResult.Fail(nameof(ItemParser), ErrorCodes.MeshParser.MissingDynamicConstraintParent);

            if(!parseContext.dynaIndexToEntityRef.TryGetValue(constraintParams.Ent1, out var parentEntityRef))
                return ToolResult.Fail(nameof(ItemParser), ErrorCodes.MeshParser.MissingDynamicConstraintParent);
            targetEntityRef.RelativeMovingParentKey = parentEntityRef.ModelKey;
        }

        return ToolResult.Success(nameof(ItemParser));
    }

    ToolResult<None> ParseStaticObjectModel(CPlugStaticObjectModel staticObjectModel, EntityRef entityRef, NormalizedItemV3 normalizedItem)
    {
        //already parsed this model, just reference it
        if (parseContext.nodeRefTable.TryGetKey(staticObjectModel, out var key))
        {
            entityRef.ModelKey = key.GetHashCode();
            return ToolResult.Success(nameof(ItemParser));
        }

        key = normalizedItem.ModelPool.Count.ToString();

        // register this model in the nodeRefTable so we don't parse it again
        parseContext.nodeRefTable.Register(key, staticObjectModel);

        var model = new NormalizedModelV3()
        {
            Type = ModelTypeV3.Static,
        };
        // add the model to the pool so it can be referenced by other entities
        normalizedItem.ModelPool.Add(key.GetHashCode(), model);
        // set the entityRef to reference this model
        entityRef.ModelKey = key.GetHashCode();

        // mesh
        if(staticObjectModel.Mesh is null)
            return ToolResult.Fail(nameof(ItemParser), ErrorCodes.MeshParser.MissingMesh);

        ParseSolid2Model(staticObjectModel.Mesh, model, normalizedItem, staticObjectModel.IsMeshCollidable);

        // staticShape
        if (staticObjectModel.Shape is null)
            return ToolResult.Success(nameof(ItemParser)); // early return if no shape

        var shapeRef = new ShapeRef()
        {
            Role = ShapeRoleV3.Static,
        };
        var shapeResult = ParseShape(staticObjectModel.Shape, shapeRef, normalizedItem);
        if (shapeResult.IsFailure)
            return shapeResult;
        model.Shapes.Add(shapeRef);


        return ToolResult.Success(nameof(ItemParser));
    }

    ToolResult<None> ParseDynamicObjectModel(CPlugDynaObjectModel dynamicObjectModel, EntRef ent, EntityRef entityRef, NormalizedItemV3 normalizedItem)
    {
        // register this model in the dynaIndexToEntityRef so that kinematic constraints can reference it later
        parseContext.dynaIndexToEntityRef[parseContext.processedDynaObjectModels.Count] = entityRef;
        parseContext.processedDynaObjectModels.Add(dynamicObjectModel);
       

        //already parsed this model, just reference it
        if (parseContext.nodeRefTable.TryGetKey(dynamicObjectModel, out var key))
        {
            entityRef.ModelKey = key.GetHashCode();
            entityRef.DynaObjectModelParams = ent.Params as NPlugDynaObjectModel_SInstanceParams;
            return ToolResult.Success(nameof(ItemParser));
        }

        key = normalizedItem.ModelPool.Count.ToString();

        // register this model in the nodeRefTable so we don't parse it again
        parseContext.nodeRefTable.Register(key, dynamicObjectModel);


        var model = new NormalizedModelV3()
        {
            Type = ModelTypeV3.Dynamic,
        };
        // add the model to the pool so it can be referenced by other entities
        normalizedItem.ModelPool.Add(key.GetHashCode(), model);
        // set the entityRef to reference this model
        entityRef.ModelKey = key.GetHashCode();
        entityRef.DynaObjectModelParams = ent.Params as NPlugDynaObjectModel_SInstanceParams;

        // mesh
        if (dynamicObjectModel.Mesh is null)
            return ToolResult.Fail(nameof(ItemParser), ErrorCodes.MeshParser.MissingMesh);

        ParseSolid2Model(dynamicObjectModel.Mesh, model, normalizedItem, meshIsCollisionSource: false);

        // staticShape
        if (dynamicObjectModel.StaticShape is not null)
        {
            var staticShapeRef = new ShapeRef()
            {
                Role = ShapeRoleV3.Static,
            };
            var shapeResult = ParseShape(dynamicObjectModel.StaticShape, staticShapeRef, normalizedItem);
            if (shapeResult.IsFailure)
                return shapeResult;
            model.Shapes.Add(staticShapeRef);
        }

        // dynamicShape
        if (dynamicObjectModel.DynaShape is not null)
        {
            var dynamicShapeRef = new ShapeRef()
            {
                Role = ShapeRoleV3.Dynamic,
            };
            var shapeResult = ParseShape(dynamicObjectModel.DynaShape, dynamicShapeRef, normalizedItem);
            if (shapeResult.IsFailure)
                return shapeResult;
            model.Shapes.Add(dynamicShapeRef);
        }

        return ToolResult.Success(nameof(ItemParser));
    }

    ToolResult<None> ParseTriggerSpecial(NPlugTrigger_SSpecial triggerSpecial, EntityRef entityRef, NormalizedItemV3 normalizedItem)
    {
        if (parseContext.nodeRefTable.TryGetKey(triggerSpecial, out var key))
        {
            entityRef.ModelKey = key.GetHashCode();
            return ToolResult.Success(nameof(ItemParser));
        }

        var triggerShape = triggerSpecial.GetTriggerShape();
        if (triggerShape == null)
            return ToolResult.Fail(nameof(ItemParser), ErrorCodes.MeshParser.MissingTriggerShape);

        key = normalizedItem.ModelPool.Count.ToString();
        parseContext.nodeRefTable.Register(key, triggerSpecial);

        // trigger gameplay
        ushort gamplayIdShort = triggerShape.GetChunk<CPlugSurface.Chunk0900C003>()?.U02?.FirstOrDefault() ?? 0;
        var triggerGameplayId = ItemTriggerEffectConverter.ShortToGameplayId(gamplayIdShort);

        var model = new NormalizedModelV3()
        {
            Type = ModelTypeV3.Trigger_Special,
            TriggerGameplayId = triggerGameplayId,
            GameplayMainDir = triggerShape.Surf?.GameplayMainDir ?? new Vector3(0,0,1),
        };

        // add the model to the pool so it can be referenced by other entities
        normalizedItem.ModelPool.Add(key.GetHashCode(), model);
        // set the entityRef to reference this model
        entityRef.ModelKey = key.GetHashCode();


        var triggerShapeRef = new ShapeRef()
        {
            Role = ShapeRoleV3.Trigger_Special,
        };
        var shapeResult = ParseShape(triggerShape, triggerShapeRef, normalizedItem);
        if (shapeResult.IsFailure)
            return shapeResult;
        model.Shapes.Add(triggerShapeRef);

        return ToolResult.Success(nameof(ItemParser));
    }

    ToolResult<None> ParseTriggerWaypoint(NPlugTrigger_SWaypoint triggerWaypoint, EntityRef entityRef, NormalizedItemV3 normalizedItem)
    {
        // register this model in the waypointIndexToEntityRef so that waypoint spawn can reference it later
        parseContext.waypointIndexToEntityRef[parseContext.processedWaypointModels.Count] = entityRef;
        parseContext.processedWaypointModels.Add(triggerWaypoint);

        if (parseContext.nodeRefTable.TryGetKey(triggerWaypoint, out var key))
        {
            entityRef.ModelKey = key.GetHashCode();
            return ToolResult.Success(nameof(ItemParser));
        }

        var triggerShape = triggerWaypoint.GetTriggerShape();
        if (triggerShape == null)
            return ToolResult.Fail(nameof(ItemParser), ErrorCodes.MeshParser.MissingTriggerShape);

        key = normalizedItem.ModelPool.Count.ToString();

        parseContext.nodeRefTable.Register(key, triggerWaypoint);


        var model = new NormalizedModelV3()
        {
            Type = ModelTypeV3.Trigger_Waypoint,
            WaypointType = (EWaypointType?)triggerWaypoint.Type,
            WaypointNoRespawn = triggerWaypoint.NoRespawn,
        };

        // add the model to the pool so it can be referenced by other entities
        normalizedItem.ModelPool.Add(key.GetHashCode(), model);
        // set the entityRef to reference this model
        entityRef.ModelKey = key.GetHashCode();


        var triggerShapeRef = new ShapeRef()
        {
            Role = ShapeRoleV3.Trigger_Waypoint,
        };
        var shapeResult = ParseShape(triggerShape, triggerShapeRef, normalizedItem);
        if (shapeResult.IsFailure)
            return shapeResult;
        model.Shapes.Add(triggerShapeRef);

        return ToolResult.Success(nameof(ItemParser));
    }

    void ParseSpawnModel(CPlugSpawnModel spawnModel, EntRef entRef)
    {
        foreach(var waypointTrigger in parseContext.waypointIndexToEntityRef.Values)
        {
            if (waypointTrigger.WaypointSpawnPosition.HasValue)
                continue; // only first waypoint model can register spawn position
            // it is assumed that all waypoint triggers that are targeted are under the same prefab parent so position is relative to that.
            waypointTrigger.WaypointSpawnPosition = entRef.Position; 
            waypointTrigger.WaypointSpawnRotation = entRef.Rotation;
        }
    }
    void ParseSolid2Model(
        CPlugSolid2Model solid2Model,
        NormalizedModelV3 normalizedModel,
        NormalizedItemV3 normalizedItem,
        bool meshIsCollisionSource)
    {
        bool hasLods = solid2Model.LodMaxDistAtFov90?.Length > 0;
        normalizedModel.LODDistances = solid2Model.LodMaxDistAtFov90 ?? [];

        foreach (var shaded in solid2Model.ShadedGeoms ?? [])
        {
            var visual = solid2Model.Visuals![shaded.VisualIndex];
            var material = solid2Model.CustomMaterials![shaded.MaterialIndex].MaterialUserInst!;
            if (visual is not CPlugVisualIndexedTriangles vit)
                continue;

            var meshRef = new MeshRef();

            ParseIndexedTriangles(vit, material, meshRef, normalizedItem);

            // disable collision for non-collision source meshes (shape takes role of collision)
            if (!meshIsCollisionSource)
                meshRef.Properties &= ~MeshPropertiesV3.Collidable; 

            // set LOD properties
            if (hasLods)
            {
                if (!LODUtils.IsVisibleInAllLods(shaded.LodMask, solid2Model.LodMaxDistAtFov90!.Length)) // check if has any lod or always visible
                {
                    meshRef.Properties |= MeshPropertiesV3.LOD;
                }
                meshRef.LODMask = shaded.LodMask;
            }

            // setting smoothing group to 0 for now. This can be updated if smoothing group information is available in the future.
            meshRef.SmoothingGroup = 0; 

            normalizedModel.Meshes.Add(meshRef);
        }

        if (solid2Model.LightInsts?.Length > 0)
        {
            var (skel, sockets) = GbxItemUtils.ParseSkel(solid2Model.Skel!);
            foreach (var light in solid2Model.LightInsts)
            {
                var model = solid2Model.LightUserModels![light.ModelIndex];
                var socket = sockets[light.SocketIndex];

                socket.U02.Deconstruct(out var rot, out var xyz);
                var m = new Matrix4x4(
                    rot.XX, rot.XY, rot.XZ, 0,
                    rot.YX, rot.YY, rot.YZ, 0,
                    rot.ZX, rot.ZY, rot.ZZ, 0,
                    0, 0, 0, 1);

                var lightRef = new LightRef()
                {
                    Position = xyz,
                    Rotation = Quaternion.CreateFromRotationMatrix(m),
                };
                ParseLightModel(model, socket, lightRef, normalizedItem);

                normalizedModel.Lights.Add(lightRef);
            }
        }
    }
    void ParseIndexedTriangles(
        CPlugVisualIndexedTriangles visual,
        CPlugMaterialUserInst material, 
        MeshRef meshRef,
        NormalizedItemV3 normalizedItem)
    {
        // properties, lod/smoothing group, etc. are set within static object model compilation.
        meshRef.Properties = MeshPropertiesV3.Enabled | MeshPropertiesV3.Visible;
        if (material.SurfacePhysicId != CPlugSurface.MaterialId.NotCollidable)
            meshRef.Properties |= MeshPropertiesV3.Collidable;

        // already parsed this mesh, just reference it
        if (parseContext.nodeRefTable.TryGetKey(visual, out var key))
        {
            meshRef.MeshKey = key.GetHashCode();
        }
        else
        {

            var stream = visual.VertexStreams[0];

            var tangentUsField = typeof(CPlugVertexStream).GetField("tangentUs",
              BindingFlags.NonPublic | BindingFlags.Instance);
            var tangentsUs = (Vec3[])tangentUsField?.GetValue(stream)!;

            var tangentVsField = typeof(CPlugVertexStream).GetField("tangentVs",
              BindingFlags.NonPublic | BindingFlags.Instance);
            var tangentVs = (Vec3[])tangentVsField?.GetValue(stream)!;

            Vec2[]? texCoords = null;
            Vec2[]? lightmapCoords = null;

            bool hasUv0 = stream.UVs.TryGetValue(0, out var uv0);
            bool hasUv1 = stream.UVs.TryGetValue(1, out var uv1);
            if (material.Color is null || material.Color.Length == 0)
            {
                if (hasUv0)
                    texCoords = uv0;
                if (hasUv1)
                    lightmapCoords = uv1;
            }
            else
            {
                if (hasUv0 && hasUv1)
                {
                    texCoords = uv0;
                    lightmapCoords = uv1;
                }
                else
                {
                    if (hasUv0)
                        lightmapCoords = uv0;
                }

            }
            var normalizedMesh = new NormalizedMeshV3()
            {
                Positions = stream.Positions!,
                Normals = stream.Normals!,
                TexCoords = texCoords,
                LightmapCoords = lightmapCoords,
                Colors = stream.Colors.TryGetValue(0, out var col) ? col : null,
                Indices = visual.IndexBuffer!.Indices,
                Material = material,
                TangentUs = tangentsUs,
                TangentVs = tangentVs,
                Name = GbxItemUtils.MaterialToName(material)
            };
            key = normalizedItem.MeshPool.Count.ToString();
            parseContext.nodeRefTable.Register(key, visual);
            normalizedItem.MeshPool.Add(key.GetHashCode(), normalizedMesh);
            meshRef.MeshKey = key.GetHashCode();
        }
        var mesh = normalizedItem.MeshPool[meshRef.MeshKey];
        meshRef.PreLightGenerator = GbxItemUtils.ComputePreLightGenFromMeshData(mesh);
    }

    void ParseLightModel(CPlugLightUserModel lightModel, Socket socket, LightRef lightRef, NormalizedItemV3 normalizedItem)
    {
        // currently no reference reuse for lights as they are lightweight.

        var key = normalizedItem.LightPool.Count.ToString();
        var normalizedLight = new NormalizedLightV3
        {
            LightModel = ObjectCloner.DeepCloneObject(lightModel)!,
            Name = $"Light_{key}",
        };

        normalizedItem.LightPool.Add(key.GetHashCode(), normalizedLight);
        lightRef.LightKey = key.GetHashCode();
    }

    ToolResult<None> ParseShape(CPlugSurface surface, ShapeRef shapeRef, NormalizedItemV3 normalizedItem)
    {
        if (parseContext.nodeRefTable.TryGetKey(surface, out var key))
        {
            shapeRef.ShapeKey = key.GetHashCode();
            return ToolResult.Success(nameof(ItemParser));
        }

        key = normalizedItem.ShapePool.Count.ToString();

        parseContext.nodeRefTable.Register(key, surface);

        var shape = new NormalizedShapeV3()
        {
        };

        // handle different surface types (only mesh was seen before)
        switch (surface.Surf)
        {
            case CPlugSurface.Mesh mesh:
                ParseSurfaceMesh(mesh, shape, normalizedItem);
                break;
            default:
                return ToolResult.Fail(nameof(ItemParser), ErrorCodes.MeshParser.UnsupportedSurfaceType);
        }


        normalizedItem.ShapePool.Add(key.GetHashCode(), shape);
        shapeRef.ShapeKey = key.GetHashCode();
      

        return ToolResult.Success(nameof(ItemParser));
    }

    void ParseSurfaceMesh(CPlugSurface.Mesh mesh, NormalizedShapeV3 normalizedShape, NormalizedItemV3 normalizedItem)
    {
        normalizedShape.Positions = mesh.Vertices.ToArray();

        // prepare arrays
        normalizedShape.Indices = new int[mesh.Triangles!.Length * 3];
        normalizedShape.SurfaceMaterialIds = new CPlugSurface.MaterialId[mesh.Triangles.Length];

        for (int i = 0; i < mesh.Triangles.Length; i++)
        {
            var triangle = mesh.Triangles[i];
            normalizedShape.Indices[i * 3 + 0] = triangle.Indices.X;
            normalizedShape.Indices[i * 3 + 1] = triangle.Indices.Y;
            normalizedShape.Indices[i * 3 + 2] = triangle.Indices.Z;

            // material id for collision surfaces
            normalizedShape.SurfaceMaterialIds[i] = (MaterialId)triangle.U02;
        }
    }

    ToolResult<None> ParseCommonItemEntityModel(CGameCommonItemEntityModel commonItemEntityModel, NormalizedItemV3 normalizedItem)
    {
        var containerkey = normalizedItem.ModelPool.Count;

        if (commonItemEntityModel.StaticObject is null)
            return ToolResult.Fail(nameof(ItemParser), ErrorCodes.MeshParser.MissingMesh);
        var staticModel = commonItemEntityModel.StaticObject;

        // root container
        var container = new NormalizedModelV3()
        {
            Type = ModelTypeV3.Container,
        };
        normalizedItem.ModelPool.Add(containerkey, container);
        normalizedItem.Model = container;

        // mesh
        var staticMeshRef = new EntityRef()
        {
            Position = Vec3.Zero,
            Rotation = Quaternion.Identity,
        };
        container.Children.Add(staticMeshRef);

        
        ParseStaticObjectModel(staticModel, staticMeshRef, normalizedItem);

        // triggerShape
        if (commonItemEntityModel.TriggerShape is null || commonItemEntityModel.TriggerShape is not CPlugSurface surface)
            return ToolResult.Success(nameof(ItemParser)); // early return if no triggershape

        bool isWaypoint = normalizedItem.WaypointType == EWaypointType.None;
        var triggerModel = new NormalizedModelV3()
        {
            Type = isWaypoint ? ModelTypeV3.Trigger_Waypoint : ModelTypeV3.Trigger_Special,
        };
        var triggerModelKey = normalizedItem.ModelPool.Count;
        normalizedItem.ModelPool.Add(triggerModelKey, triggerModel);
        
        var triggerShapeEntityRef = new EntityRef()
        {
            Position = Vec3.Zero,
            Rotation = Quaternion.Identity,
            ModelKey = triggerModelKey,
        };
        container.Children.Add(triggerShapeEntityRef);

        var shapeRef = new ShapeRef()
        {
            Role = isWaypoint ? ShapeRoleV3.Trigger_Waypoint : ShapeRoleV3.Trigger_Special,
        };
        triggerModel.Shapes.Add(shapeRef);

        var shapeResult = ParseShape(surface, shapeRef, normalizedItem);
        if (shapeResult.IsFailure)
            return shapeResult;


        if (isWaypoint)
        {
            triggerModel.WaypointType = normalizedItem.WaypointType;
            triggerModel.WaypointNoRespawn = false;

            triggerShapeEntityRef.WaypointSpawnPosition = triggerShapeEntityRef.Position;
            triggerShapeEntityRef.WaypointSpawnRotation = triggerShapeEntityRef.Rotation;
        }
        else
        {
            ushort gamplayIdShort = surface.GetChunk<CPlugSurface.Chunk0900C003>()?.U02?.FirstOrDefault() ?? 0;
            var triggerGameplayId = ItemTriggerEffectConverter.ShortToGameplayId(gamplayIdShort);
            triggerModel.TriggerGameplayId = triggerGameplayId;
        }

        return ToolResult.Success(nameof(ItemParser));
    }

    ToolResult<None> ParseVariantList(NPlugItem_SVariantList variantList, NormalizedItemV3 normalizedItem)
    {
        // root container
        var containerkey = normalizedItem.ModelPool.Count;

        var container = new NormalizedModelV3()
        {
            Type = ModelTypeV3.Variant_List,
        };
        normalizedItem.ModelPool.Add(containerkey, container);
        normalizedItem.Model = container;

        // variants
        foreach(var variant in variantList.Variants ?? [])
        {
            var result = ParseVariant(variant, normalizedItem, container);
            if(result.IsFailure)
                return result;
        }
        return ToolResult.Success(nameof(ItemParser));
    }
    ToolResult<None> ParseVariant(NPlugItem_SVariant variant, NormalizedItemV3 normalizedItem, NormalizedModelV3 container)
    {
        if (variant.EntityModel is not CPlugPrefab prefab)
            return ToolResult.Fail(nameof(ItemParser), ErrorCodes.MeshParser.UnsupportedVariantType);


        var key = normalizedItem.ModelPool.Count;

        var normVariant = new NormalizedVariantV3()
        {
            Tags = variant.Tags.ToDictionary(),
            HiddenInManualCycle = variant.HiddenInManualCycle,
        };
        container.Variants.Add(normVariant);

        var result = ParsePrefabEntityModel(prefab, normVariant, normalizedItem);
        if (result.IsFailure)
            return result;
        return ToolResult.Success(nameof(ItemParser));
    }
}
