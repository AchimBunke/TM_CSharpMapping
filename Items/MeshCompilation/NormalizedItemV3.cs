using GBX.NET;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.Plug;
using System.Numerics;
using static GBX.NET.Engines.GameData.CGameItemModel;
using static GBX.NET.Engines.Plug.CPlugSolid2Model;
using static GBX.NET.Engines.Plug.CPlugSurface;

namespace TM_GenericMapping.Items.MeshCompilation;

public class NormalizedItemV3
{
    public CGameItemPlacementParam? PlacementParam { get; set; }
    public byte[]? IconWebP { get; set; }
    public Color[,]? Icon { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public EWaypointType WaypointType { get; set; } = EWaypointType.None;


    public Dictionary<int, NormalizedMeshV3> MeshPool { get; set; } = new();
    public Dictionary<int, NormalizedShapeV3> ShapePool { get; set; } = new();
    public Dictionary<int, NormalizedLightV3> LightPool { get; set; } = new();
    public Dictionary<int, NormalizedModelV3> ModelPool { get; set; } = new();

    public NormalizedModelV3 Model { get; set; } = null!;

}

public class NormalizedMeshV3
{
    public Vec3[] Positions { get; set; } = [];
    public Vec3[] Normals { get; set; } = [];

    public Vec2[]? TexCoords { get; set; }
    public Vec2[]? LightmapCoords { get; set; }
    public int[]? Colors { get; set; } // packed argb color
    public int[] Indices { get; set; } = [];     // local 0-based
    public CPlugMaterialUserInst Material { get; set; } = null!;
   

    public Vec3[]? TangentUs { get; set; }  // per vertex, same length as Positions
    public Vec3[]? TangentVs { get; set; }  // per vertex, same length as Positions

    public string Name { get; set; } = string.Empty;

}
public class NormalizedShapeV3
{
    public Vec3[] Positions { get; set; } = [];
    public int[] Indices { get; set; } = [];
    public MaterialId[] SurfaceMaterialIds { get; set; } = [];
    public Vec3 GameplayMainDir { get; set; } = new Vec3(0, 0, 1);
}
public class NormalizedLightV3
{
    public CPlugLightUserModel LightModel { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
}

[Flags]
public enum MeshPropertiesV3
{
    None = 0,
    Enabled = 1 << 0,
    Visible = 1 << 1,
    Collidable = 1 << 2,
    LOD = 1 << 3
}
public enum ModelTypeV3
{
    Container,
    Static,
    Dynamic,
    Trigger_Special,
    Trigger_Waypoint,
    Variant_List,
}
public enum ShapeRoleV3
{
    Static,
    Dynamic,
    Trigger_Special,
    Trigger_Waypoint,
}




public class NormalizedModelV3
{
    public ModelTypeV3 Type { get; set; }


    public List<MeshRef> Meshes { get; set; } = new();
    public List<ShapeRef> Shapes { get; set; } = new();
    public List<LightRef> Lights { get; set; } = new();

    // Mesh properties
    public float[] LODDistances { get; set; } = [];


    // checkpoint properties
    public EWaypointType? WaypointType { get; set; }
    public bool? WaypointNoRespawn { get; set; }

    // trigger special properties
    public LegacyGameplayId? TriggerGameplayId { get; set; }

    // prefab properties
    public List<EntityRef> Children { get; set; } = [];

    // variant list properties
    public List<NormalizedVariantV3> Variants { get; set; } = [];
}

public abstract class RefBase
{
    public Guid Id { get; init; } = Guid.NewGuid();
}
public abstract class EntityRefBase : RefBase
{
    public int ModelKey { get; set; } = -1;
}
public class NormalizedVariantV3 : EntityRefBase
{
    public Dictionary<string, string> Tags { get; set; } = [];
    public bool HiddenInManualCycle { get; set; }
}
public class EntityRef : EntityRefBase
{
    public Vector3 Position { get; set; } = Vector3.Zero;
    public Quaternion Rotation { get; set; } = Quaternion.Identity;

   


    // Dyna properties (here so dynamodel can be reused)
    public NPlugDyna_SKinematicConstraint? KinematicConstraint { get; set; }
    public NPlugDynaObjectModel_SInstanceParams? DynaObjectModelParams { get; set; }
    public int? RelativeMovingParentKey { get; set; } = null;

    // SpawnModel properties (here so spawnmodel can be reused)
    public Vector3? WaypointSpawnPosition { get; set; }
    public Quaternion? WaypointSpawnRotation { get; set; }
}
public class MeshRef : RefBase
{
    public int MeshKey { get; set; } = -1;

    public int LODMask { get; set; } = 1;
    public int? SmoothingGroup { get; set; } = 0;
    public PreLightGen? PreLightGenerator { get; set; }
    public MeshPropertiesV3 Properties { get; set; } = MeshPropertiesV3.None;
}

public class ShapeRef : RefBase
{
    public int ShapeKey { get; set; }
    public ShapeRoleV3 Role { get; set; }
}
public class LightRef : RefBase
{
    public int LightKey { get; set; } = -1;
    public Vector3 Position { get; set; } = Vector3.Zero;
    public Quaternion Rotation { get; set; } = Quaternion.Identity;
}