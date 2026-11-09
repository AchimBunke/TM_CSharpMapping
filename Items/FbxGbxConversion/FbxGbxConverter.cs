using GBX.NET.Engines.GameData;
using System.Numerics;
using TM_GenericMapping.Items.FbxGbxConversion.Importing;
using TM_GenericMapping.Items.FbxGbxConversion.Serialization;
using TM_GenericMapping.Items.MeshCompilation;
using TM_GenericMapping.Messaging;
using static GBX.NET.Engines.Plug.NPlugModelKit_SDataBase;

namespace TM_GenericMapping.Items.FbxGbxConversion;

internal class ConfigIndex
{
    public Dictionary<string, MeshConfig> MeshConfigsByName { get; set; } = new Dictionary<string, MeshConfig>();
    public Dictionary<string, MaterialConfig> MaterialConfigsByName { get; set; } = new Dictionary<string, MaterialConfig>();
    public Dictionary<string, LightConfig> LightConfigsByName { get; set; } = new Dictionary<string, LightConfig>();
    public Dictionary<string, InstanceCollectionConfig> InstanceCollectionConfigsByName { get; set; } = new Dictionary<string, InstanceCollectionConfig>();
    public Dictionary<string, InstanceConfig> InstanceConfigsByName { get; set; } = new Dictionary<string, InstanceConfig>();
    public Dictionary<string, MovingGroupConfig> MovingGroupConfigsByName { get; set; } = new Dictionary<string, MovingGroupConfig>();

    public Dictionary<string,string> OriginalNodes { get; set; } = new Dictionary<string, string>();

    public ItemConfig ItemConfig { get; set; } = null!;

    private ConfigIndex()
    {
    }

    public static ConfigIndex FromItemConfig(ItemConfig config)
    {
        var index = new ConfigIndex();
        index.MeshConfigsByName = config.Meshes.ToDictionary(m => m.Name, m => m);
        index.MaterialConfigsByName = config.Materials.ToDictionary(m => m.Name, m => m);
        index.LightConfigsByName = config.Lights.ToDictionary(m => m.Name, m => m);
        index.InstanceCollectionConfigsByName = config.InstanceCollections.ToDictionary(ic => ic.Name, ic => ic);
        index.InstanceConfigsByName = config.Instances.ToDictionary(i => i.Name, i => i);
        index.MovingGroupConfigsByName = config.MovingGroups.ToDictionary(mg => mg.MovingGroupId, mg => mg);
        index.ItemConfig = config;
        return index;
    }
}

/// <summary>
/// Converts FBX scenes into <see cref="NormalizedItem"/>/<see cref="CGameItemModel"/> using the new
/// position/rotation hierarchy item model. Unlike the legacy <see cref="FbxGbxConverter"/>, mesh vertices
/// are NOT baked into a shared world anchor - each group's meshes are expressed relative to the group's own
/// anchor transform (position AND rotation), and that anchor is stored on the group's EntityRef, since
/// NormalizedItem natively supports a position/rotation hierarchy.
/// </summary>
public class FbxGbxConverter
{

    public ToolResult<CGameItemModel> ConvertToGbx(FbxGbxConversionInput conversionInput, CompileOptions? compileOptions = null)
    {
        var normalizedItemResult = ConvertToNormalizedItem(conversionInput);
        if (normalizedItemResult.IsFailure)
            return ToolResult.Fail(normalizedItemResult);

        var buildSettings = BuildSettings.DefaultFromItem(normalizedItemResult.Value);
        var compiler = new ItemCompiler();
        var itemResult = compiler.Compile(normalizedItemResult.Value, buildSettings, compileOptions ?? new CompileOptions());

        if (itemResult.IsFailure)
            return ToolResult.Fail(itemResult);

        return ToolResult.Success(itemResult.Value, nameof(FbxGbxConverter));
    }

    public ToolResult<CGameItemModel> ConvertToGbxAndSaveItem(FbxGbxConversionInput conversionInput, CompileOptions? compileOptions = null)
    {
        if (string.IsNullOrWhiteSpace(conversionInput.ItemOutputPath))
            return ToolResult.Fail(nameof(FbxGbxConverter), ErrorCodes.FbxGbxConverter.InvalidItemOutputPath);

        var itemResult = ConvertToGbx(conversionInput, compileOptions);
        if (itemResult.IsFailure)
            return ToolResult.Fail(itemResult);

        Directory.CreateDirectory(Path.GetDirectoryName(conversionInput.ItemOutputPath)!);
        itemResult.Value.Save(conversionInput.ItemOutputPath);

        return itemResult;
    }

    public ToolResult<NormalizedItem> ConvertToNormalizedItem(FbxGbxConversionInput conversionInput)
    {
        ConfigIndex configIndex = ConfigIndex.FromItemConfig(conversionInput.ItemConfig);
        var sceneResult = ImportScene(conversionInput, configIndex);
        if (sceneResult.IsFailure)
            return ToolResult.Fail(sceneResult);

        return ConvertToNormalizedItem(sceneResult.Value, conversionInput, configIndex);
    }

    ToolResult<ImportedScene> ImportScene(FbxGbxConversionInput conversionInput, ConfigIndex configIndex)
    {
        try 
        { 
            var importer = CreateSceneImporter(conversionInput.ItemConfig.Scale);
            var scene = importer.Import(conversionInput.Fbx);
            importer.FixConfigAxes(conversionInput.ItemConfig);
            FixConfigScale(conversionInput.ItemConfig);
            var res = ResolveInstances(scene, configIndex);
            if (res.IsFailure)
                return ToolResult.Fail(res);
            return ToolResult.Success(scene, nameof(FbxGbxConverter));
        }
        catch (Exception e)
        {
            return ToolResult.Fail<ImportedScene>(nameof(FbxGbxConverter), ErrorCodes.FbxGbxConverter.FbxParsingError, e);
        }
    }
    protected virtual ISceneImporter CreateSceneImporter(float scale) => new SilkAssimpSceneImporter(scale);

    ToolResult<NormalizedItem> ConvertToNormalizedItem(ImportedScene scene, FbxGbxConversionInput config, ConfigIndex configIndex)
    {
        var item = new NormalizedItem();

        var res = ExtractAndGroupFbxData(scene, config, configIndex,
            out var groups, 
            out var lights, 
            out var sockets, 
            out var nodes, 
            out var materials);
        if (res.IsFailure)
            return ToolResult.Fail(res);

        // Root container model that references every group as a child EntityRef.
        var rootModel = new NormalizedModel { Type = ModelType.Container };
        int rootKey = 0;
        item.ModelPool[rootKey] = rootModel;
        item.Model = rootModel;

        var groupIndexToModelKey = new Dictionary<int, int>();
        var groupIndexToAnchorPosition = new Dictionary<int, Vector3>();
        var groupIndexToAnchorRotation = new Dictionary<int, Quaternion>();
        var groupIndexToEntityRef = new Dictionary<int, EntityRef>();
        var groupKeyToIndex = new Dictionary<string, int>();

        for (int i = 0; i < groups.Count; i++)
        {
            var group = groups[i];
            groupKeyToIndex[group.GroupKey] = i;

            var (anchorPos, anchorRot) = FbxMeshConverter.ComputeGroupAnchor(group);
            groupIndexToAnchorPosition[i] = anchorPos;
            groupIndexToAnchorRotation[i] = anchorRot;

            if(group.GameplayMainDir.HasValue)
            {
                Vector3 mainDir = group.GameplayMainDir.Value;
                var rotationAdjustedGameplayDir = Vector3.Transform(mainDir, Quaternion.Inverse(anchorRot));
                group.GameplayMainDir = rotationAdjustedGameplayDir;
            }
            var model = new NormalizedModel
            {
                Type = group.Type,
                LODDistances = group.LodDistances.ToArray(),
                WaypointType = group.WaypointType,
                WaypointNoRespawn = group.WaypointNoRespawn,
                TriggerGameplayId = group.TriggerGameplayId,
                GameplayMainDir = group.GameplayMainDir,
            };

            int modelKey = item.ModelPool.Count == 0 ? 0 : item.ModelPool.Keys.Max() + 1;
            item.ModelPool[modelKey] = model;
            groupIndexToModelKey[i] = modelKey;

            var entityRef = new EntityRef
            {
                ModelKey = modelKey,
                Position = anchorPos,
                Rotation = anchorRot,
                KinematicConstraint = group.KinematicConstraint,
                DynaObjectModelParams = group.DynaObjectModelParams,
            };

            if (group.WaypointSpawnModel is not null)
            {
                var spawnLoc = group.WaypointSpawnModel.SpawnModel.Loc;
                entityRef.WaypointSpawnPosition = new Vector3(spawnLoc.TX, spawnLoc.TY, spawnLoc.TZ);
                entityRef.WaypointSpawnRotation = Quaternion.CreateFromRotationMatrix(new Matrix4x4(
                    spawnLoc.XX, spawnLoc.XY, spawnLoc.XZ, 0,
                    spawnLoc.YX, spawnLoc.YY, spawnLoc.YZ, 0,
                    spawnLoc.ZX, spawnLoc.ZY, spawnLoc.ZZ, 0,
                    0, 0, 0, 1));
            }

            groupIndexToEntityRef[i] = entityRef;
            rootModel.Children.Add(entityRef);
        }

        // resolve relative moving parents now that all groups have model keys
        for (int i = 0; i < groups.Count; i++)
        {
            var group = groups[i];
            if (!string.IsNullOrEmpty(group.RelativeMovingParentGroupId))
            {
                for(int groupIdx = 0; groupIdx < groups.Count; groupIdx++)
                {
                    if(groups[groupIdx].OriginalGroupId == group.RelativeMovingParentGroupId)
                    {
                        var parentIndex = groupIdx;
                        groupIndexToEntityRef[i].RelativeMovingParentKey = groupIndexToModelKey[parentIndex];
                        break;
                    }
                }
            }
        }

        var meshResult = FbxMeshConverter.ExtractMeshes(scene, groups, materials, nodes, config, item,
            groupIndexToModelKey, groupIndexToAnchorPosition, groupIndexToAnchorRotation);
        if (meshResult.IsFailure)
            return ToolResult.Fail(meshResult);

        AssignLightsToGroups(item, lights, groups, groupIndexToModelKey, groupIndexToAnchorPosition, groupIndexToAnchorRotation);

        SetMetaData(item, config);

        SetAttachmentLocations(item, rootModel, config);

        item.PlacementParam = CreatePlacementParameters(config);

        return ToolResult.Success(item, nameof(FbxGbxConverter));
    }

    ToolResult<None> ExtractAndGroupFbxData(
        ImportedScene scene,
        FbxGbxConversionInput config,
        ConfigIndex configIndex,
        out List<NodeDefGroup> groups,
        out List<LightDef> lights,
        out List<SocketDef> sockets,
        out List<NodeDef> nodes,
        out List<MaterialDef> materials)
    {
        groups = [];
        lights = [];
        sockets = [];
        nodes = [];
        materials = [];

        var materialConverter = new FbxMaterialConverter(config.MaterialLibrary);
        var materialResults = materialConverter.ExtractMaterials(scene, configIndex);
        if (materialResults.IsFailure)
            return ToolResult.Fail(materialResults);
        materials = materialResults.Value;

        var allSceneNodes = scene.CollectNodes();

        var meshNodeResults = FbxMeshConverter.ExtractMeshNodes(scene, configIndex);
        if (meshNodeResults.IsFailure)
            return ToolResult.Fail(meshNodeResults);

        var socketResults = FbxMeshConverter.ExtractSockets(scene, configIndex);
        if (socketResults.IsFailure)
            return ToolResult.Fail(socketResults);
        if (socketResults.Value.Count > 1)
            return ToolResult.Fail(nameof(FbxGbxConverter), ErrorCodes.FbxGbxConverter.MultipleSocketsNotSupported);
        sockets = socketResults.Value;

        var lightResults = FbxLightConverter.ExtractLights(scene, configIndex);
        if (lightResults.IsFailure)
            return ToolResult.Fail(lightResults);
        lights = lightResults.Value;

        nodes = FilterAndApplySpecialMeshItems(scene, meshNodeResults.Value, materialResults.Value);

        var groupResults = FbxMeshConverter.GroupNodes(nodes, allSceneNodes, socketResults.Value, configIndex);
        if (groupResults.IsFailure)
            return ToolResult.Fail(groupResults);
        groups = groupResults.Value;

        return ToolResult.Success(nameof(FbxGbxConverter));

    }

    void AssignLightsToGroups(
        NormalizedItem item,
        List<LightDef> lights,
        List<NodeDefGroup> groups,
        Dictionary<int, int> groupIndexToModelKey,
        Dictionary<int, Vector3> groupIndexToAnchorPosition,
        Dictionary<int, Quaternion> groupIndexToAnchorRotation)
    {
        int firstStaticGroupIndex = groups.FindIndex(g => g.Type == ModelType.Static);
        if (firstStaticGroupIndex < 0)
            firstStaticGroupIndex = 0;

        if (groups.Count == 0 || lights.Count == 0)
            return;

        var anchorPos = groupIndexToAnchorPosition[firstStaticGroupIndex];
        var anchorRot = groupIndexToAnchorRotation[firstStaticGroupIndex];
        var modelKey = groupIndexToModelKey[firstStaticGroupIndex];
        var model = item.ModelPool[modelKey];

        var anchorInverseRotation = Quaternion.Inverse(anchorRot);
        foreach (var lightDef in lights)
        {
            int lightKey = item.LightPool.Count == 0 ? 0 : item.LightPool.Keys.Max() + 1;
            item.LightPool[lightKey] = lightDef.Light;

            var localPos = Vector3.Transform(lightDef.Position - anchorPos, anchorInverseRotation);
            var localRot = lightDef.Rotation * anchorRot;   

            model.Lights.Add(new LightRef
            {
                LightKey = lightKey,
                Position = localPos,
                Rotation = localRot,
            });
        }
    }

    void SetMetaData(NormalizedItem item, FbxGbxConversionInput config)
    {
        item.Icon = FbxIconLoader.LoadIcon(config);
        item.Name = !string.IsNullOrWhiteSpace(config.ItemConfig.Name) ? config.ItemConfig.Name! : "Unnamed Item";
        item.Description = !string.IsNullOrWhiteSpace(config.ItemConfig.Description) ? config.ItemConfig.Description! : "No Description";
        item.WaypointType = config.ItemConfig.Waypoint?.Type ?? GBX.NET.Engines.GameData.CGameItemModel.EWaypointType.None;
    }
    CGameItemPlacementParam CreatePlacementParameters(FbxGbxConversionInput config)
    {
        return PlacementConfig.ToPlacementParam(config.ItemConfig.Placement);
    }

    void SetAttachmentLocations(NormalizedItem item, NormalizedModel containerModel, FbxGbxConversionInput config)
    {
        var attachmentLocations = config.ItemConfig.CreateAttachmentLocations();
        if (attachmentLocations.Count > 0)
        {
            var attachmentModel = new NormalizedModel { Type = ModelType.ItemAttachmentGroup };
            attachmentModel.AttachmentGroup = attachmentLocations;

            int modelKey = item.ModelPool.Count == 0 ? 0 : item.ModelPool.Keys.Max() + 1;
            item.ModelPool[modelKey] = attachmentModel;

            containerModel.Children.Add(new EntityRef()
            {
                ModelKey = modelKey,
                Position = Vector3.Zero,
                Rotation = Quaternion.Identity,
            });
        }
    }
    List<NodeDef> FilterAndApplySpecialMeshItems(ImportedScene scene, IEnumerable<NodeDef> nodes, List<MaterialDef> materials)
    {
        List<NodeDef> nodesWithMesh = new List<NodeDef>();

        foreach (var nodeDef in nodes)
        {
            if (nodeDef.NodeConfig.MeshFlags.HasMeshData())
            {
                if (nodeDef.Node.MeshIndices.Any(mi => materials[scene.Meshes[mi].MaterialIndex]?.MaterialInstance != null))
                    nodesWithMesh.Add(nodeDef);
            }
        }

        return nodesWithMesh;
    }

    void FixConfigScale(ItemConfig itemConfig)
    {
        if (itemConfig.Scale == 1)
            return;
        if (itemConfig.ScaleOptions == null)
            return;
        var scale = itemConfig.Scale;
        var scaleOptions = itemConfig.ScaleOptions;

        if (scaleOptions.ScaleMagnets)
        {
            for (int i = 0; i < itemConfig.Placement?.MagnetPositions?.Count; i++)
                itemConfig.Placement.MagnetPositions[i] = Vector3.Multiply(itemConfig.Placement.MagnetPositions[i], scale);
        }
        if (scaleOptions.ScalePivots)
        {
            for (int i = 0; i < itemConfig.Placement?.PivotPositions?.Count; i++)
                itemConfig.Placement.PivotPositions[i] = Vector3.Multiply(itemConfig.Placement.PivotPositions[i], scale);
        }
        if (scaleOptions.ScaleAnimationDistance)
        {
            foreach(var mg in itemConfig.MovingGroups)
            {
                if (mg.KinematicMovement is null)
                    continue;
                mg.KinematicMovement.TransMin = mg.KinematicMovement.TransMin * scale;
                mg.KinematicMovement.TransMax = mg.KinematicMovement.TransMax * scale;
            }
        }
        if (scaleOptions.ScaleAnimationTime)
        {
            foreach (var mg in itemConfig.MovingGroups)
            {
                if (mg.KinematicMovement is null)
                    continue;
                foreach(var anim in mg.KinematicMovement.TranslationAnims)
                {
                    anim.Duration = (uint)(anim.Duration * scale);
                }
                foreach (var anim in mg.KinematicMovement.RotationAnims)
                {
                    anim.Duration = (uint)(anim.Duration * scale);
                }
                if (mg.KinematicMovement.MovingTexture is null)
                    continue;
                foreach(var t in mg.KinematicMovement.MovingTexture.TextureAnims)
                {
                    t.Duration = (uint)(t.Duration * scale);
                }
            }
        }
        if (scaleOptions.ScaleLightRadius)
        {
            foreach (var light in itemConfig.Lights)
            {
                light.Distance = light.Distance * scale;
            }
        }
        if (scaleOptions.ScaleLightmapSizeOverrides)
        {
           foreach(var mesh in itemConfig.Meshes)
           {
                if(mesh.LightmapSize.HasValue)
                    mesh.LightmapSize = mesh.LightmapSize * scale;
           }
        }

    }

    ToolResult<None> ResolveInstances(ImportedScene scene, ConfigIndex configIndex)
    {
        ItemConfig itemConfig = configIndex.ItemConfig;

        void FlattenNestedInstanceCollections(InstanceConfig instanceConfig, List<string> meshes, List<string> lights)
        {
            if (!configIndex.InstanceCollectionConfigsByName.TryGetValue(instanceConfig.Collection, out var instanceCollection))
                return;
            
            meshes.AddRange(instanceCollection.Meshes);
            lights.AddRange(instanceCollection.Lights);
            foreach (var nestedInstanceName in instanceCollection.Instances)
            {
                var nestedInstance = configIndex.InstanceConfigsByName[nestedInstanceName];
                FlattenNestedInstanceCollections(nestedInstance, meshes, lights);
            }
        }

        ToolResult<None> Visit(ImportedNode node)
        {
            if (configIndex.InstanceConfigsByName.TryGetValue(node.Name, out var instanceDef))
            {
                if (!configIndex.InstanceCollectionConfigsByName.TryGetValue(instanceDef.Collection, out var instanceCollection))
                    return ToolResult.Fail(nameof(FbxGbxConverter), ErrorCodes.FbxGbxConverter.MissingInstanceCollection);

                List<string> flatMeshes = new List<string>();
                List<string> flatLights = new List<string>();
                FlattenNestedInstanceCollections(instanceDef, flatMeshes, flatLights);

                foreach (var meshName in flatMeshes)
                {
                    var meshNode = node.Children.FirstOrDefault(n => n.Name.StartsWith($"{instanceDef.Name}|{meshName}|"));
                    if (meshNode is null)
                        continue; // should not happen
                    if (!configIndex.MeshConfigsByName.TryGetValue(meshName, out var templateConfig))
                        return ToolResult.Fail(nameof(FbxGbxConverter), ErrorCodes.FbxGbxConverter.MissingMeshConfig);
                    var newConfig = templateConfig.Clone();
                    newConfig.Name = meshNode.Name;
                    configIndex.OriginalNodes[newConfig.Name] = templateConfig.Name;
                    // TODO: overrides
                    newConfig.Lods = instanceDef.Lods;
                    newConfig.Group = instanceDef.Name;
                    if(!string.IsNullOrEmpty(instanceDef.MovingGroup))
                        newConfig.MovingGroup = instanceDef.MovingGroup;
                    if(!string.IsNullOrEmpty(newConfig.MovingGroup))
                        newConfig.MeshFlags |= MeshFlags.Moving;

                    configIndex.MeshConfigsByName.Add(newConfig.Name, newConfig);
                }
                foreach (var lightName in instanceCollection.Lights)
                {
                    var lightNode = node.Children.FirstOrDefault(n => n.Name.StartsWith($"{instanceDef.Name}|{lightName}|"));
                    if (lightNode is null)
                        continue; // should not happen
                    if (!configIndex.LightConfigsByName.TryGetValue(lightName, out var templateConfig))
                        return ToolResult.Fail(nameof(FbxGbxConverter), ErrorCodes.FbxGbxConverter.MissingLightConfig);
                    var newConfig = templateConfig.Clone();
                    configIndex.OriginalNodes[newConfig.Name] = templateConfig.Name;
                    newConfig.Name = lightNode.Name;
                    // TODO: overrides
                    configIndex.LightConfigsByName.Add(newConfig.Name, newConfig);
                }

            }
            foreach (var child in node.Children)
            {
                var res = Visit(child);
                if (res.IsFailure)
                    return res;
            }

            return ToolResult.Success(nameof(FbxGbxConverter));
        }
        return Visit(scene.RootNode);
    }

    public ToolResult<FbxGbxPlanReport> PlanConversion(FbxGbxConversionInput conversionInput, CompileOptions? compileOptions = null)
    {
        var configIndex = ConfigIndex.FromItemConfig(conversionInput.ItemConfig);
        var sceneResult = ImportScene(conversionInput, configIndex);
        if (sceneResult.IsFailure)
            return ToolResult.Fail(sceneResult);
        var scene = sceneResult.Value;

        var res = ExtractAndGroupFbxData(scene, conversionInput, configIndex,
              out var groups,
              out var lights,
              out var sockets,
              out var nodes,
              out var materials);
        if (res.IsFailure)
            return ToolResult.Fail(res);

        //groups = groups.OrderBy(g => g.GroupKey, StringComparer.Ordinal).ToList();

        var report = new FbxGbxPlanReport(1, groups.Select(g => new FbxGbxPlanGroup(
                g.GroupKey,
                g.Type.ToString(),
                PlannedLightmapSize(g),
                g.LodDistances.ToArray(),
                g.Nodes.Select(a => new FbxGbxPlanNode(
                    a.NodeDef.Node.Name,
                    configIndex.OriginalNodes.TryGetValue(a.NodeDef.Node.Name, out var originalName) ? originalName : null,
                    a.LodIndices.ToArray())).ToArray()
            )).ToArray());

        return ToolResult.Success(report, nameof(FbxGbxConverter));
    }

    float? PlannedLightmapSize(NodeDefGroup group)
    {
        float? lightmapSize = null;
        foreach (var node in group.Nodes)
        {
            if(node.NodeDef.NodeConfig.LightmapSize.HasValue)
            {
                if (lightmapSize.HasValue)
                    lightmapSize = Math.Max(lightmapSize.Value, node.NodeDef.NodeConfig.LightmapSize.Value);
                else
                    lightmapSize = node.NodeDef.NodeConfig.LightmapSize.Value;
            }
        }
        return lightmapSize;
    }
}
