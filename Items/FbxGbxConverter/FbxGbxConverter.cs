using Assimp;
using GBX.NET;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Plug;
using SixLabors.ImageSharp;
using TM_GenericMapping.Items.FbxGbxConversion.Serialization;
using TM_GenericMapping.Messaging;
using TM_GenericMapping.Templating;

namespace TM_GenericMapping.Items.FbxGbxConversion;

public class FbxGbxConverter
{
    MeshBuilder meshBuilder = new();

    public ToolResult<CGameItemModel> ConvertToGbx(
        FbxGbxConversionInput conversionInput,
        MeshBuilder.ItemModel targetModel = MeshBuilder.ItemModel.General)
    {

        var normalizedItemResult = ConvertToNormalizedItem(conversionInput);
        if (normalizedItemResult.IsFailure)
            return ToolResult.Fail(normalizedItemResult);

        var buildSettings = CreateBuildSettings(normalizedItemResult.Value, conversionInput);
        buildSettings.TargetModel = targetModel;
        var itemResult = meshBuilder.BuildItem(normalizedItemResult.Value, buildSettings);

        if(itemResult.IsFailure)
            return ToolResult.Fail(itemResult);

        SetItemMetaData(itemResult.Value, conversionInput);

        return ToolResult.Success(itemResult.Value, nameof(FbxGbxConverter));
    }

    public ToolResult<CGameItemModel> ConvertToGbxAndSaveItem(FbxGbxConversionInput conversionInput,
        MeshBuilder.ItemModel targetModel = MeshBuilder.ItemModel.General)
    {
        if(string.IsNullOrWhiteSpace(conversionInput.ItemOutputPath))
            return ToolResult.Fail(nameof(FbxGbxConverter), ErrorCodes.FbxGbxConverter.InvalidItemOutputPath);

        var itemResult = ConvertToGbx(conversionInput, targetModel);
        if (itemResult.IsFailure)
            return ToolResult.Fail(itemResult);

        Directory.CreateDirectory(Path.GetDirectoryName(conversionInput.ItemOutputPath));
        itemResult.Value.Save(conversionInput.ItemOutputPath);

        return itemResult;
    }
    public ToolResult<NormalizedItem> ConvertToNormalizedItem(FbxGbxConversionInput conversionInput)
    {
        var fbxSceneResult = FbxSceneReader.ParseFbx(conversionInput.Fbx);
        if(fbxSceneResult.IsFailure)
            return ToolResult.Fail(fbxSceneResult);

        return ConvertToNormalizedItem(fbxSceneResult.Value, conversionInput);
    }


    void SetItemMetaData(CGameItemModel item, FbxGbxConversionInput config)
    {

    }

    ToolResult<NormalizedItem> ConvertToNormalizedItem(Scene scene, FbxGbxConversionInput config)
    {
        var normalizedItem = new NormalizedItem();

        normalizedItem.PlacementParam = CreatePlacementParameters(config);

        List<NormalizedMesh> meshes = new List<NormalizedMesh>();
        List<NodeDefGroup> groups = new List<NodeDefGroup>();

        var solid2ModelTemplate = GbxTemplateLibrary.CreateCPlugSolid2ModelTemplate().Value;
        var materialConverter = new FbxMaterialConverter(config.MaterialLibrary, solid2ModelTemplate.CustomMaterials![0].MaterialUserInst!);

        var materialResults = materialConverter.ExtractMaterials(scene, config);
        if (materialResults.IsFailure)
            return ToolResult.Fail(materialResults);

        var nodeResults = FbxMeshConverter.ExtractNodes(scene, config);
        if (nodeResults.IsFailure)
            return ToolResult.Fail(nodeResults);

        var socketResults = FbxMeshConverter.ExtractSockets(scene, config);
        if (socketResults.IsFailure)
            return ToolResult.Fail(socketResults);
        if(socketResults.Value.Count > 1)
            return ToolResult.Fail(nameof(FbxGbxConverter), ErrorCodes.FbxGbxConverter.MultipleSocketsNotSupported);

        var lightResults = FbxLightConverter.ExtractLights(scene, config);
        if (lightResults.IsFailure)
            return ToolResult.Fail(lightResults);


        var nodes = FilterAndApplySpecialMeshItems(scene, nodeResults.Value, materialResults.Value);

        var groupResults = FbxMeshConverter.GroupNodes(nodes, socketResults.Value, config);
        if (groupResults.IsFailure)
            return ToolResult.Fail(groupResults);

        FbxLightConverter.GroupLights(lightResults.Value, groupResults.Value);

        var meshResults = FbxMeshConverter.ExtractMeshes(scene, groupResults.Value, materialResults.Value, nodes, config);
        if (meshResults.IsFailure)
            return ToolResult.Fail(meshResults);


        normalizedItem.Icon = FbxIconLoader.LoadIcon(config);

        if(config.ItemConfig.Name != null)
            normalizedItem.Name = config.ItemConfig.Name;
        else
            normalizedItem.Name = "Unnamed Item";
        if(config.ItemConfig.Description != null)
            normalizedItem.Description = config.ItemConfig.Description;
        else
            normalizedItem.Description = "No Description";

        normalizedItem.Groups = groupResults.Value.ToArray();
        normalizedItem.Meshes = meshResults.Value.ToArray();
        normalizedItem.Lights = lightResults.Value.Select(lr => lr.Light).ToArray();

        return ToolResult.Success(normalizedItem, nameof(FbxGbxConverter));
    }


    List<NodeDef> FilterAndApplySpecialMeshItems(Scene scene, IEnumerable<NodeDef> nodes, List<MaterialDef> materials)
    {
        List<NodeDef> nodesWithMesh = new List<NodeDef>();

        foreach (var nodeDef in nodes)
        {
            if (nodeDef.NodeConfig.MeshFlags.HasMeshData())
            {
                // has meshes with valid materials
                if (nodeDef.Node.MeshIndices.Any(mi => materials[scene.Meshes[mi].MaterialIndex]?.MaterialInstance != null))
                    nodesWithMesh.Add(nodeDef);
            }
        }


        return nodesWithMesh;
    }



    //-------------------------------------
    // Placement Parameter
    //-------------------------------------
    CGameItemPlacementParam CreatePlacementParameters(FbxGbxConversionInput config)
    {
        var placementParamsTemplate = config.ItemConfig.PlacementParams?.PlacementClass == null ? 
            GbxTemplateLibrary.CreatePlacementParamTemplate() : 
            GbxTemplateLibrary.CreatePlacementParamTemplateWithPlacementClass();

        if(config.ItemConfig.PlacementParams != null)
            placementParamsTemplate.InjectData(config.ItemConfig.PlacementParams);


        return placementParamsTemplate;
    }

    //-------------------------------------
    // Build Settings
    //-------------------------------------
    MeshBuilder.BuildSettings CreateBuildSettings(NormalizedItem normalizedItem, FbxGbxConversionInput conversionInput)
    {
        var buildSettings = MeshBuilder.BuildSettings.DefaultFromMesh(normalizedItem);

        for (int i = 0; i < buildSettings.MeshSettings.Count; i++)
        {
            var meshSetting = buildSettings.MeshSettings[i];
            var mesh = normalizedItem.Meshes[meshSetting.MeshIndex];
            var groupSetting = buildSettings.GroupSettings.FirstOrDefault(b => b.GroupId == mesh.GroupIndex);
            switch (groupSetting.Type)
            {
                case GroupType.StaticObject:
                    break;
                case GroupType.DynaObject:
                    meshSetting.Movable = true;
                    break;
                case GroupType.Trigger_Special:
                    meshSetting.Trigger = true;
                    break;
                case GroupType.Trigger_Waypoint:
                    meshSetting.Trigger = true;
                    break;
            }
        }
        return buildSettings;
    }

}
