using GBX.NET.Engines.Plug;
using System.Drawing;
using System.Text.Json.Serialization;
using static GBX.NET.Engines.GameData.CGameItemModel;

namespace TM_GenericMapping.Items.FbxGbxConversion.Serialization;


public class MaterialConfig
{
    public string Name { get; set; } = "";
    public string Link { get; set; } = "";

    public System.Drawing.Color? Color { get; set; } = null;

    public CPlugSurface.MaterialId? PhysicsId { get; set; } = null;
    public CPlugMaterialUserInst.GameplayId? GameplayId { get; set; } = null;
}

public class MeshConfig
{
    public string Name { get; set; }
    public MeshFlags MeshFlags { get; set; }
    public LegacyGameplayId? TriggerEffect { get; set; } = null;
    public EWaypointType? WaypointType { get; set; } = null;
    public string? MovingGroup { get; set; }

    /// <summary>
    /// Global LOD indices this mesh has actual geometry for, e.g. [1, 3, 5].
    /// Not necessarily contiguous. Empty = mesh has no LOD variance.
    /// </summary>
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