using GBX.NET;
using GBX.NET.Engines.Plug;
using System.Text.Json.Serialization;
using static GBX.NET.Engines.GameData.CGameItemModel;
using static GBX.NET.Engines.Plug.CPlugSurface;

namespace TM_GenericMapping.Items.FbxGbxConversion.Serialization;


public class MaterialConfig
{
    [JsonPropertyName("Name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("Link")]
    public string Link { get; set; } = "";


    [JsonPropertyName("Color")]
    public System.Drawing.Color? Color { get; set; } = null;


    [JsonPropertyName("PhysicsId")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public CPlugSurface.MaterialId? PhysicsId { get; set; } = null;

    [JsonPropertyName("GameplayId")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public GameplayId? GameplayId { get; set; } = null;

    [JsonPropertyName("BaseTexture")]
    public string? BaseTexture { get; set; } = null;

    [JsonPropertyName("Model")]
    public string? Model { get; set; } = null;
}

public class MeshConfig
{
    [JsonPropertyName("Name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("MeshFlags")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public MeshFlags MeshFlags { get; set; }

    [JsonPropertyName("TriggerEffect")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public LegacyGameplayId? TriggerEffect { get; set; } = null;

    [JsonPropertyName("WaypointType")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public EWaypointType? WaypointType { get; set; } = null;

    [JsonPropertyName("GameplayMainDir")]
    public Vec3? GameplayMainDir { get; set; } = null;

    [JsonPropertyName("MovingGroup")]
    public string? MovingGroup { get; set; }

    [JsonPropertyName("LightmapSize")]
    public float? LightmapSize { get; set; } = null;

    /// <summary>
    /// Global LOD indices this mesh has actual geometry for, e.g. [1, 3, 5].
    /// Not necessarily contiguous. Empty = mesh has no LOD variance.
    /// </summary>
    [JsonPropertyName("Lods")]
    public List<int> Lods { get; set; } = [];
}

[Flags]
public enum MeshFlags
{
    None,
    NonCollidable = 1 << 0,
    Invisible = 1 << 1,
    Moving = 1 << 2,
    TriggerEffect = 1 << 3,
    TriggerWaypoint = 1 << 4,
    Socket = 1 << 5,
    SingleMesh = 1 << 6,
    Skip = 1 << 7,

}

public static class MeshFlagsUtils
{
    public static bool HasMeshData(this MeshFlags flags)
    {
        return (flags & (MeshFlags.Socket | MeshFlags.Skip)) == 0;
    }
}