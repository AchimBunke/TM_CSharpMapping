using Assimp;
using GBX.NET.Engines.GameData;
using System.Xml.Serialization;
using TM_GenericMapping.Messaging;

namespace TM_GenericMapping.Items.FbxConverter;

public record GbxConversionInput(string ItemXmlPath)
{
    public string ItemXmlFolder => Path.GetDirectoryName(ItemXmlPath)!;
    public string ItemNameWithoutExtensions => Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(ItemXmlPath));
}

public class GbxConverter
{
    public ToolResult<CGameItemModel> ConvertToGbx(GbxConversionInput conversionInput)
    {
        ItemXml itemXml;
        try
        {
            itemXml = ParseItemXml(conversionInput.ItemXmlPath);
        }
        catch
        {
            return ToolResult.Fail(nameof(GbxConverter), ErrorCodes.GbxConverter.InvalidItemXml);
        }
       
        var meshParamsPath = GetMeshParamsPath(conversionInput, itemXml);
        MeshParamsXml meshParamsXml;
        try
        {
            meshParamsXml = ParseMeshParamsXml(meshParamsPath);
        }
        catch 
        {
            return ToolResult.Fail(nameof(GbxConverter), ErrorCodes.GbxConverter.InvalidMeshParamsXml);
        }
       
        meshParamsXml.FilePath = meshParamsPath;
        var fbxPath = GetFbxPath(conversionInput, meshParamsXml);
        var iconPath = GetIconPath(conversionInput);
        bool iconExists = File.Exists(iconPath);

        ParseFbx(fbxPath);


        return ToolResult.Fail(nameof(GbxConverter), "");
    }

    void ParseFbx(string fbxPath)
    {
        var context = new AssimpContext();
        // maybe flipUV
        var scene = context.ImportFile(fbxPath, 
            PostProcessSteps.Triangulate |
            PostProcessSteps.JoinIdenticalVertices
            //PostProcessSteps.ValidateDataStructure
            );

    }


    ItemXml ParseItemXml(string filePath)
    {
        var serializer = new XmlSerializer(typeof(ItemXml));

        using var stream = File.OpenRead(filePath);
        var itemXml = (ItemXml)serializer.Deserialize(stream)!;

        return itemXml;
    }
    MeshParamsXml ParseMeshParamsXml(string filePath)
    {
        var serializer = new XmlSerializer(typeof(MeshParamsXml));
        using var stream = File.OpenRead(filePath);
        var meshParamsXml = (MeshParamsXml)serializer.Deserialize(stream)!;
        return meshParamsXml;
    }
    string GetMeshParamsPath(GbxConversionInput conversionInput, ItemXml itemXml)
    {
        if (itemXml.MeshParamsLink.File == null)
        {
            return Path.Combine(conversionInput.ItemXmlFolder, "Mesh", $"{conversionInput.ItemNameWithoutExtensions}.MeshParams.xml");
        }
        else
        {
            return Path.Combine(conversionInput.ItemXmlFolder, itemXml.MeshParamsLink.File);
        }
    }
    string GetFbxPath(GbxConversionInput conversionInput, MeshParamsXml meshParamsXml)
    {
        if (meshParamsXml.FbxFile == null)
        {
            return Path.Combine(Path.GetDirectoryName(meshParamsXml.FilePath)!, $"{Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(meshParamsXml.FilePath))}.fbx");
        }
        else
        {
            return Path.Combine(Path.GetDirectoryName(meshParamsXml.FilePath)!, meshParamsXml.FbxFile);
        }
    }
    string GetIconPath(GbxConversionInput conversionInput)
    {
        return Path.Combine(conversionInput.ItemXmlFolder, "Icon", $"{conversionInput.ItemNameWithoutExtensions}.tga");
    }
}
