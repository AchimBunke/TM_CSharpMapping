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

    void ParseCPlugCrystal(CPlugCrystal crystal, NormalizedItemV3 normalizedItem)
    {
        const int Mesh = 0;
        const int Trigger = 1;
        const int Unknown = -1;

        List<MeshRef> meshes = new List<MeshRef>();
        List<ShapeRef> triggers = new List<ShapeRef>();

        CPlugSpawnModel? spawnModel = null;
        List<int> smoothingGroups = crystal.GetChunk<CPlugCrystal.Chunk09003007>()?.U01?.ToList() ?? new List<int>();
        int firstSmoothingGroupIdx = 0;

        foreach (var layer in crystal.Layers)
        {
            // Two-pass: group split vertices by material first, then concatenate
            // so each material produces a contiguous index range (NormalizedMesh)

            // per-material buckets of indices (into the shared vertex buffer)
            var buckets = new Dictionary<CPlugMaterialUserInst, (
                List<Vec3> positions,
                List<Vec3> normals,
                List<Vec2> texCoords,
                List<Vec2> lightmapCoords,
                List<int> indices,
                int type,
                bool collidable,
                Dictionary<(Vec3, Vec2, Vec2), int> weldMap,
                int smoothingGroup)>();

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
                        foreach (var face in geo.Crystal.Faces)
                        {
                            var mat = face.Material!.MaterialUserInst!;
                            if (!buckets.TryGetValue(mat, out var bucket))
                            {
                                bucket = (new(), new(), new(), new(), new(), Mesh, mat.SurfacePhysicId != CPlugSurface.MaterialId.NotCollidable, [], 0);
                                buckets[mat] = bucket;
                            }

                            // fan triangulation — fully split vertices (per corner)
                            for (int i = 1; i < face.Vertices.Length - 1; i++)
                            {
                                var corners = new[] { face.Vertices[0], face.Vertices[i], face.Vertices[i + 1] };

                                foreach (var corner in corners)
                                {
                                    var key = (sourcePositions[corner.Index], corner.TexCoord, corner.LightmapCoord);
                                    if (!bucket.weldMap.TryGetValue(key, out int dst))
                                    {
                                        dst = bucket.positions.Count;
                                        bucket.weldMap[key] = dst;
                                        bucket.positions.Add(sourcePositions[corner.Index]);
                                        bucket.texCoords.Add(corner.TexCoord);
                                        bucket.lightmapCoords.Add(corner.LightmapCoord);
                                        bucket.normals.Add(Vec3.Zero);
                                        bucket.smoothingGroup = smoothingGroups[firstSmoothingGroupIdx];
                                    }
                                    bucket.indices.Add(dst);
                                }
                            }
                        }
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

                            if (!buckets.TryGetValue(mat, out var bucket))
                            {
                                bucket = (new(), new(), new(), new(), new(), Trigger, false, [], -1);
                                buckets[mat] = bucket;
                            }

                            // fan triangulation — fully split vertices (per corner)
                            for (int i = 1; i < face.Vertices.Length - 1; i++)
                            {
                                var corners = new[]
                                {
                                    face.Vertices[0],
                                    face.Vertices[i],
                                    face.Vertices[i + 1]
                                };

                                foreach (var corner in corners)
                                {
                                    var key = (sourcePositions[corner.Index], corner.TexCoord, corner.LightmapCoord);
                                    if (!bucket.weldMap.TryGetValue(key, out int dst))
                                    {
                                        dst = bucket.positions.Count;
                                        bucket.weldMap[key] = dst;
                                        bucket.positions.Add(sourcePositions[corner.Index]);
                                        bucket.texCoords.Add(corner.TexCoord);
                                        bucket.lightmapCoords.Add(corner.LightmapCoord);
                                        bucket.normals.Add(Vec3.Zero);
                                    }
                                    bucket.indices.Add(dst);
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
                        var position = spawn.SpawnPosition.ToVector3();
                        spawnModel.Loc = GbxItemUtils.IsoFromPitchYawRoll(position, spawn.VerticalAngle, spawn.HorizontalAngle, spawn.RollAngle);
                    }
                    break;
                default:
                    continue;
            }

            // concatenate buckets into final index buffer, recording submesh ranges
            var indices = new List<int>();

            foreach (var (mat, bucket) in buckets)
            {
                var posArr = bucket.positions.ToArray();
                var idxArr = bucket.indices.ToArray();
                var nrmArr = GbxItemUtils.ComputeSmoothNormals(posArr, idxArr);

                var normalizedModel = new NormalizedModelV3();
                if(bucket.type == Mesh)
                {
                    normalizedModel.Type = ModelTypeV3.Static;

                    var mesh = new NormalizedMeshV3()
                    {
                        Positions = posArr,
                        Normals = nrmArr,
                        TexCoords = bucket.texCoords.Count > 0 ? bucket.texCoords.ToArray() : null,
                        LightmapCoords = bucket.lightmapCoords.Count > 0 ? bucket.lightmapCoords.ToArray() : null,
                        Colors = null, // crystal has no vertex colors
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
                        SmoothingGroup = bucket.smoothingGroup
                    };
                    meshes.Add(meshRef);
                }
                else if( bucket.type == Trigger)
                {
                    var shape = new NormalizedShapeV3()
                    {
                        Positions = posArr,
                        Indices = idxArr,
                        GameplayMainDir = new Vec3(0, 0, 1),
                        SurfaceMaterialIds = Enumerable.Repeat(mat.SurfacePhysicId, idxArr.Length / 3).ToArray()
                    };
                    int key = normalizedItem.ShapePool.Count;
                    normalizedItem.ShapePool.Add(key, shape);
                    var shapeRef = new ShapeRef() 
                    {
                        ShapeKey = key,
                        Role = ShapeRoleV3.Trigger_Waypoint, 
                    };
                    triggers.Add(shapeRef);
                }
            }
        }

        var root = new NormalizedModelV3()
        {
            Type = ModelTypeV3.Container,
        };
        if (meshes.Count > 0)
        {
            var model = new NormalizedModelV3()
            {
                Type = ModelTypeV3.Static,
                Meshes = meshes,
            };
            var key = normalizedItem.ModelPool.Count;
            normalizedItem.ModelPool.Add(key, model);

            var modelRef = new EntityRef()
            {
                ModelKey = key,
            };
            root.Children.Add(modelRef);
        }
        if(triggers.Count > 0)
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

            var modelRef = new EntityRef()
            {
                ModelKey = key,
            };
            root.Children.Add(modelRef);
        }
        normalizedItem.Model = root;
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
            if (waypointTrigger.SpawnPosition.HasValue)
                continue; // only first waypoint model can register spawn position
            // it is assumed that all waypoint triggers that are targeted are under the same prefab parent so position is relative to that.
            waypointTrigger.SpawnPosition = entRef.Position; 
            waypointTrigger.SpawnRotation = entRef.Rotation;
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
        meshRef.PreLightGenerator = GbxItemUtils.ComputePreLightGeneratorFromMeshData(mesh);
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


        var shape = new NormalizedShapeV3()
        {
            GameplayMainDir = surface.Surf?.GameplayMainDir ?? new Vec3(0, 0, 1),
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


        key = normalizedItem.ShapePool.Count.ToString();
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

            triggerShapeEntityRef.SpawnPosition = triggerShapeEntityRef.Position;
            triggerShapeEntityRef.SpawnRotation = triggerShapeEntityRef.Rotation;
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
