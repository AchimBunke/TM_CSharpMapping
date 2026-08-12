using System.Xml.Serialization;
using TM_GenericMapping.Items.FbxGbxConversion.Serialization;
using static GBX.NET.Engines.GameData.CGameItemModel;

namespace TM_GenericMapping.Items.FbxGbxConversion;

[Flags]
public enum ConversionOptions
{
    None = 0,
    MeshConfigFromObjectNames = 1 << 0,
}
public record FbxGbxConversionInput : IDisposable
{
    public required ItemConfig ItemConfig { get; set; }

    public required DMaterialLibrary MaterialLibrary { get; set; }
    public required Stream Fbx { get; set; }
    public required Stream? Icon { get; set; }

    public string? ItemOutputPath { get; set; }

    public ConversionOptions ConversionOptions { get; set; } = ConversionOptions.None;

    public void Dispose()
    {
        Fbx.Dispose();
        Icon?.Dispose();
    }


    public static FbxGbxConversionInput CreateFromItemXmlFile(string itemXmlFilePath, string materialLibFilePath, ConversionOptions options = ConversionOptions.MeshConfigFromObjectNames)
    {
        var itemXml = ParseItemXml(itemXmlFilePath);
        var meshParamsPath = GetMeshParamsFilePath(itemXml, itemXmlFilePath);
        var meshParamsXml = ParseMeshParamsXml(meshParamsPath);
        var fbxPath = GetFbxPath(meshParamsXml, meshParamsPath);
        var fbx = File.OpenRead(fbxPath);
        var iconPath = GetIconPath(fbxPath);
        var materialLib = ParseMaterialLibrary(materialLibFilePath);
        Stream? icon = null;
        if (File.Exists(iconPath))
        {
            icon = File.OpenRead(iconPath);
        }

        var config = new FbxGbxConversionInput()
        {
            ItemConfig = ToItemConfig(itemXml, meshParamsXml),
            Fbx = fbx,
            Icon = icon,
            MaterialLibrary = materialLib,
            ItemOutputPath = GetItemOutputPath(itemXmlFilePath),
            ConversionOptions = options
        };
        return config;
    }
    public static FbxGbxConversionInput CreateFromXmlStreams(Stream itemXml, Stream meshParamsXml, Stream fbx, Stream materialLibStream, Stream? icon = null, string? itemOutputPath = null, ConversionOptions options = ConversionOptions.MeshConfigFromObjectNames)
    {
        var config = new FbxGbxConversionInput()
        {
            ItemConfig = ToItemConfig(ParseItemXml(itemXml), ParseMeshParamsXml(meshParamsXml)),
            MaterialLibrary = ParseMaterialLibrary(materialLibStream),
            Fbx = fbx,
            Icon = icon,
            ItemOutputPath = itemOutputPath,
            ConversionOptions = options
        };
        return config;
    }

    public static FbxGbxConversionInput CreateFromItemConfig(Stream itemConfigStream, Stream fbx, DMaterialLibrary materialLibrary, Stream? icon = null, string? itemOutputPath = null, ConversionOptions options = ConversionOptions.None)
    {
        var config = new FbxGbxConversionInput()
        {
            ItemConfig = ItemConfig.Deserialize(itemConfigStream),
            MaterialLibrary = materialLibrary,
            Fbx = fbx,
            Icon = icon,
            ItemOutputPath = itemOutputPath,
            ConversionOptions = options
        };
        return config;
    }


    static ItemXml ParseItemXml(string itemXmlPath)
    {
        using var fs = File.OpenRead(itemXmlPath);
        return ParseItemXml(fs);
    }

    static ItemXml ParseItemXml(Stream itemXmlStream)
    {
        var serializer = new XmlSerializer(typeof(ItemXml));
        var itemXml = (ItemXml)serializer.Deserialize(itemXmlStream)!;
        return itemXml;
    }
    static MeshParamsXml ParseMeshParamsXml(string meshParamsFilePath)
    {
        using var fs = File.OpenRead(meshParamsFilePath);
        return ParseMeshParamsXml(fs);
    }
    static MeshParamsXml ParseMeshParamsXml(Stream meshParamsStream)
    {
        var serializer = new XmlSerializer(typeof(MeshParamsXml));
        var meshParamsXml = (MeshParamsXml)serializer.Deserialize(meshParamsStream)!;
        return meshParamsXml;
    }

    static DMaterialLibrary ParseMaterialLibrary(string materialLibFilePath)
    {
        using var fs = File.OpenRead(materialLibFilePath);
        return ParseMaterialLibrary(fs);
    }
    static DMaterialLibrary ParseMaterialLibrary(Stream materialLibStream)
    {
        return DMaterialLibraryParser.Parse(materialLibStream);
    }

    static string GetMeshParamsFilePath(ItemXml itemXml, string itemXmlFilePath)
    {
        if (itemXml.MeshParamsLink.File == null)
        {
            return Path.Combine(Path.GetDirectoryName(itemXmlFilePath)!, "Mesh", $"{Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(itemXmlFilePath))}.MeshParams.xml");
        }
        else
        {
            return Path.Combine(Path.GetDirectoryName(itemXmlFilePath)!, itemXml.MeshParamsLink.File);
        }
    }
    static string GetFbxPath(MeshParamsXml meshParamsXml, string meshParamsXmlFilePath)
    {
        if (meshParamsXml.FbxFile == null)
        {
            return Path.Combine(Path.GetDirectoryName(meshParamsXmlFilePath)!, $"{Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(meshParamsXmlFilePath))}.fbx");
        }
        else
        {
            return Path.Combine(Path.GetDirectoryName(meshParamsXmlFilePath)!, meshParamsXml.FbxFile);
        }
    }
    static string GetIconPath(string itemXmlFilePath)
    {
        return Path.Combine(Path.GetDirectoryName(itemXmlFilePath)!, "Icon", $"{Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(itemXmlFilePath))}.tga");
    }

    public static string GetItemOutputPath(string itemXmlPath)
    {
        string itemDirectory = Path.GetDirectoryName(itemXmlPath)!;
        const string marker = @"Trackmania\Work";
        int index = itemDirectory.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
            throw new InvalidOperationException("Path is not inside a Trackmania Work folder.");

        string trackmaniaRoot = itemDirectory[..(index + "Trackmania".Length)];
        string workFolder = Path.Combine(trackmaniaRoot, "Work");

        string relative = Path.GetRelativePath(workFolder, itemDirectory);

        string itemName = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(itemXmlPath));

        return Path.Combine(trackmaniaRoot, relative, $"{itemName}.Item.Gbx");
    }

    static ItemConfig ToItemConfig(ItemXml itemXml, MeshParamsXml meshParamsXml)
    {


        var config = new ItemConfig()
        {
            Type = itemXml.Type,
            Collection = itemXml.Collection,
            AuthorName = itemXml.AuthorName,
            Waypoint = itemXml.Waypoint != null ? new Waypoint()
            {
                Type = itemXml.Waypoint?.Type switch
                {
                    WaypointTypeXml.Start => EWaypointType.Start,
                    WaypointTypeXml.Checkpoint => EWaypointType.Checkpoint,
                    WaypointTypeXml.Finish => EWaypointType.Finish,
                    WaypointTypeXml.StartFinish => EWaypointType.StartFinish,
                    _ => EWaypointType.Checkpoint
                },
                NoRespawn = false,
            } : null,
            PivotsPositions = itemXml.Pivots?.Select(p => new PivotPosition { Pos = p.GetPosition() }).ToList(),
            PlacementParams = new PlacementParameters()
            {
                GridHorizontalStep = itemXml.GridSnap?.HStep ?? 0f,
                GridHorizontalOffset = itemXml.GridSnap?.HOffset ?? 0f,
                GridVerticalStep = itemXml.GridSnap?.VStep ?? 0f,
                GridVerticalOffset = itemXml.GridSnap?.VOffset ?? 0f,
                LevitationVerticalStep = itemXml.Levitation?.VStep ?? 0f,
                LevitationVerticalOffset = itemXml.Levitation?.VOffset ?? 0f,
                GhostMode = itemXml.Levitation?.GhostMode ?? false,
                OneAxisRotation = itemXml.Options?.OneAxisRotation ?? false,
                ManualPivotSwitch = itemXml.Options?.ManualPivotSwitch ?? false,
                NotOnItem = itemXml.Options?.NotOnItem ?? false,
                AutoRotation = itemXml.Options?.AutoRotation ?? false,
                PivotSnapDistance = itemXml.PivotSnap?.Distance ?? -1f
            },
            Scale = meshParamsXml.Scale,
            MaterialConfiguration = meshParamsXml.Materials.Select(m => new MaterialConfig
            {
                Name = m.Name,
                Link = m.Link,
                Color = m.OverrideColor ? m.Color : null,
                PhysicsId = m.OverridePhysicsId ? m.PhysicsId : null,
                GameplayId = m.OverrideGameplayId ? m.GameplayId : null
            }).ToList(),
            Lights = meshParamsXml.Lights.Select(l => new LightConfig
            {
                Name = l.Name,
                Type = l.Type,
                Color = l.Color,
                Intensity = l.Intensity,
                Distance = l.Distance,
                NightOnly = l.NightOnly,
                PointEmissionRadius = l.PointEmissionRadius,
                PointEmissionLength = l.PointEmissionLength,
                SpotInnerAngle = l.SpotInnerAngle,
                SpotOuterAngle = l.SpotOuterAngle,
                SpotEmissionSizeX = l.SpotEmissionSizeX,
                SpotEmissionSizeY = l.SpotEmissionSizeY
            }).ToList()

        };
        return config;

    }

}
