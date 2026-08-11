namespace TM_GenericMapping.Items.FbxConverter.Serialization;

using GBX.NET;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using static GBX.NET.Engines.GameData.CGameItemModel;

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
    public List<PivotPosition>? PivotsPositions { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<PivotRotation>? PivotRotations { get; set; }


    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PlacementParameters? PlacementParams { get; set; }

    public float Scale { get; set; } = 1.0f;

    public List<MaterialConfig> MaterialConfiguration { get; set; } = new List<MaterialConfig>();

    public List<MeshConfig> MeshConfiguration { get; set; } = new List<MeshConfig>();

    public List<LightConfig> Lights { get; set; } = new List<LightConfig>();


    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LodParameters? LodParameters { get; set; }





    public static ItemConfig Deserialize(string filePath)
    {
        using var itemInfoStream = File.OpenRead(filePath);
        return Deserialize(itemInfoStream);
    }
    public static ItemConfig Deserialize(Stream itemInfoStream)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new Vec3Converter() }
        };
        return JsonSerializer.Deserialize<ItemConfig>(itemInfoStream, options) ?? throw new InvalidOperationException("Failed to deserialize ItemInfoJson.");
    }
    public static void Serialize(ItemConfig item, string filePath)
    {
        using var itemInfoStream = File.Create(filePath);
        Serialize(item, itemInfoStream);
    }
    public static void Serialize(ItemConfig item, Stream itemInfoStream)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new Vec3Converter() }
        };
        JsonSerializer.Serialize(itemInfoStream, item, options);
    }
}


public class Waypoint
{
    public EWaypointType Type { get; set; } = EWaypointType.Checkpoint;
    public bool NoRespawn { get; set; }
    public Vec3? DefaultGravitySpawn { get; set; } = new Vec3(0, -1, 0);
    public float? TorqueX { get; set; } = 0;
    public int? TorqueDuration { get; set; } = 0;
}

public class PivotPosition
{
    public Vec3 Pos { get; set; } = new Vec3(0, 0, 0);
}
public class PivotRotation
{
    public Quat Rot { get; set; } = Quat.Identity;
}
public class LodParameters
{
    /// <summary>
    /// Mismatch between distance value and actual ingame distance (value = 100 => 200 units ingame)
    /// </summary>
    public List<float> MaxLodDistances { get; set; } = [];
}
public class PlacementParameters
{
    public float GridHorizontalStep { get; set; }
    public float GridHorizontalOffset { get; set; }
    public float GridVerticalStep { get; set; }
    public float GridVerticalOffset { get; set; }

    public float LevitationVerticalStep { get; set; }
    public float LevitationVerticalOffset { get; set; }

    public bool GhostMode { get; set; }

    public bool OneAxisRotation { get; set; }
    public bool ManualPivotSwitch { get; set; }
    public bool NotOnItem { get; set; }
    public bool AutoRotation { get; set; }
    public float PivotSnapDistance { get; set; } = -1;

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