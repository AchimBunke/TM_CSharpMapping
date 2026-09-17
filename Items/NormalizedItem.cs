using GBX.NET;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.Plug;
using System.Numerics;
using static GBX.NET.Engines.GameData.CGameItemModel;
using static GBX.NET.Engines.Plug.CPlugSolid2Model;
using static GBX.NET.Engines.Plug.CPlugSurface;
using static GBX.NET.Engines.Plug.NPlugTrigger_SWaypoint;

namespace TM_GenericMapping.Items;
/*
public enum MeshType
{
    Mesh,
    Dyna_Shape,
    Static_Shape,
    Trigger_Special,
    Trigger_Waypoint,
}

[Flags]
public enum MeshProperties
{
    None = 0,
    Enabled = 1 << 0,
    Visible = 1 << 1,
    Collidable = 1 << 2,
    LOD = 1 << 3
}
public enum GroupType
{
    StaticObject,
    DynaObject,
    Trigger_Special,
    Trigger_Waypoint
}

public class NormalizedMesh
{
    public Vec3[] Positions { get; set; } = [];
    public Vec3[] Normals { get; set; } = [];
    public Vec2[]? TexCoords { get; set; }
    public Vec2[]? LightmapCoords { get; set; }
    public int[]? Colors { get; set; } // packed argb color
    public int[] Indices { get; set; } = [];     // local 0-based
    public CPlugMaterialUserInst Material { get; set; } = null!;
    public MaterialId[]? SurfaceMaterialIds { get; set; }

    public int LODMask { get; set; } = 1;

    public Vec3[]? TangentUs { get; set; }  // per vertex, same length as Positions
    public Vec3[]? TangentVs { get; set; }  // per vertex, same length as Positions

    public MeshType Type { get; set; } = MeshType.Mesh;
    public MeshProperties Properties { get; set; } = MeshProperties.None;

    public string Name { get; set; } = string.Empty;
    public int GroupIndex { get; set; } = -1;
    public int? MeshLink { get; set; } = null; // ID of a mesh that this mesh is linked to (same mesh) (not index, just id that could match already created/extarcted mesh)

    public PreLightGen? PreLightGenerator { get; set; }
    public int? SmoothingGroup { get; set; } = 0;
}
public enum LightType
{
    Point,
    Spot,
    Area,
}
public class NormalizedLight
{
    public Vector3 Position { get; set; }
    public Quaternion Rotation { get; set; }
    public LightType Type { get; set; }

    public CPlugLightUserModel LightModel { get; set; } = null!;

    public string Name { get; set; } = string.Empty;
    public int GroupIndex { get; set; } = -1;
}

public class MeshGroup
{
    public GroupType GroupType { get; set; } = GroupType.StaticObject;
    public float[] LODDistances { get; set; } = [];
    public Vector3 Position { get; set; } = Vector3.Zero;
    public Quaternion Rotation { get; set; } = Quaternion.Identity;

    public NPlugDyna_SKinematicConstraint? KinematicConstraint { get; set; }
    public NPlugDynaObjectModel_SInstanceParams? DynaObjectModelParams { get; set; }
    public LegacyGameplayId? TriggerGameplayId { get; set; }
    public EWaypointType? WaypointType { get; set; }
    public bool? WaypointNoRespawn { get; set; }
    public CPlugSpawnModel? WaypointSpawnModel { get; set; }
    public Vec3? GameplayMainDir { get; set; } = new Vec3(0, 0, 1);
    public int? RelativeMovingParentIndex { get; set; } = null;

    public int? GroupLink { get; set; } = null;

    public int? VariantIndex { get; set; }
}
public class VariantGroup
{
    public Dictionary<string, string> Tags { get; set; } = [];
    public bool HiddenInManualCycle { get; set; }

}

public class NormalizedItem
{

    public NormalizedMesh[] Meshes { get; set; } = [];
    public NormalizedLight[] Lights { get; set; } = [];
    public MeshGroup[] Groups { get; set; } = [];
    public VariantGroup[] VariantGroups { get; set; } = [];

    public object? SourceData { get; set; } // Data where this data comes from
    public CGameItemPlacementParam? PlacementParam { get; set; }
    public byte[]? IconWebP { get; set; }
    public Color[,]? Icon { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
   
}
*/
