using GBX.NET;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.Plug;
using System.Numerics;
using System.Reflection;
using TM_GenericMapping.Common;
using TM_GenericMapping.Messaging;
using static GBX.NET.Engines.GameData.CGameItemModel;
using static GBX.NET.Engines.Plug.CPlugSkel;
using static GBX.NET.Engines.Plug.CPlugSurface;

namespace TM_GenericMapping.Items;

public class MeshExtractor
{
    NodeRefTable _nodeRefTable = new NodeRefTable();

    public ToolResult<NormalizedItem> ExtractMesh(CGameItemModel item)
    {

        _nodeRefTable.Clear();

        List<(NormalizedMesh[] meshes, NormalizedLight[] lights, MeshGroup group)> groupResults = [];
        List<VariantGroup> variantGroups = [];
        if (ItemExtensions.TryGetCrystal(item, out var crystal))
        {
            var extractionResult = ExtractFromCrystal(crystal);
            if(extractionResult.IsFailure)
                return ToolResult.Fail(extractionResult);
            foreach(var group in extractionResult.Value.Where(g=>g.group.GroupType == GroupType.Trigger_Waypoint))
            {
                group.group.WaypointType = item.WaypointType;
            }
          
            groupResults.AddRange(extractionResult.Value);
        }
        else if (ItemExtensions.TryGetPrefab(item, out var prefab))
        {
            var prefabExtractionResult = ExtractFromPrefab(prefab);
            if (prefabExtractionResult.IsFailure)
                return ToolResult.Fail(prefabExtractionResult);
            groupResults.AddRange(prefabExtractionResult.Value);
        }
        else if (ItemExtensions.TryGetCommonItemEntityModel(item, out var commonEntityModel))
        {
            var commonEntityModelExtractionResult = ExtractFromCommonEntityModel(commonEntityModel, item.WaypointType != CGameItemModel.EWaypointType.None);
            if (commonEntityModelExtractionResult.IsFailure)
                return ToolResult.Fail(commonEntityModelExtractionResult);
            groupResults.AddRange(commonEntityModelExtractionResult.Value);
        }
        else if(ItemExtensions.TryGetVariantList(item, out var variantList))
        {
            var variantResult = ExtractFromVariantList(variantList);
            if (variantResult.IsFailure)
                return ToolResult.Fail(variantResult);
            groupResults.AddRange(variantResult.Value.Item1);
            variantGroups.AddRange(variantResult.Value.Item2);
        }
        else
        {
            return ToolResult.Fail(nameof(MeshExtractor), ErrorCodes.MeshExtractor.UnsupportedMesh);
        }

        var normalizedItem = new NormalizedItem();

        List<NormalizedMesh> allMeshes = [];
        List<MeshGroup> allGroups = [];
        List<NormalizedLight> allLights = [];

        for (int i = 0; i < groupResults.Count; i++)
        {
            var (meshes, lights, group) = groupResults[i];
            var groupIndex = allGroups.Count;
            allGroups.Add(group);
            foreach (var mesh in meshes)
            {
                mesh.GroupIndex = groupIndex;
            }
            foreach (var light in lights)
            {
                light.GroupIndex = groupIndex;
            }
            allMeshes.AddRange(meshes);
            allLights.AddRange(lights);
        }

        foreach (var mesh in allMeshes)
        {
            if (allMeshes.Count(g => g.MeshLink == mesh.MeshLink) <= 1)
                mesh.MeshLink = null; // reset mesh link for when meshes are never reused
        }
        foreach (var group in allGroups)
        {
            if (allGroups.Count(g => g.GroupLink == group.GroupLink) <= 1)
                group.GroupLink = null; // reset group link for when groups are never reused
        }

        normalizedItem.Meshes = allMeshes.ToArray();
        normalizedItem.Groups = allGroups.ToArray();
        normalizedItem.Lights = allLights.ToArray();
        normalizedItem.VariantGroups = variantGroups.ToArray();

        ExtractItemMetaData(item, normalizedItem);

        if (normalizedItem.Meshes.Any(m => m.MeshLink.HasValue))
        {
            int i = 0;
        }
        return ToolResult.Success(normalizedItem, nameof(MeshExtractor));
    }
    void ExtractItemMetaData(CGameItemModel item, NormalizedItem normalizedItem)
    {
        normalizedItem.PlacementParam = item.DefaultPlacement;
        normalizedItem.IconWebP = item.IconWebP;
        normalizedItem.Icon = item.Icon;
        normalizedItem.Name = item.Name ?? string.Empty;
        normalizedItem.Description = item.Description ?? string.Empty;
    }

    ToolResult<(NormalizedMesh[] mesh, NormalizedLight[] lights, MeshGroup group)[]> ExtractFromPrefab(CPlugPrefab prefab, Vector3? parentPosition = null, Quaternion? parentRotation = null, List<MeshGroup> dynaMeshGroups = null)
    {
        List<(NormalizedMesh[] meshes, NormalizedLight[] lights, MeshGroup group)> meshGroups = [];
        if (dynaMeshGroups is null)
            dynaMeshGroups = [];
        foreach (var ent in prefab.Ents)
        {
            var position = ent.Position.ToVector3();
            var rotation = new Quaternion(ent.Rotation.X, ent.Rotation.Y, ent.Rotation.Z, ent.Rotation.W);
            var worldRotation = parentRotation.HasValue ? rotation * parentRotation.Value : rotation;
            var worldPosition = parentPosition.HasValue ? Vector3.Transform(position, parentRotation!.Value) + parentPosition.Value : position;
            switch (ent.Model)
            {
                case CPlugStaticObjectModel staticObjectModel:
                    var staticResult = ExtractMeshesFromStaticObjectModel(staticObjectModel);
                    if(staticResult.IsFailure)
                        return ToolResult.Fail(staticResult);
                    staticResult.Value.group.Position = worldPosition;
                    staticResult.Value.group.Rotation = worldRotation;
                    meshGroups.Add(staticResult.Value);
                    break;
                case CPlugDynaObjectModel dynaObjectModel:
                    var dynaResult = ExtractFromDynaObjectModel(dynaObjectModel);
                    if(dynaResult.IsFailure)
                        return ToolResult.Fail(dynaResult);
                    dynaResult.Value.group.DynaObjectModelParams = ent.Params as NPlugDynaObjectModel_SInstanceParams;
                    dynaResult.Value.group.Position = worldPosition;
                    dynaResult.Value.group.Rotation = worldRotation;
                    meshGroups.Add(dynaResult.Value);
                    dynaMeshGroups.Add(dynaResult.Value.group);
                    break;
                case NPlugTrigger_SSpecial triggerSpecial:
                    var triggerResult = ExtractFromTriggerSpecial(triggerSpecial);
                    if (triggerResult.IsFailure)
                        return ToolResult.Fail(triggerResult);
                    triggerResult.Value.group.Position = worldPosition;
                    triggerResult.Value.group.Rotation = worldRotation;
                    meshGroups.Add(([triggerResult.Value.mesh], [], triggerResult.Value.group));
                    break;
                case NPlugTrigger_SWaypoint triggerWaypoint:
                    var waypointResult = ExtractFromTriggerWaypoint(triggerWaypoint);
                    if (waypointResult.IsFailure)
                        return ToolResult.Fail(waypointResult);
                    waypointResult.Value.group.Position = worldPosition;
                    waypointResult.Value.group.Rotation = worldRotation;
                    meshGroups.Add(([waypointResult.Value.mesh], [], waypointResult.Value.group));
                    break;
                case CPlugPrefab nestedPrefab:
                    var nestedResult = ExtractFromPrefab(nestedPrefab, worldPosition, worldRotation, dynaMeshGroups);
                    if (nestedResult.IsFailure)
                        return ToolResult.Fail(nestedResult);
                    meshGroups.AddRange(nestedResult.Value);
                    break;
                case NPlugDyna_SKinematicConstraint kinematicConstraint:
                    var constraintParams = (ent.Params as NPlugDyna_SPrefabConstraintParams)!;
                    var targetEnt = constraintParams.Ent2;

                    if(targetEnt >= dynaMeshGroups.Count)
                        return ToolResult.Fail(nameof(MeshExtractor), ErrorCodes.MeshExtractor.MissingDynaModel);
                    var targetMeshGroup = dynaMeshGroups[targetEnt];
                    targetMeshGroup.KinematicConstraint = kinematicConstraint;
                    targetMeshGroup.RelativeMovingParentIndex = constraintParams.Ent1 >= 0 ? constraintParams.Ent1 : (int?)null;
                    break;
                default:
                    return ToolResult.Fail(nameof(MeshExtractor), ErrorCodes.MeshExtractor.UnsupportedPrefabEntity, ent);
            }
        }

        return ToolResult.Success(meshGroups.ToArray(), nameof(MeshExtractor));
    }

    ToolResult<(NormalizedMesh[] meshes, NormalizedLight[] lights, MeshGroup group)> ExtractMeshesFromStaticObjectModel(CPlugStaticObjectModel staticObjectModel)
    {
        if (staticObjectModel.Mesh is null)
            return ToolResult.Fail(nameof(MeshExtractor), ErrorCodes.MeshExtractor.MissingMesh);
        var meshResult = ExtractFromSolid2Model(staticObjectModel.Mesh, meshIsCollisionSource: staticObjectModel.IsMeshCollidable);
        if(meshResult.IsFailure)
            return ToolResult.Fail(meshResult);
        meshResult.Value.group.GroupType = GroupType.StaticObject;
        
        var groupLink = meshResult.Value.group.GroupLink!.Value;
        if(!_nodeRefTable.TryGetNode<CPlugStaticObjectModel>(groupLink, out var key))
            _nodeRefTable.Register(groupLink, staticObjectModel);

        if (staticObjectModel.Shape is null)
            return meshResult;
        var shapeResult = ExtractFromSurface(staticObjectModel.Shape, out _);
        if(shapeResult.IsFailure)
            return ToolResult.Fail(shapeResult);
        shapeResult.Value.Type = MeshType.Static_Shape;
        shapeResult.Value.Properties |= MeshProperties.Collidable;

        return ToolResult.Success(meshResult.Value with { meshes = [..meshResult.Value.meshes, shapeResult.Value] }, nameof(MeshExtractor));
    }
    ToolResult<(NormalizedMesh[] meshes, NormalizedLight[] lights, MeshGroup group)> ExtractFromDynaObjectModel(CPlugDynaObjectModel dynaObjectModel)
    {
        (NormalizedMesh[] meshes, NormalizedLight[] lights, MeshGroup group) result;
        if (dynaObjectModel.Mesh is null)
            return ToolResult.Fail(nameof(MeshExtractor), ErrorCodes.MeshExtractor.MissingMesh);

        var meshResult = ExtractFromSolid2Model(dynaObjectModel.Mesh, meshIsCollisionSource: false);
        if (meshResult.IsFailure)
            return ToolResult.Fail(meshResult);
        result = meshResult.Value;
        result.group.GroupType = GroupType.DynaObject;

        var groupLink = meshResult.Value.group.GroupLink!.Value;
        if (!_nodeRefTable.TryGetNode<CPlugDynaObjectModel>(groupLink, out var key))
            _nodeRefTable.Register(groupLink, dynaObjectModel);

        if (dynaObjectModel.StaticShape is not null)
        {
            var staticShapeResult = ExtractFromSurface(dynaObjectModel.StaticShape, out _);
            if (staticShapeResult.IsFailure)
                return ToolResult.Fail(staticShapeResult);
            staticShapeResult.Value.Type = MeshType.Static_Shape;
            staticShapeResult.Value.Properties |= MeshProperties.Collidable;
            result = result with { meshes = [.. result.meshes, staticShapeResult.Value] };
        }
        if (dynaObjectModel.DynaShape is not null)
        {
            var dynaShapeResult = ExtractFromSurface(dynaObjectModel.DynaShape, out _);
            if (dynaShapeResult.IsFailure)
                return ToolResult.Fail(dynaShapeResult);
            dynaShapeResult.Value.Type = MeshType.Dyna_Shape;
            dynaShapeResult.Value.Properties |= MeshProperties.Collidable;
            result = result with { meshes = [.. result.meshes, dynaShapeResult.Value] };
        }

        return ToolResult.Success(result, nameof(MeshExtractor));
    }

    ToolResult<(NormalizedMesh[] meshes, NormalizedLight[] lights, MeshGroup group)> ExtractFromSolid2Model(CPlugSolid2Model solid2Model, bool meshIsCollisionSource = false)
    {
        var meshes = new List<NormalizedMesh>();
        var lights = new List<NormalizedLight>();

        bool hasLods = solid2Model.LodMaxDistAtFov90?.Length > 0;
        foreach (var shaded in solid2Model.ShadedGeoms ?? [])
        {
            var visual = solid2Model.Visuals![shaded.VisualIndex];
            if (visual is not CPlugVisualIndexedTriangles vit)
                continue;

            var subMeshResult = ExtractFromVisual(vit, solid2Model.CustomMaterials![shaded.MaterialIndex].MaterialUserInst!);
            if (subMeshResult.IsFailure)
                return ToolResult.Fail(subMeshResult);
            if (!meshIsCollisionSource)
                subMeshResult.Value.Properties &= ~MeshProperties.Collidable;
            if (hasLods)
            {
                if (!LODUtils.IsVisibleInAllLods(shaded.LodMask, solid2Model.LodMaxDistAtFov90!.Length)) // check if has any lod or always visible
                {
                    subMeshResult.Value.Properties |= MeshProperties.LOD;
                }
                subMeshResult.Value.LODMask = shaded.LodMask;
            }
            subMeshResult.Value.PreLightGenerator = solid2Model.PreLightGenerator;

            meshes.Add(subMeshResult.Value);
        }

        if (solid2Model.LightInsts?.Length > 0) 
        {
            var (skel, sockets) = ParseSkel(solid2Model.Skel!);
            foreach (var light in solid2Model.LightInsts)
            {
                var model = solid2Model.LightUserModels![light.ModelIndex];
                var socket = sockets[light.SocketIndex];

                var normalizedLight = ExtractFromLight(model, socket, $"Light_{light.ModelIndex}");
                lights.Add(normalizedLight);
            }
        }

        var group = new MeshGroup
        {
            LODDistances = solid2Model.LodMaxDistAtFov90 ?? []
        };
        if (!_nodeRefTable.TryGetKey(solid2Model, out var key))
            _nodeRefTable.Register(solid2Model, out key);
        group.GroupLink = key;


        return ToolResult.Success((meshes.ToArray(), lights.ToArray(), group), nameof(MeshExtractor));
    }

    (CPlugSkel skel, Socket[] sockets) ParseSkel(CPlugSkel skel)
    {
        var socketField = typeof(CPlugSkel).GetField("sockets",
         BindingFlags.NonPublic | BindingFlags.Instance);
        var sockets = (Socket[])socketField!.GetValue(skel)!;

        return (skel, sockets);
    }
    NormalizedLight ExtractFromLight(CPlugLightUserModel model, Socket socket, string name)
    {
        socket.U02.Deconstruct(out var rot, out var xyz);
        var m = new Matrix4x4(
            rot.XX, rot.XY, rot.XZ, 0,
            rot.YX, rot.YY, rot.YZ, 0,
            rot.ZX, rot.ZY, rot.ZZ, 0,
            0, 0, 0, 1);
        var normalizedLight = new NormalizedLight
        {
            Position = xyz,
            Rotation = Quaternion.CreateFromRotationMatrix(m),
            LightModel = ObjectCloner.DeepCloneObject(model)!,
            Name = name
        };
        return normalizedLight;
    }

    ToolResult<(NormalizedMesh[] meshes, NormalizedLight[] lights, MeshGroup group)[]> ExtractFromCrystal(CPlugCrystal crystal)
    {
        List<NormalizedMesh> geometryGroup= [];
        List<NormalizedMesh> triggerGroups = [];
        CPlugSpawnModel? spawnModel = null;
        List<int> smoothingGroups = crystal.GetChunk<CPlugCrystal.Chunk09003007>()?.U01?.ToList() ?? new List<int>();
        int firstSmoothingGroupIdx = 0;

        foreach (var layer in crystal.Layers)
        {
            // Two-pass: group split vertices by material first, then concatenate
            // so each material produces a contiguous index range (NormalizedSubmesh)

            // per-material buckets of indices (into the shared vertex buffer)
            var buckets = new Dictionary<CPlugMaterialUserInst, (
                List<Vec3> positions,
                List<Vec3> normals,
                List<Vec2> texCoords,
                List<Vec2> lightmapCoords,
                List<int> indices,
                MeshType type,
                bool collidable,
                Dictionary<(Vec3, Vec2, Vec2), int> weldMap,
                int smoothingGroup)>();

            MeshProperties properties = MeshProperties.None;

           
            switch (layer)
            {
                case CPlugCrystal.GeometryLayer geo:
                    {
                        var sourcePositions = geo.Crystal!.Positions;
                        if(geo.IsEnabled)
                            properties |= MeshProperties.Enabled;
                        if(geo.IsVisible)
                            properties |= MeshProperties.Visible;
                        if(geo.Collidable)
                            properties |= MeshProperties.Collidable;
                        foreach (var face in geo.Crystal.Faces)
                        {
                            var mat = face.Material!.MaterialUserInst!;
                            if (!buckets.TryGetValue(mat, out var bucket))
                            {
                                bucket = (new(), new(), new(), new(), new(), MeshType.Mesh, mat.SurfacePhysicId != CPlugSurface.MaterialId.NotCollidable, [], 0);
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
                            properties |= MeshProperties.Enabled;
                        foreach (var face in trigger.Crystal.Faces)
                        {
                            var mat = face.Material!.MaterialUserInst!;

                            if (!buckets.TryGetValue(mat, out var bucket))
                            {
                                bucket = (new(), new(), new(), new(), new(), MeshType.Trigger_Waypoint, false, [], -1);
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
                        spawnModel = MeshBuilder.CreateSpawnModel();
                        var position = spawn.SpawnPosition.ToVector3();
                        spawnModel.Loc = MeshBuilder.IsoFromPitchYawRoll(position, spawn.VerticalAngle, spawn.HorizontalAngle, spawn.RollAngle);
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
                var nrmArr = ComputeSmoothNormals(posArr, idxArr);
          
                var mesh = new NormalizedMesh
                {
                    Positions = posArr,
                    Normals = nrmArr,
                    TexCoords = bucket.texCoords.Count > 0 ? bucket.texCoords.ToArray() : null,
                    LightmapCoords = bucket.lightmapCoords.Count > 0 ? bucket.lightmapCoords.ToArray() : null,
                    Colors = null, // crystal has no vertex colors
                    Indices = idxArr,
                    Material = mat,
                    Type = bucket.type,
                    Properties = properties,
                    Name = MatToName(mat),
                    SmoothingGroup = bucket.smoothingGroup,
                };
                if (bucket.type == MeshType.Trigger_Waypoint)
                    triggerGroups.Add(mesh);
                else
                    geometryGroup.Add(mesh);
            }
        }
        List<(NormalizedMesh[] meshes, NormalizedLight[] lights, MeshGroup group)> groupedMeshes = [(geometryGroup.ToArray(), [], new MeshGroup() { GroupType = GroupType.StaticObject })];
        groupedMeshes.AddRange(triggerGroups.Select(m => (new[] { m }, new NormalizedLight[0], new MeshGroup() { GroupType = GroupType.Trigger_Waypoint })));
        if (spawnModel != null)
        {
            var group = groupedMeshes.LastOrDefault(g => g.group.GroupType == GroupType.Trigger_Waypoint);
            if(group != default)
                group.group.WaypointSpawnModel = spawnModel;
        }

        return ToolResult.Success(groupedMeshes.ToArray(), nameof(MeshExtractor));
    }
    ToolResult<(NormalizedMesh[] meshes, NormalizedLight[] lights, MeshGroup group)[]> ExtractFromCommonEntityModel(CGameCommonItemEntityModel commonEntityModel, bool isWaypoint)
    {
        List<(NormalizedMesh[] meshes, NormalizedLight[] lights, MeshGroup group)> results = [];

        if (commonEntityModel.StaticObject is null)
            return ToolResult.Fail(nameof(MeshExtractor), ErrorCodes.MeshExtractor.MissingMesh);
        var meshResult = ExtractMeshesFromStaticObjectModel(commonEntityModel.StaticObject);
        if (meshResult.IsFailure)
            return ToolResult.Fail(meshResult);
        meshResult.Value.group.GroupType = GroupType.StaticObject;
        results.Add(meshResult.Value);

        if (commonEntityModel.TriggerShape is null || commonEntityModel.TriggerShape is not CPlugSurface triggerSurf)
            return ToolResult.Success(results.ToArray(), nameof(MeshExtractor));

        var shapeResult = ExtractFromSurface(triggerSurf, out var gameplayDir);
        if (shapeResult.IsFailure)
            return ToolResult.Fail(shapeResult);
        shapeResult.Value.Type = isWaypoint ? MeshType.Trigger_Waypoint : MeshType.Trigger_Special;

        var group = new MeshGroup() 
        { 
            GroupType = isWaypoint ? GroupType.Trigger_Waypoint : GroupType.Trigger_Special, 
            GameplayMainDir = gameplayDir,
            GroupLink = null, // no link possible with CGameCommonItem
        };

        results.Add(([shapeResult.Value], [], group));

        return ToolResult.Success(results.ToArray(), nameof(MeshExtractor));
    }

    ToolResult<((NormalizedMesh[] meshes, NormalizedLight[] lights, MeshGroup group)[], VariantGroup[])> ExtractFromVariantList(NPlugItem_SVariantList variantList)
    {
        List<VariantGroup> variants = [];
        List<(NormalizedMesh[] meshes, NormalizedLight[] lights, MeshGroup group)> groupResults = [];
        foreach(var variant in variantList.Variants ?? [])
        {
            var variantGroup = new VariantGroup()
            {
                Tags = variant.Tags.ToDictionary(),
                HiddenInManualCycle = variant.HiddenInManualCycle
            };
            variants.Add(variantGroup);

            switch (variant.EntityModel)
            {
                case CPlugPrefab prefab:
                    var prefabResult = ExtractFromPrefab(prefab);
                    if (prefabResult.IsFailure)
                        return ToolResult.Fail(prefabResult);
                    foreach(var v in prefabResult.Value)
                    {
                        v.group.VariantIndex = variants.Count;
                    }
                    groupResults.AddRange(prefabResult.Value);
                    break;
                case CPlugStaticObjectModel staticObjectModel:
                    var staticResult = ExtractMeshesFromStaticObjectModel(staticObjectModel);
                    if (staticResult.IsFailure)
                        return ToolResult.Fail(staticResult);
                    staticResult.Value.group.VariantIndex = variants.Count;
                    groupResults.AddRange(staticResult.Value);
                    break;
                case CPlugDynaObjectModel dynaObjectModel:
                    var dynaResult = ExtractFromDynaObjectModel(dynaObjectModel);
                    if (dynaResult.IsFailure)
                        return ToolResult.Fail(dynaResult);
                    dynaResult.Value.group.VariantIndex = variants.Count;
                    groupResults.AddRange(dynaResult.Value);
                    break;
                default:
                    return ToolResult.Fail(nameof(MeshExtractor), ErrorCodes.MeshExtractor.UnsupportedMesh, variant.EntityModel);
            }
        }
        return ToolResult.Success((groupResults.ToArray(), variants.ToArray()), nameof(MeshExtractor));
    }

    ToolResult<NormalizedMesh> ExtractFromVisual(CPlugVisualIndexedTriangles visual, CPlugMaterialUserInst material)
    {
        var stream = visual.VertexStreams[0];

        var tangentUsField = typeof(CPlugVertexStream).GetField("tangentUs",
          BindingFlags.NonPublic | BindingFlags.Instance);
        var tangentsUs = (Vec3[])tangentUsField?.GetValue(stream)!;

        var tangentVsField = typeof(CPlugVertexStream).GetField("tangentVs",
          BindingFlags.NonPublic | BindingFlags.Instance);
        var tangentVs = (Vec3[])tangentVsField?.GetValue(stream)!;

        var properties = MeshProperties.Enabled | MeshProperties.Visible;
        if(material.SurfacePhysicId != CPlugSurface.MaterialId.NotCollidable)
            properties |= MeshProperties.Collidable;

        Vec2[]? texCoords = null;
        Vec2[]? lightmapCoords = null;

        bool hasUv0 = stream.UVs.TryGetValue(0, out var uv0);
        bool hasUv1 = stream.UVs.TryGetValue(1, out var uv1);
        if (material.Color is null || material.Color.Length == 0)
        {
            if(hasUv0)
                texCoords = uv0;
            if(hasUv1)
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
        var mesh = new NormalizedMesh
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
            Type = MeshType.Mesh,
            Properties = properties,
            Name = MatToName(material)
        };

        if (!_nodeRefTable.TryGetKey(visual, out var key))
            _nodeRefTable.Register(visual, out key);
        mesh.MeshLink = key;

        return ToolResult.Success(mesh, nameof(MeshExtractor));
    }


    ToolResult<(NormalizedMesh mesh, MeshGroup group)> ExtractFromTriggerSpecial(NPlugTrigger_SSpecial triggerSpecial)
    {
        var triggerShape = triggerSpecial.GetTriggerShape();
        if (triggerShape == null)
            return ToolResult.Fail(nameof(MeshExtractor), ErrorCodes.MeshExtractor.MissingTriggerShape);
        var result = ExtractFromSurface(triggerShape, out var gameplayMainDir);
        if(result.IsFailure)
            return ToolResult.Fail(result);
        result.Value.Type = MeshType.Trigger_Special;
        ushort gamplayIdShort = triggerShape.GetChunk<CPlugSurface.Chunk0900C003>()?.U02?.FirstOrDefault() ?? 0;
        var triggerGameplayId = ItemTriggerEffectConverter.ShortToGameplayId(gamplayIdShort);



        var group = new MeshGroup()
        {
            GroupType = GroupType.Trigger_Special,
            TriggerGameplayId = triggerGameplayId,
            GameplayMainDir = gameplayMainDir,
        };

        if (!_nodeRefTable.TryGetKey(triggerSpecial, out var key))
            _nodeRefTable.Register(triggerSpecial, out key);
        group.GroupLink = key;

        return ToolResult.Success((result.Value, group), nameof(MeshExtractor));
    }
    ToolResult<(NormalizedMesh mesh, MeshGroup group)> ExtractFromTriggerWaypoint(NPlugTrigger_SWaypoint triggerWaypoint)
    {
        var triggerShape = triggerWaypoint.GetTriggerShape();

        if (triggerShape == null)
            return ToolResult.Fail(nameof(MeshExtractor), ErrorCodes.MeshExtractor.MissingTriggerShape);
        var result = ExtractFromSurface(triggerShape, out var gameplayMainDir);
        if (result.IsFailure)
            return ToolResult.Fail(result);
        result.Value.Type = MeshType.Trigger_Waypoint;

        var group = new MeshGroup()
        {
            GroupType = GroupType.Trigger_Waypoint,
            WaypointType = (EWaypointType?)triggerWaypoint.Type,
            GameplayMainDir = gameplayMainDir,
            WaypointNoRespawn = triggerWaypoint.NoRespawn
        };

        if (!_nodeRefTable.TryGetKey(triggerWaypoint, out var key))
            _nodeRefTable.Register(triggerWaypoint, out key);
        group.GroupLink = key;


        return ToolResult.Success((result.Value, group), nameof(MeshExtractor));
    }
    ToolResult<NormalizedMesh> ExtractFromSurface(CPlugSurface surface, out Vec3 gameplayMainDir)
    {
        var mesh = new NormalizedMesh();
        var surf = surface.Surf as CPlugSurface.Mesh;
        gameplayMainDir = surf?.GameplayMainDir ?? new Vec3(0, 0, 1);
        mesh.Positions = surf?.Vertices.ToArray() ?? [];
        mesh.Indices = surf?.Triangles!.SelectMany(t => new[] { t.Indices.X, t.Indices.Y, t.Indices.Z }).ToArray() ?? [];
        mesh.Material = CreateErrorMat();
        mesh.SurfaceMaterialIds = surf?.Triangles!.Select(t => (MaterialId)t.U02).ToArray() ?? [];
        mesh.Name = MatToName(mesh.Material);
        mesh.Properties = MeshProperties.Enabled;

        if (!_nodeRefTable.TryGetKey(surface, out var key))
            _nodeRefTable.Register(surface, out key);
        mesh.MeshLink = key;

        return ToolResult.Success(mesh, nameof(MeshExtractor));
    }


    static Vec3[] ComputeSmoothNormals(Vec3[] positions, int[] indices)
    {
        var normals = new Vec3[positions.Length];

        for (int i = 0; i < indices.Length; i += 3)
        {
            var a = positions[indices[i]];
            var b = positions[indices[i + 1]];
            var c = positions[indices[i + 2]];

            // weighted by triangle area (cross product magnitude = 2x area)
            var faceNormal = Vec3.GetCrossProduct(b - a, c - a);

            normals[indices[i]] += faceNormal;
            normals[indices[i + 1]] += faceNormal;
            normals[indices[i + 2]] += faceNormal;
        }

        for (int i = 0; i < normals.Length; i++)
            if (normals[i] != Vec3.Zero)
                normals[i] = normals[i].GetNormalized();

        return normals;
    }
    static (Vec3[] tangentUs, Vec3[] tangentVs) ComputeTangents(
    Vec3[] positions, Vec3[] normals, Vec2[] uvs, int[] indices)
    {
        var tangents = new Vec3[positions.Length];
        var bitangents = new Vec3[positions.Length];

        for (int i = 0; i < indices.Length; i += 3)
        {
            int i0 = indices[i], i1 = indices[i + 1], i2 = indices[i + 2];

            var edge1 = positions[i1] - positions[i0];
            var edge2 = positions[i2] - positions[i0];
            var deltaUV1 = uvs[i1] - uvs[i0];
            var deltaUV2 = uvs[i2] - uvs[i0];

            float denom = deltaUV1.X * deltaUV2.Y - deltaUV2.X * deltaUV1.Y;
            if (MathF.Abs(denom) < 1e-6f) continue; // degenerate UV
            float r = 1f / denom;

            var tangent = (edge1 * deltaUV2.Y - edge2 * deltaUV1.Y) * r;
            var bitangent = (edge2 * deltaUV1.X - edge1 * deltaUV2.X) * r;

            tangents[i0] += tangent; tangents[i1] += tangent; tangents[i2] += tangent;
            bitangents[i0] += bitangent; bitangents[i1] += bitangent; bitangents[i2] += bitangent;
        }

        // orthogonalize against normal (Gram-Schmidt)
        for (int i = 0; i < positions.Length; i++)
        {
            var n = normals[i];
            var t = tangents[i];

            tangents[i] = (t - n * Vec3.GetDotProduct(n, t)).GetNormalized();
            bitangents[i] = (bitangents[i]).GetNormalized();
        }

        return (tangents, bitangents);
    }
    CPlugMaterialUserInst CreateErrorMat()
    {
        var mat = new CPlugMaterialUserInst()
        {
            MaterialName = "",
            Model = "",
            BaseTexture = "",
            
        };
        //{
        //    BaseTexture = "",
        //    Color = [],
        //    Csts = null,
        //    HidingGroup = "",
        //    IsNatural = false,
        //    IsUsingGameMaterial = false,
        //    Link = null,
        //    MaterialName = "",
        //    Model = "",
        //    SurfaceGameplayId = CPlugMaterialUserInst.GameplayId.None,
        //    SurfacePhysicId = CPlugSurface.MaterialId.Concrete,
        //    TextureSizeInMeters = 1,
        //    TilingU = CPlugMaterialUserInst.ETexAddress.Wrap,
        //    TilingV = CPlugMaterialUserInst.ETexAddress.Wrap,
        //    UserTextures = [],
        //};
        mat.TryCreateChunk<CPlugMaterialUserInst.Chunk090FD000>(out var c1);
        mat.TryCreateChunk<CPlugMaterialUserInst.Chunk090FD001>(out var c2);
        c2.U02 = 0;
        mat.TryCreateChunk<CPlugMaterialUserInst.Chunk090FD002>(out var c3);
        return mat;
    }

    string MatToName(CPlugMaterialUserInst mat)
    {
        if(!string.IsNullOrWhiteSpace(mat.MaterialName))
            return mat.MaterialName;
        if (!string.IsNullOrWhiteSpace(mat.Link))
            return string.Join("\\", mat.Link.Split('\\').TakeLast(2));
        return "Unknown Material";
    }

    /// <summary>
    /// Welds vertices that share the same position, texCoord and lightmapCoord.
    /// Returns remapped positions/UVs and a new index buffer.
    /// </summary>
    (Vec3[] positions, Vec2[] texCoords, Vec2[] lightmapCoords, Vec3[] normals, int[] indices)
        WeldVertices(Vec3[] positions, Vec2[]? texCoords, Vec2[]? lightmapCoords, Vec3[]? normals, int[] indices)
    {
        var map = new Dictionary<(Vec3 pos, Vec2 uv, Vec2 lm), int>();
        var newPositions = new List<Vec3>();
        var newTexCoords = new List<Vec2>();
        var newLightmap = new List<Vec2>();
        var newNormals = new List<Vec3>();
        var newIndices = new int[indices.Length];

        for (int i = 0; i < indices.Length; i++)
        {
            int src = indices[i];
            var key = (
                positions[src],
                texCoords?[src] ?? default,
                lightmapCoords?[src] ?? default
            );

            if (!map.TryGetValue(key, out int dst))
            {
                dst = newPositions.Count;
                map[key] = dst;
                newPositions.Add(positions[src]);
                newTexCoords.Add(texCoords?[src] ?? default);
                newLightmap.Add(lightmapCoords?[src] ?? default);
                newNormals.Add(normals?[src] ?? Vec3.Zero);
            }
            newIndices[i] = dst;
        }

        return (
            [.. newPositions],
            [.. newTexCoords],
            [.. newLightmap],
            [.. newNormals],
            newIndices
        );
    }

}
