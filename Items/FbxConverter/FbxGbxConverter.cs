using Assimp;
using Assimp.Configs;
using GBX.NET;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Plug;
using GBX.NET.Engines.Scene;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Numerics;
using System.Xml.Serialization;
using TM_GenericMapping.Common;
using TM_GenericMapping.Items.FbxConverter.Serialization;
using TM_GenericMapping.Messaging;

namespace TM_GenericMapping.Items.FbxConverter;

public class FbxGbxConverter
{
    const string EntityModelTemplatePath = @"EntityModelTemplate.Item.Gbx";
    CGameItemModel entityModelTemplate = null!;
    CGameItemModel EntityModelTemplate => (entityModelTemplate ??= Gbx.Parse<CGameItemModel>(TemplateLoader.GetTemplate(EntityModelTemplatePath)));
    CGameCommonItemEntityModel CommonItemEntityModelTemplate => (EntityModelTemplate.EntityModel as CGameCommonItemEntityModel)!;
    CPlugSolid2Model Solid2ModelTemplate => CommonItemEntityModelTemplate.StaticObject!.Mesh!;

    MeshBuilder meshBuilder = new();



    public ToolResult<CGameItemModel> ConvertToGbx(FbxGbxConversionInput conversionInput)
    {

        var fbxSceneResult = FbxSceneReader.ParseFbx(conversionInput.Fbx);
        if(fbxSceneResult.IsFailure)
            return ToolResult.Fail(fbxSceneResult);

        var normalizedItemResult = ConvertToNormalizedItem(fbxSceneResult.Value, conversionInput);
        if (normalizedItemResult.IsFailure)
            return ToolResult.Fail(normalizedItemResult);

        var buildSettings = CreateBuildSettings(normalizedItemResult.Value, conversionInput);
        var itemResult = meshBuilder.BuildMixedItem(normalizedItemResult.Value, buildSettings);
        if(itemResult.IsFailure)
            return ToolResult.Fail(itemResult);

        SetItemMetaData(itemResult.Value, conversionInput);

        return ToolResult.Success(itemResult.Value, nameof(FbxGbxConverter));
    }

    public ToolResult<CGameItemModel> ConvertToGbxAndSaveItem(FbxGbxConversionInput conversionInput)
    {
        if(string.IsNullOrWhiteSpace(conversionInput.ItemOutputPath))
            return ToolResult.Fail(nameof(FbxGbxConverter), ErrorCodes.FbxGbxConverter.InvalidItemOutputPath);

        var itemResult = ConvertToGbx(conversionInput);
        if (itemResult.IsFailure)
            return ToolResult.Fail(itemResult);

        Directory.CreateDirectory(Path.GetDirectoryName(conversionInput.ItemOutputPath));
        itemResult.Value.Save(conversionInput.ItemOutputPath);

        return itemResult;
    }



    void SetItemMetaData(CGameItemModel item, FbxGbxConversionInput config)
    {

    }

    ToolResult<NormalizedItem> ConvertToNormalizedItem(Scene scene, FbxGbxConversionInput config)
    {
        var normalizedItem = new NormalizedItem();

        normalizedItem.PlacementParam = CreatePlacementParameters(config);

        List<NormalizedMesh> meshes = new List<NormalizedMesh>();
        List<MeshDefGroup> groups = new List<MeshDefGroup>();

        var materialConverter = new FbxMaterialConverter(config.MaterialLibrary, Solid2ModelTemplate.CustomMaterials![0].MaterialUserInst!);

        var materialResults = materialConverter.ExtractMaterials(scene, config);
        if (materialResults.IsFailure)
            return ToolResult.Fail(materialResults);

        var meshResults = FbxMeshConverter.ExtractMeshes(scene, materialResults.Value, config);
        if (meshResults.IsFailure)
            return ToolResult.Fail(meshResults);

        var socketResults = FbxMeshConverter.ExtractSockets(scene, config);
        if (socketResults.IsFailure)
            return ToolResult.Fail(socketResults);
        if(socketResults.Value.Count > 1)
            return ToolResult.Fail(nameof(FbxGbxConverter), ErrorCodes.FbxGbxConverter.MultipleSocketsNotSupported);

        var lightResults = FbxLightConverter.ExtractLights(scene, config);
        if (lightResults.IsFailure)
            return ToolResult.Fail(lightResults);


        var meshItems = FilterAndApplySpecialMeshItems(meshResults.Value, normalizedItem);

        var groupResults = FbxMeshConverter.GroupMeshes(meshItems, socketResults.Value, config);
        if (groupResults.IsFailure)
            return ToolResult.Fail(groupResults);

        FbxLightConverter.GroupLights(lightResults.Value, groupResults.Value);



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
        normalizedItem.Meshes = meshItems.Select(m => m.Mesh).ToArray();
        normalizedItem.Lights = lightResults.Value.Select(lr => lr.Light).ToArray();

        return ToolResult.Success(normalizedItem, nameof(FbxGbxConverter));
    }


    List<MeshDef> FilterAndApplySpecialMeshItems(IEnumerable<MeshDef> meshItems, NormalizedItem item)
    {
        List<MeshDef> meshItemsWithMesh = new List<MeshDef>();

        foreach (var meshItem in meshItems)
        {
            if (meshItem.MeshConfig.MeshFlags.HasMeshData())
                meshItemsWithMesh.Add(meshItem);
        }


        return meshItemsWithMesh;
    }



    //-------------------------------------
    // Placement Parameter
    //-------------------------------------
    CGameItemPlacementParam CreatePlacementParameters(FbxGbxConversionInput config)
    {
        var placementParams = EntityModelTemplate.DefaultPlacement!;

        placementParams.AutoRotation = config.ItemConfig.PlacementParams?.AutoRotation ?? false;
        placementParams.FlyVOffset = config.ItemConfig.PlacementParams?.LevitationVerticalOffset ?? 0;
        placementParams.FlyVStep = config.ItemConfig.PlacementParams?.LevitationVerticalStep ?? 0;
        placementParams.GridSnapHOffset = config.ItemConfig.PlacementParams?.GridHorizontalOffset ?? 0;
        placementParams.GridSnapVOffset = config.ItemConfig.PlacementParams?.GridVerticalOffset ?? 0;
        placementParams.GridSnapHStep = config.ItemConfig.PlacementParams?.GridHorizontalStep ?? 0;
        placementParams.GridSnapVStep = config.ItemConfig.PlacementParams?.GridVerticalStep ?? 0;
        placementParams.NotOnObject = config.ItemConfig.PlacementParams?.NotOnItem ?? false;
        placementParams.PivotPositions = config.ItemConfig.PivotsPositions?.Select(p => p.Pos).ToArray();
        placementParams.PivotRotations = null;
        placementParams.PivotSnapDistance = config.ItemConfig.PlacementParams?.PivotSnapDistance ?? 0;
        placementParams.SwitchPivotManually = config.ItemConfig.PlacementParams?.ManualPivotSwitch ?? false;
        placementParams.YawOnly = config.ItemConfig.PlacementParams?.OneAxisRotation ?? false;


        return placementParams;
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
