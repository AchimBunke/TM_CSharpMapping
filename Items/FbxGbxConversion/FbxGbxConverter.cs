using GBX.NET.Engines.GameData;
using Silk.NET.Maths;
using System.Numerics;
using TM_GenericMapping.Common;
using TM_GenericMapping.Items.FbxGbxConversion.Importing;
using TM_GenericMapping.Items.FbxGbxConversion.Serialization;
using TM_GenericMapping.Items.MeshCompilation;
using TM_GenericMapping.Messaging;
using static GBX.NET.Engines.Plug.CPlugCrystal;

namespace TM_GenericMapping.Items.FbxGbxConversion;

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
        ImportedScene scene;
        try
        {
            var importer = CreateSceneImporter(conversionInput.ItemConfig.Scale);
            scene = importer.Import(conversionInput.Fbx);
        }
        catch (Exception e)
        {
            return ToolResult.Fail<NormalizedItem>(nameof(FbxGbxConverter), ErrorCodes.FbxGbxConverter.FbxParsingError, e);
        }

        return ConvertToNormalizedItem(scene, conversionInput);
    }

    protected virtual ISceneImporter CreateSceneImporter(float scale) => new SilkAssimpSceneImporter(scale);

    ToolResult<NormalizedItem> ConvertToNormalizedItem(ImportedScene scene, FbxGbxConversionInput config)
    {
        var item = new NormalizedItem();

        var materialConverter = new FbxMaterialConverter(config.MaterialLibrary);
        var materialResults = materialConverter.ExtractMaterials(scene, config);
        if (materialResults.IsFailure)
            return ToolResult.Fail(materialResults);

        var nodeResults = FbxMeshConverter.ExtractMeshNodes(scene, config);
        if (nodeResults.IsFailure)
            return ToolResult.Fail(nodeResults);

        var socketResults = FbxMeshConverter.ExtractSockets(scene, config);
        if (socketResults.IsFailure)
            return ToolResult.Fail(socketResults);
        if (socketResults.Value.Count > 1)
            return ToolResult.Fail(nameof(FbxGbxConverter), ErrorCodes.FbxGbxConverter.MultipleSocketsNotSupported);

        var lightResults = FbxLightConverter.ExtractLights(scene, config);
        if (lightResults.IsFailure)
            return ToolResult.Fail(lightResults);

        var nodes = FilterAndApplySpecialMeshItems(scene, nodeResults.Value, materialResults.Value);

        var groupResults = FbxMeshConverter.GroupNodes(nodes, nodeResults.Value, socketResults.Value, config);
        if (groupResults.IsFailure)
            return ToolResult.Fail(groupResults);

        var groups = groupResults.Value;

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
                var parentIndex = groups
                    .Select((g, idx) => (g, idx))
                    .Where(t => t.g.Type == ModelType.Dynamic)
                    .FirstOrDefault(t => t.g.OriginalGroupId == group.RelativeMovingParentGroupId).idx;

                groupIndexToEntityRef[i].RelativeMovingParentKey = groupIndexToModelKey[parentIndex];
            }
        }

        var meshResult = FbxMeshConverter.ExtractMeshes(scene, groups, materialResults.Value, nodes, config, item,
            groupIndexToModelKey, groupIndexToAnchorPosition, groupIndexToAnchorRotation);
        if (meshResult.IsFailure)
            return ToolResult.Fail(meshResult);

        AssignLightsToGroups(item, lightResults.Value, groups, groupIndexToModelKey, groupIndexToAnchorPosition, groupIndexToAnchorRotation);

        SetMetaData(item, config);

        item.PlacementParam = CreatePlacementParameters(config);

        return ToolResult.Success(item, nameof(FbxGbxConverter));
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
        return PlacementConfig.ToPlacementParam(config.ItemConfig.PlacementParams);
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
}
