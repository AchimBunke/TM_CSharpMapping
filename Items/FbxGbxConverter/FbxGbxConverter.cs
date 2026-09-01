using Assimp;
using Assimp.Unmanaged;
using DirectXTexNet;
using GBX.NET;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Plug;
using SixLabors.ImageSharp;
using System.IO.Compression;
using System.Numerics;
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

    public ToolResult<CGameItemModel> CreateVariantItem(VariantItemCreationInput variantCreationInput)
    {
        var builder = new VariantItemBuilder();
        var itemResult = builder.CreateVariantItem(variantCreationInput.ItemVariants.ToArray());
        if (itemResult.IsFailure)
            return ToolResult.Fail(itemResult);
        return ToolResult.Success(itemResult.Value, nameof(FbxGbxConverter));
    }




    ToolResult<NormalizedItem> ConvertToNormalizedItem(Scene scene, FbxGbxConversionInput config)
    {
        var normalizedItem = new NormalizedItem();

        normalizedItem.PlacementParam = CreatePlacementParameters(config);

        List<NormalizedMesh> meshes = new List<NormalizedMesh>();
        List<NodeDefGroup> groups = new List<NodeDefGroup>();

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
        if(socketResults.Value.Count > 1)
            return ToolResult.Fail(nameof(FbxGbxConverter), ErrorCodes.FbxGbxConverter.MultipleSocketsNotSupported);

        var lightResults = FbxLightConverter.ExtractLights(scene, config);
        if (lightResults.IsFailure)
            return ToolResult.Fail(lightResults);


        var nodes = FilterAndApplySpecialMeshItems(scene, nodeResults.Value, materialResults.Value);

        var groupResults = FbxMeshConverter.GroupNodes(nodes, socketResults.Value, config);
        if (groupResults.IsFailure)
            return ToolResult.Fail(groupResults);

        var lightGroupResult = FbxLightConverter.GroupLights(lightResults.Value, groupResults.Value);
        if (lightGroupResult.IsFailure)
            return ToolResult.Fail(lightGroupResult);

        AnchorObjects(scene, groupResults.Value, nodes, lightResults.Value);

        var meshResults = FbxMeshConverter.ExtractMeshes(scene, groupResults.Value, materialResults.Value, nodes, config);
        if (meshResults.IsFailure)
            return ToolResult.Fail(meshResults);


        SetMetaData(normalizedItem, config);

        normalizedItem.Groups = groupResults.Value.ToArray();
        normalizedItem.Meshes = meshResults.Value.ToArray();
        normalizedItem.Lights = lightResults.Value.Select(lr => lr.Light).ToArray();

        return ToolResult.Success(normalizedItem, nameof(FbxGbxConverter));
    }
    void SetMetaData(NormalizedItem normalizedItem, FbxGbxConversionInput config)
    {
        normalizedItem.Icon = FbxIconLoader.LoadIcon(config);

        if (config.ItemConfig.Name != null)
            normalizedItem.Name = config.ItemConfig.Name;
        else
            normalizedItem.Name = "Unnamed Item";
        if (config.ItemConfig.Description != null)
            normalizedItem.Description = config.ItemConfig.Description;
        else
            normalizedItem.Description = "No Description";
    }
    void AnchorObjects(Scene scene, List<MeshGroup> meshGroups, List<NodeDef> nodes, List<LightDef> lights)
    {
        for (int i = 0; i < meshGroups.Count; ++i)
        {
            var group = meshGroups[i];
            var pos = group.Position;
            if (pos == Vector3.Zero)
                continue;
            //pos = new Vector3(pos.X, pos.Z, -pos.Y);
            foreach(var node in nodes.Where(n=>n.GroupIndex == i))
            {
                node.GlobalTransform = MakeRelativeToPosition(node.GlobalTransform, pos);
            }
            foreach (var light in lights.Where(l => l.Light.GroupIndex == i))
            {
                light.Light.Position -= pos;
            }
        }
    }
    static Assimp.Matrix4x4 MakeRelativeToPosition(Assimp.Matrix4x4 globalTransform, Vector3 position)
    {
        Assimp.Matrix4x4 result = globalTransform;
        result.A4 -= position.X;
        result.B4 -= position.Y;
        result.C4 -= position.Z;
        return result;
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
        return PlacementConfig.ToPlacementParam(config.ItemConfig.PlacementParams);
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



    //-------------------------------------
    // Convert to Fbx
    //-------------------------------------

    public ToolResult<(Stream fbx, ItemConfig config, Stream icon)> ConvertToFbx(CGameItemModel itemModel, DMaterialLibrary materialLibrary)
    {
        var scene = FbxSceneReader.CreateEmptyScene();

        var result = ConvertToFbx(scene, itemModel, materialLibrary);
        if (result.IsFailure)
            return ToolResult.Fail(result);

        using var context = new AssimpContext();
        foreach (var desc in context.GetSupportedExportFormats())
        {
            Console.WriteLine($"{desc.FormatId} - {desc.Description} (.{desc.FileExtension})");
        }

        ExportDataBlob blob = context.ExportToBlob(scene, "gltf2");

        var fbxStream = new MemoryStream();
        using (var archive = new ZipArchive(fbxStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var current = blob;
            while (current != null)
            {
                var fileName = current.Name switch
                {
                    string s when string.IsNullOrEmpty(current.Name) => $"{itemModel.Name ?? "item."}.gltf",
                    string s when current.Name.EndsWith("bin") => $"$blobfile.bin",
                    _ => current.Name
                };
                var entry = archive.CreateEntry(fileName, CompressionLevel.Optimal);
                using var entryStream = entry.Open();
                entryStream.Write(current.Data, 0, current.Data.Length);
                current = current.NextBlob;
            }
        }
        fbxStream.Position = 0;


        //var fbxStream = new MemoryStream(blob.Data);

        return ToolResult.Success(((Stream)fbxStream, result.Value.config, result.Value.icon), nameof(FbxGbxConverter));
    }

    void ExtractPlacementParamConfig(CGameItemModel item, ItemConfig config)
    {
        var placementConfig = PlacementConfig.FromPlacementParam(item.DefaultPlacement!);
        config.PlacementParams = placementConfig;
    }
    void ExtractMetaData(NormalizedItem normalizedItem, CGameItemModel item, ItemConfig config, out Stream iconStream)
    {
        iconStream = FbxIconLoader.ExtractIcon(normalizedItem);

        if (!string.IsNullOrWhiteSpace(normalizedItem.Name))
            config.Name = normalizedItem.Name;
        if (!string.IsNullOrWhiteSpace(normalizedItem.Description))
            config.Description = normalizedItem.Description;
        config.AuthorName = string.IsNullOrEmpty(item.Ident.Author) ? "Unknown Author" : item.Ident.Author;
    }

    ToolResult<(ItemConfig config, Stream icon)> ConvertToFbx(Scene scene, CGameItemModel item, DMaterialLibrary materialLibrary)
    {
        var config = new ItemConfig();

        var meshExtractor = new MeshExtractor();
        var extractionResult = meshExtractor.ExtractMesh(item);
        if (extractionResult.IsFailure)
            return ToolResult.Fail(extractionResult);

        var normalizedItem = extractionResult.Value;

        ExtractMetaData(normalizedItem, item, config, out var iconStream);
        ExtractPlacementParamConfig(item, config);

        var result = ConvertToFbx(scene, config, normalizedItem, materialLibrary);
        if(result.IsFailure)
            return ToolResult.Fail(result);

        return ToolResult.Success((config, iconStream), nameof(FbxGbxConverter));
    }

    ToolResult<None> ConvertToFbx(Scene scene, ItemConfig itemConfig, NormalizedItem normalizedItem, DMaterialLibrary materialLibrary)
    {
        var materialConverter = new FbxMaterialConverter(materialLibrary);

        var materialResults = materialConverter.RebuildMaterials(scene, normalizedItem, itemConfig);

        var result = FbxMeshConverter.RebuildMeshes(scene, normalizedItem, itemConfig, materialResults);
        if(result.IsFailure)
            return ToolResult.Fail(result);

        return ToolResult.Success(None.Value, nameof(FbxGbxConverter));
    }
}
