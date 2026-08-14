using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Plug;
using System.Xml.Serialization;
using TM_GenericMapping.Items.FbxGbxConversion.Serialization;
using static GBX.NET.Engines.GameData.CGameItemModel;

namespace TM_GenericMapping.Items.FbxGbxConversion;


public record FbxGbxConversionInput : IDisposable
{
    public required ItemConfig ItemConfig { get; set; }

    public required DMaterialLibrary MaterialLibrary { get; set; }
    public required Stream Fbx { get; set; }
    public required Stream? Icon { get; set; }

    public string? ItemOutputPath { get; set; }

    public void Dispose()
    {
        Fbx.Dispose();
        Icon?.Dispose();
    }


    public static FbxGbxConversionInput CreateFromItemXmlFile(string itemXmlFilePath, string materialLibFilePath, ItemConversionOptions options = ItemConversionOptions.MeshConfigFromObjectNames)
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
            ItemConfig = ToItemConfig(itemXml, meshParamsXml, options),
            Fbx = fbx,
            Icon = icon,
            MaterialLibrary = materialLib,
            ItemOutputPath = GetItemOutputPath(itemXmlFilePath),
        };
        return config;
    }
    public static FbxGbxConversionInput CreateFromXmlStreams(Stream itemXml, Stream meshParamsXml, Stream fbx, Stream materialLibStream, Stream? icon = null, string? itemOutputPath = null, ItemConversionOptions options = ItemConversionOptions.MeshConfigFromObjectNames)
    {
        var config = new FbxGbxConversionInput()
        {
            ItemConfig = ToItemConfig(ParseItemXml(itemXml), ParseMeshParamsXml(meshParamsXml), options),
            MaterialLibrary = ParseMaterialLibrary(materialLibStream),
            Fbx = fbx,
            Icon = icon,
            ItemOutputPath = itemOutputPath,
        };
        return config;
    }

    public static FbxGbxConversionInput CreateFromItemConfig(Stream itemConfigStream, Stream fbx, DMaterialLibrary materialLibrary, Stream? icon = null, string? itemOutputPath = null)
    {
        var config = new FbxGbxConversionInput()
        {
            ItemConfig = ItemConfig.Deserialize(itemConfigStream),
            MaterialLibrary = materialLibrary,
            Fbx = fbx,
            Icon = icon,
            ItemOutputPath = itemOutputPath,
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

    static ItemConfig ToItemConfig(ItemXml itemXml, MeshParamsXml meshParamsXml, ItemConversionOptions itemConversionOptions = ItemConversionOptions.MeshConfigFromObjectNames)
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
            PlacementParams = new CGameItemPlacementParam()
            {
                GridSnapHStep = itemXml.GridSnap?.HStep ?? 0f,
                GridSnapHOffset = itemXml.GridSnap?.HOffset ?? 0f,
                GridSnapVStep = itemXml.GridSnap?.VStep ?? 0f,
                GridSnapVOffset = itemXml.GridSnap?.VOffset ?? 0f,
                FlyVStep = itemXml.Levitation?.VStep ?? 0f,
                FlyVOffset = itemXml.Levitation?.VOffset ?? 0f,
                YawOnly = itemXml.Options?.OneAxisRotation ?? false,
                SwitchPivotManually = itemXml.Options?.ManualPivotSwitch ?? false,
                NotOnObject = itemXml.Options?.NotOnItem ?? false,
                AutoRotation = itemXml.Options?.AutoRotation ?? false,
                PivotSnapDistance = itemXml.PivotSnap?.Distance ?? -1f,
                PivotPositions = itemXml.Pivots?.Select(p => p.GetPosition()).ToArray(),
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
            }).ToList(),
            ConversionOptions = itemConversionOptions,

        };
        return config;

    }

}
