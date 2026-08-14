namespace TM_GenericMapping.Items.FbxGbxConversion.Serialization;

using GBX.NET;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Plug;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using TM_GenericMapping.Common;
using static GBX.NET.Engines.GameData.CGameItemModel;

[Flags]
public enum ItemConversionOptions
{
    None = 0,
    MeshConfigFromObjectNames = 1 << 0,
    IgnoreMeshesWithInvalidMaterials = 1 << 1,
}
public class ItemConfig
{
    public string Type { get; init; } = "StaticObject";
    public string Collection { get; init; } = "Stadium";
    public string AuthorName { get; set; } = "";

    public string? Name { get; set; } = null;
    public string? Description { get; set; } = null;


    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Waypoint? Waypoint { get; set; }


    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CGameItemPlacementParam? PlacementParams { get; set; }

    public float Scale { get; set; } = 1.0f;

    public List<MaterialConfig> MaterialConfiguration { get; set; } = new List<MaterialConfig>();

    public List<MeshConfig> MeshConfiguration { get; set; } = new List<MeshConfig>();

    public List<LightConfig> Lights { get; set; } = new List<LightConfig>();


    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LodParameters? LodParameters { get; set; }

    public ItemConversionOptions ConversionOptions { get; set; }




    public static ItemConfig Deserialize(string filePath)
    {
        using var itemInfoStream = File.OpenRead(filePath);
        return Deserialize(itemInfoStream);
    }
    public static ItemConfig Deserialize(Stream itemInfoStream)
    {
        return JsonSerializer.Deserialize<ItemConfig>(itemInfoStream, serializerOptions) ?? throw new InvalidOperationException("Failed to deserialize ItemInfoJson.");
    }
    public static void Serialize(ItemConfig item, string filePath)
    {
        using var itemInfoStream = File.Create(filePath);
        Serialize(item, itemInfoStream);
    }
    public static void Serialize(ItemConfig item, Stream itemInfoStream)
    {
        JsonSerializer.Serialize(itemInfoStream, item, serializerOptions);
    }

    static JsonSerializerOptions serializerOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        Converters = {
            new Vec3Converter(),
            new JsonStringEnumConverter(),
            new ColorJsonConverter(),
            new PlacementParameterConverter(),
            new PatchLayoutConverter(),
            new NPlugItemPlacement_SClassConverter()
        }
    };
}


public class Waypoint
{
    public EWaypointType Type { get; set; } = EWaypointType.Checkpoint;
    public bool NoRespawn { get; set; }
    public Vec3? DefaultGravitySpawn { get; set; } = new Vec3(0, -1, 0);
    public float? TorqueX { get; set; } = 0;
    public int? TorqueDuration { get; set; } = 0;
}


public class LodParameters
{
    /// <summary>
    /// Mismatch between distance value and actual ingame distance (value = 100 => 200 units ingame)
    /// </summary>
    public List<float> MaxLodDistances { get; set; } = [];
}


public class LightConfig
{
    public string Name { get; set; }
    public LightType Type { get; set; }
    public System.Drawing.Color Color { get; set; }
    public float Intensity { get; set; }
    public float Distance { get; set; }
    public bool NightOnly { get; set; }
    public float PointEmissionRadius { get; set; }
    public float PointEmissionLength { get; set; }
    public float SpotInnerAngle { get; set; } = 40;
    public float SpotOuterAngle { get; set; } = 60;
    public float SpotEmissionSizeX { get; set; }
    public float SpotEmissionSizeY { get; set; }
}




//
// Converter
//

public class Vec3Converter : JsonConverter<Vec3>
{
    public override Vec3 Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        var parts = reader.GetString()!.Split(' ');

        return new Vec3(
            float.Parse(parts[0], CultureInfo.InvariantCulture),
            float.Parse(parts[1], CultureInfo.InvariantCulture),
            float.Parse(parts[2], CultureInfo.InvariantCulture)
        );
    }

    public override void Write(Utf8JsonWriter writer, Vec3 value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(
            $"{value.X.ToString(CultureInfo.InvariantCulture)} " +
            $"{value.Y.ToString(CultureInfo.InvariantCulture)} " +
            $"{value.Z.ToString(CultureInfo.InvariantCulture)}"
        );
    }
}

public sealed class ColorJsonConverter : JsonConverter<System.Drawing.Color>
{
    public override System.Drawing.Color Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        var value = reader.GetString();

        return value != null
            ? ColorUtils.FromRGBHex(value)
            : System.Drawing.Color.Black;
    }

    public override void Write(
        Utf8JsonWriter writer,
        System.Drawing.Color value,
        JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToRGBHex());
    }
}

public sealed class PlacementParameterConverter : JsonConverter<CGameItemPlacementParam>
{
    public override CGameItemPlacementParam? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        JsonElement root = document.RootElement;

        var value = new CGameItemPlacementParam
        {
            YawOnly = root.TryGetProperty("YawOnly", out var yawOnly)
                && yawOnly.GetBoolean(),

            NotOnObject = root.TryGetProperty("NotOnObject", out var notOnObject)
                && notOnObject.GetBoolean(),

            AutoRotation = root.TryGetProperty("AutoRotation", out var autoRotation)
                && autoRotation.GetBoolean(),

            SwitchPivotManually = root.TryGetProperty("SwitchPivotManually", out var switchPivotManually)
                && switchPivotManually.GetBoolean(),

            CubeSize = root.GetProperty("CubeSize").GetSingle(),
            GridSnapHStep = root.GetProperty("GridSnapHStep").GetSingle(),
            GridSnapVStep = root.GetProperty("GridSnapVStep").GetSingle(),
            GridSnapHOffset = root.GetProperty("GridSnapHOffset").GetSingle(),
            GridSnapVOffset = root.GetProperty("GridSnapVOffset").GetSingle(),
            FlyVStep = root.GetProperty("FlyVStep").GetSingle(),
            FlyVOffset = root.GetProperty("FlyVOffset").GetSingle(),
            PivotSnapDistance = root.GetProperty("PivotSnapDistance").GetSingle()
        };

        if (root.TryGetProperty("CubeCenter", out var cubeCenter))
        {
            value.CubeCenter =
                cubeCenter.Deserialize<Vec3>(options);
        }

        if (root.TryGetProperty("PivotPositions", out var pivotPositions))
        {
            value.PivotPositions =
                pivotPositions.Deserialize<Vec3[]>(options);
        }

        if (root.TryGetProperty("PivotRotations", out var pivotRotations))
        {
            value.PivotRotations =
                pivotRotations.Deserialize<Quat[]>(options);
        }

        if (root.TryGetProperty("PlacementClass", out var placementClass))
        {
            value.PlacementClass =
                placementClass.Deserialize<NPlugItemPlacement_SClass>(options);
        }

        return value;
    }

    public override void Write(Utf8JsonWriter writer, CGameItemPlacementParam value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        writer.WriteBoolean("YawOnly", value.YawOnly);
        writer.WriteBoolean("NotOnObject", value.NotOnObject);
        writer.WriteBoolean("AutoRotation", value.AutoRotation);
        writer.WriteBoolean("SwitchPivotManually", value.SwitchPivotManually);

        writer.WritePropertyName("CubeCenter");
        JsonSerializer.Serialize(writer, value.CubeCenter, options);

        writer.WriteNumber("CubeSize", value.CubeSize);
        writer.WriteNumber("GridSnapHStep", value.GridSnapHStep);
        writer.WriteNumber("GridSnapVStep", value.GridSnapVStep);
        writer.WriteNumber("GridSnapHOffset", value.GridSnapHOffset);
        writer.WriteNumber("GridSnapVOffset", value.GridSnapVOffset);
        writer.WriteNumber("FlyVStep", value.FlyVStep);
        writer.WriteNumber("FlyVOffset", value.FlyVOffset);
        writer.WriteNumber("PivotSnapDistance", value.PivotSnapDistance);

        writer.WritePropertyName("PivotPositions");
        JsonSerializer.Serialize(writer, value.PivotPositions, options);

        writer.WritePropertyName("PivotRotations");
        JsonSerializer.Serialize(writer, value.PivotRotations, options);

        writer.WritePropertyName("PlacementClass");
        JsonSerializer.Serialize(writer, value.PlacementClass, options);

        writer.WriteEndObject();
    }
}
public sealed class PatchLayoutConverter : JsonConverter<NPlugItemPlacement_SClass.PatchLayout>
{
    public override void Write(
        Utf8JsonWriter writer,
        NPlugItemPlacement_SClass.PatchLayout value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        writer.WriteNumber("ItemCount", value.ItemCount);
        writer.WriteNumber("ItemSpacing", value.ItemSpacing);
        writer.WriteNumber("FillAlign", value.FillAlign);
        writer.WriteNumber("FillDir", value.FillDir);
        writer.WriteNumber("NormedPos", value.NormedPos);
        writer.WriteNumber("U01", value.U01);

        writer.WritePropertyName("OnlyOnGroups");
        JsonSerializer.Serialize(writer, value.OnlyOnGroups, options);

        writer.WriteNumber("Altitude", value.Altitude);
        writer.WriteNumber("U02", value.U02);

        writer.WriteEndObject();
    }

    public override NPlugItemPlacement_SClass.PatchLayout Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        JsonElement root = document.RootElement;

        var value = new NPlugItemPlacement_SClass.PatchLayout
        {
            ItemCount = root.GetProperty("ItemCount").GetInt32(),
            ItemSpacing = root.GetProperty("ItemSpacing").GetSingle(),
            FillAlign = root.GetProperty("FillAlign").GetInt32(),
            FillDir = root.GetProperty("FillDir").GetInt32(),
            NormedPos = root.GetProperty("NormedPos").GetSingle(),
            U01 = root.GetProperty("U01").GetSingle(),
            Altitude = root.GetProperty("Altitude").GetSingle(),
            U02 = root.GetProperty("U02").GetSingle()
        };

        if (root.TryGetProperty("OnlyOnGroups", out var onlyOnGroups))
        {
            value.OnlyOnGroups =
                onlyOnGroups.Deserialize<string[]>(options);
        }

        return value;
    }
}
public sealed class NPlugItemPlacement_SClassConverter
    : JsonConverter<NPlugItemPlacement_SClass>
{
    public override void Write(
        Utf8JsonWriter writer,
        NPlugItemPlacement_SClass value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        writer.WritePropertyName("SizeGroup");
        writer.WriteStringValue(value.SizeGroup);

        writer.WritePropertyName("CompatibleGroupsIds");
        JsonSerializer.Serialize(writer, value.CompatibleGroupsIds, options);

        writer.WriteBoolean("AlwaysUp", value.AlwaysUp);
        writer.WriteBoolean("AlignToInterior", value.AlignToInterior);
        writer.WriteBoolean("AlignToWorldDir", value.AlignToWorldDir);

        writer.WritePropertyName("WorldDir");
        JsonSerializer.Serialize(writer, value.WorldDir, options);

        writer.WritePropertyName("PatchLayouts");
        JsonSerializer.Serialize(writer, value.PatchLayouts, options);

        writer.WritePropertyName("GroupCurPatchLayouts");
        JsonSerializer.Serialize(writer, value.GroupCurPatchLayouts, options);

        writer.WriteEndObject();
    }

    public override NPlugItemPlacement_SClass Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        JsonElement root = document.RootElement;

        var value = new NPlugItemPlacement_SClass
        {
            SizeGroup =
                root.TryGetProperty("SizeGroup", out var sizeGroup)
                    ? sizeGroup.GetString()
                    : null,

            CompatibleGroupsIds =
                root.TryGetProperty("CompatibleGroupsIds", out var compatibleGroupsIds)
                    ? compatibleGroupsIds.Deserialize<string[]>(options)
                    : null,

            AlwaysUp =
                root.TryGetProperty("AlwaysUp", out var alwaysUp)
                    && alwaysUp.GetBoolean(),

            AlignToInterior =
                root.TryGetProperty("AlignToInterior", out var alignToInterior)
                    && alignToInterior.GetBoolean(),

            AlignToWorldDir =
                root.TryGetProperty("AlignToWorldDir", out var alignToWorldDir)
                    && alignToWorldDir.GetBoolean(),

            WorldDir =
                root.TryGetProperty("WorldDir", out var worldDir)
                    ? worldDir.Deserialize<Vec3>(options)
                    : default,

            PatchLayouts =
                root.TryGetProperty("PatchLayouts", out var patchLayouts)
                    ? patchLayouts.Deserialize<
                        NPlugItemPlacement_SClass.PatchLayout[]>(options)
                    : null,

            GroupCurPatchLayouts =
                root.TryGetProperty("GroupCurPatchLayouts", out var groupCurPatchLayouts)
                    ? groupCurPatchLayouts.Deserialize<int[]>(options)
                    : null
        };

        return value;
    }
}

