using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Scene;
using Silk.NET.Assimp;
using Silk.NET.Maths;
using System.Numerics;
using TM_GenericMapping.Common;
using TM_GenericMapping.Items.FbxGbxConversion.Importing;
using TM_GenericMapping.Items.FbxGbxConversion.Serialization;
using TM_GenericMapping.Items.MeshCompilation;
using TM_GenericMapping.Messaging;
using static GBX.NET.Engines.Game.CGameCtnChallenge;
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
        var sceneResult = ImportScene(conversionInput);
        if (sceneResult.IsFailure)
            return ToolResult.Fail(sceneResult);

        return ConvertToNormalizedItem(sceneResult.Value, conversionInput);
    }

    ToolResult<ImportedScene> ImportScene(FbxGbxConversionInput conversionInput)
    {
        try 
        { 
            var importer = CreateSceneImporter(conversionInput.ItemConfig.Scale);
            var scene = importer.Import(conversionInput.Fbx);
            importer.FixConfigAxes(conversionInput.ItemConfig);
            return ToolResult.Success(scene, nameof(FbxGbxConverter));
        }
        catch (Exception e)
        {
            return ToolResult.Fail<ImportedScene>(nameof(FbxGbxConverter), ErrorCodes.FbxGbxConverter.FbxParsingError, e);
        }
    }
    protected virtual ISceneImporter CreateSceneImporter(float scale) => new SilkAssimpSceneImporter(scale);

    ToolResult<NormalizedItem> ConvertToNormalizedItem(ImportedScene scene, FbxGbxConversionInput config)
    {
        var item = new NormalizedItem();

        var res = ExtractAndGroupFbxData(scene, config, 
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

        item.PlacementParam = CreatePlacementParameters(config);

        return ToolResult.Success(item, nameof(FbxGbxConverter));
    }

    ToolResult<None> ExtractAndGroupFbxData(
        ImportedScene scene,
        FbxGbxConversionInput config,
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
        var materialResults = materialConverter.ExtractMaterials(scene, config);
        if (materialResults.IsFailure)
            return ToolResult.Fail(materialResults);
        materials = materialResults.Value;

        var allSceneNodes = scene.CollectNodes();

        var meshNodeResults = FbxMeshConverter.ExtractMeshNodes(scene, config);
        if (meshNodeResults.IsFailure)
            return ToolResult.Fail(meshNodeResults);

        var socketResults = FbxMeshConverter.ExtractSockets(scene, config);
        if (socketResults.IsFailure)
            return ToolResult.Fail(socketResults);
        if (socketResults.Value.Count > 1)
            return ToolResult.Fail(nameof(FbxGbxConverter), ErrorCodes.FbxGbxConverter.MultipleSocketsNotSupported);
        sockets = socketResults.Value;

        var lightResults = FbxLightConverter.ExtractLights(scene, config);
        if (lightResults.IsFailure)
            return ToolResult.Fail(lightResults);
        lights = lightResults.Value;

        nodes = FilterAndApplySpecialMeshItems(scene, meshNodeResults.Value, materialResults.Value);

        var groupResults = FbxMeshConverter.GroupNodes(nodes, allSceneNodes, socketResults.Value, config);
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


    public ToolResult<FbxGbxPlanReport> PlanConversion(FbxGbxConversionInput conversionInput, CompileOptions? compileOptions = null)
    {
        var sceneResult = ImportScene(conversionInput);
        if (sceneResult.IsFailure)
            return ToolResult.Fail(sceneResult);
        var scene = sceneResult.Value;

        var res = ExtractAndGroupFbxData(scene, conversionInput,
              out var groups,
              out var lights,
              out var sockets,
              out var nodes,
              out var materials);
        if (res.IsFailure)
            return ToolResult.Fail(res);

        nodes = nodes.OrderBy(n => n.Node.Name, StringComparer.Ordinal).ToList();
        groups = groups.OrderBy(g => g.GroupKey, StringComparer.Ordinal).ToList();

        var report = new FbxGbxPlanReport(1, groups.Select(g => new FbxGbxPlanGroup(
                g.GroupKey,
                g.Type.ToString(),
                g.OriginalGroupId,
                g.LodDistances.ToArray(),
                g.Nodes.Select(a => new FbxGbxPlanNode(a.NodeDef.Node.Name, a.LodIndices.ToArray())).ToArray()
            )).ToArray());

        return ToolResult.Success(report, nameof(FbxGbxConverter));
    }
}
