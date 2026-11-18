using GBX.NET;
using GBX.NET.Engines.Plug;
using System.Text.Json.Serialization;
using TM_GenericMapping.Items.MeshCompilation;
using static GBX.NET.Engines.GameData.CGameItemModel;
using static GBX.NET.Engines.Meta.CPlugVegetTreeModel;
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

    /// <summary>Asset ids (see ItemConfig.Assets). Only used by materials of meshes that are part of a VegetTree (replaces Link).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("ColorImage")]
    public string? ColorImage { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("NormalImage")]
    public string? NormalImage { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("RoughnessImage")]
    public string? RoughnessImage { get; set; }

    /// <summary>Asset ids (see ItemConfig.Assets). Only used by materials of meshes that are part of a VegetTree (replaces Link).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("DiffuseBitmap")]
    public string? DiffuseBitmap { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("NormalBitmap")]
    public string? NormalBitmap { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("RoughnessBitmap")]
    public string? RoughnessBitmap { get; set; }
}



/// <summary>Entry of the top-level asset registry. Path is relative to the config file unless absolute.</summary>
public class AssetConfig
{
    [JsonPropertyName("Id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("Path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("Type")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AssetType Type { get; set; } = AssetType.ImgFile;
}

public class VegetTreeConfig
{
    [JsonPropertyName("Name")]
    public string Name { get; set; } = string.Empty;


    [JsonPropertyName("AngleMaxRotXZDeg")]
    public float AngleMaxRotXZDeg { get; set; } = 0; // not sure what this does.

    [JsonPropertyName("EnableRandomRotationY")]
    public bool EnableRandomRotationY { get; set; } = false; // rotation around Y axis is randomized when placed.

    [JsonPropertyName("ForceNoCollision")]
    public bool ForceNoCollision { get; set; } = false;

    [JsonPropertyName("ImpostorAllPlanesVisible")]
    public bool ImpostorAllPlanesVisible { get; set; } = false;

    [JsonPropertyName("LodDistAtFov90")]
    public float LodDistAtFov90 { get; set; } = 100;

    [JsonPropertyName("PlaneComputeMode")]
    public EPlaneComputeMode PlaneComputeMode { get; set; } = EPlaneComputeMode.Zero; // how is the importer plane positioned

    //  propagation (particle fx)

    [JsonPropertyName("EnablePropagation")]
    public bool EnablePropagation { get; set; } = false;

    [JsonPropertyName("PropagationAtlasGrid")]
    public Int2 PropagationAtlasGrid { get; set; } = (1, 1);

    [JsonPropertyName("PropagationBaseColorBitmap")]
    public string PropagationBaseColorBitmap { get; set; } = string.Empty;

    [JsonPropertyName("PropagationNormalBitmap")]
    public string PropagationNormalBitmap { get; set; } = string.Empty;

    [JsonPropertyName("PropagationConeHalfAngleDeg")]
    public float PropagationConeHalfAngleDeg { get; set; } = 45;

    [JsonPropertyName("PropagationEmissionPositions")]
    public Vec3[] PropagationEmissionPositions { get; set; } = [];

    [JsonPropertyName("PropagationEmissionSpawnRadius")]
    public float PropagationEmissionSpawnRadius { get; set; } = 2.5f;

    [JsonPropertyName("PropagationGroundGenPointCount")]
    public int PropagationGroundGenPointCount { get; set; } = 300;

    [JsonPropertyName("PropagationLeafPerSecondCount")]
    public int PropagationLeafPerSecondCount { get; set; } = 20;

    [JsonPropertyName("PropagationLeafSize")]
    public float PropagationLeafSize { get; set; } = 0.1f;


    [JsonPropertyName("ReceivesPSSM")]
    public bool ReceivesPSSM { get; set; } = true; // probably Parallel-Split Shadow Maps.

    [JsonPropertyName("PropagationMaxRandomScaleReductionPerInstance")]
    public float PropagationMaxRandomScaleReductionPerInstance { get; set; } = 0.1f;
}

public class MeshConfig
{
    /// <summary>Name of the vegetation tree (see ItemConfig.VegetTrees) this mesh is part of.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("VegetTree")]
    public string? VegetTree { get; set; } = null;

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

    /// <summary>Name of the variant (see ItemConfig.Variants) this mesh belongs to. Empty = part of every variant.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("Variant")]
    public string? Variant { get; set; } = null;

    [JsonPropertyName("Group")]
    public string? Group { get; set; } = string.Empty;

    /// <summary>
    /// Global LOD indices this mesh has actual geometry for, e.g. [1, 3, 5].
    /// Not necessarily contiguous. Empty = mesh has no LOD variance.
    /// </summary>
    [JsonPropertyName("Lods")]
    public List<int> Lods { get; set; } = [];

    public MeshConfig Clone() => new MeshConfig
    {
        Name = this.Name,
        MeshFlags = this.MeshFlags,
        TriggerEffect = this.TriggerEffect,
        WaypointType = this.WaypointType,
        GameplayMainDir = this.GameplayMainDir,
        MovingGroup = this.MovingGroup,
        LightmapSize = this.LightmapSize,
        Variant = this.Variant,
        VegetTree = this.VegetTree,
        Lods = [.. this.Lods]
    };
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