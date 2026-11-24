using GBX.NET;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.Plug;
using System.Numerics;
using static GBX.NET.Engines.GameData.CGameItemModel;
using static GBX.NET.Engines.Meta.CPlugVegetTreeModel;
using static GBX.NET.Engines.Plug.CPlugSolid2Model;
using static GBX.NET.Engines.Plug.CPlugSurface;
using static GBX.NET.Engines.Plug.CPlugVertexStream;

namespace TM_GenericMapping.Items.MeshCompilation;

public class NormalizedItem
{
    public CGameItemPlacementParam? PlacementParam { get; set; }
    public byte[]? IconWebP { get; set; }
    public Color[,]? Icon { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public EWaypointType WaypointType { get; set; } = EWaypointType.None;

    public EItemFlags ItemFlags { get; set; } = EItemFlags.None;


    public Dictionary<int, NormalizedMesh> MeshPool { get; set; } = new();
    public Dictionary<int, NormalizedShape> ShapePool { get; set; } = new();
    public Dictionary<int, NormalizedLight> LightPool { get; set; } = new();
    public Dictionary<int, NormalizedModel> ModelPool { get; set; } = new();
    public Dictionary<int, NormalizedMaterial> MaterialPool { get; set; } = new();

    public NormalizedModel Model { get; set; } = null!;

}
public record struct DataDeclType(EPlugVDcl Semantic, EPlugVDclSpace Space, EPlugVDclType Type);
public class NormalizedMesh
{
    public Vec3[] Positions { get; set; } = [];
    public Vec3[] Normals { get; set; } = [];

    public Vec2[]? TexCoords0 { get; set; }
    public Vec2[]? LightmapCoords { get; set; }
    public Vec2[]? TexCoords1 { get; set; } // tree models, unknown role
    public Vec2[]? TexCoords2 { get; set; } // tree models, unknown role
    public int[]? Colors { get; set; } // packed argb color
    public int[]? Colors2 { get; set; } // packed argb color
    public int[] Indices { get; set; } = [];     // local 0-based


   

    public Vec3[]? TangentUs { get; set; }  // per vertex, same length as Positions
    public Vec3[]? TangentVs { get; set; }  // per vertex, same length as Positions

    public bool DirtyVision { get; set; } = false;
    public bool IsStatic { get; set; } = true;
    public bool SkipVision { get; set; } = false;
    public byte CompressTexCoords { get; set; } = 0;
    public bool CompressLocalFloat3 { get; set; } = true;

    public string Name { get; set; } = string.Empty;

}
public enum MaterialType
{
    UserInstance,
    TextureSet,
}
public enum AssetType
{
    ImgFile,
    Bitmap,
    // not used yet: reserved for particle fx (Nadeo's default particles don't reference materials/shaders)
    Material,
    Shader,
    /// <summary>
    /// Texture inside the game data, by its path below GameData (e.g. "Stadium\Media\Texture\Image\RoadIce_D.dds").
    /// Used as is by custom materials (CPlugMaterialUserInst.UserTextures): nothing is shipped with the item.
    /// </summary>
    GameTexture,
}
public class NormalizedMaterial
{
    public MaterialType Type { get; set; } = MaterialType.UserInstance;
    public CPlugMaterialUserInst? MaterialUserInst { get; set; } = null;
    /// <summary>Boilerplate: to be created from <see cref="TextureAssets"/> by the GBX creation code.</summary>
    public TextureSet? TextureSet { get; set; } = null;
    public NormalizedTextureAssets? TextureAssets { get; set; } = null;
    public string Name { get; set; } = string.Empty;
}

/// <summary>Resolved external file (absolute, or as given if no base directory is known).</summary>
public record NormalizedAsset(string Path, AssetType Type);
public record NormalizedTextureAssets(
    NormalizedAsset? ColorImage, 
    NormalizedAsset? NormalImage, 
    NormalizedAsset? RoughnessImage, 
    NormalizedAsset? DiffuseBitmap, 
    NormalizedAsset? NormalBitmap, 
    NormalizedAsset? RoughnessBitmap, 
    MaterialId? SurfacePhysicsId, 
    GameplayId? SurfaceGameplayId);
public class NormalizedShape
{
    public Vec3[] Positions { get; set; } = [];
    public int[] Indices { get; set; } = [];
    public MaterialId[] SurfaceMaterialIds { get; set; } = [];
    public GameplayId[] SurfaceGameplayIds { get; set; } = [];

}
public class NormalizedLight
{
    public CPlugLightUserModel? LightModel { get; set; } = null!;
    public Light? Light { get; set; } = null;
    public string Name { get; set; } = string.Empty;
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
public enum ModelType
{
    Container,
    Static,
    Dynamic,
    Trigger_Special,
    Trigger_Waypoint,
    Variant_List,
    ItemAttachmentPoint,
    ItemAttachmentGroup,
    VegetTreeModel,
    ParticleFx,
}

public enum ShapeRole
{
    Static,
    Dynamic,
    Trigger_Special,
    Trigger_Waypoint,

}




public class NormalizedModel
{
    public ModelType Type { get; set; }


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

    // effects properties
    public Vec3? GameplayMainDir { get; set; }

    // prefab properties
    public List<EntityRef> Children { get; set; } = [];

    // variant list properties
    public List<NormalizedVariant> Variants { get; set; } = [];


    // single attachement point
    public ItemAttachmentLocation? AttachmentPoint { get; set; } = null;

    // attachment group
    public List<ItemAttachmentLocation> AttachmentGroup { get; set; } = [];

    // VegetTreeModel
    public CPlugVegetTreeModel? VegetTreeModel { get; set; } = null;

    // ParticleFx: stored as-is (too many fields to normalize), the compiler references this node directly
    public CPlugFxSystem? FxSystem { get; set; } = null;
}

public abstract class RefBase
{
    public Guid Id { get; init; } = Guid.NewGuid();
}
public abstract class EntityRefBase : RefBase
{
    public int ModelKey { get; set; } = -1;
}
public class NormalizedVariant : EntityRefBase
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

    public int MaterialKey { get; set; } = -1;

    public int LODMask { get; set; } = 1;
    public int? SmoothingGroup { get; set; } = 0;
    public PreLightGen? PreLightGenerator { get; set; }
    public MeshProperties Properties { get; set; } = MeshProperties.None;
}

public class ShapeRef : RefBase
{
    public int ShapeKey { get; set; }
    public ShapeRole Role { get; set; }
}
public class LightRef : RefBase
{
    public int LightKey { get; set; } = -1;
    public Vector3 Position { get; set; } = Vector3.Zero;
    public Quaternion Rotation { get; set; } = Quaternion.Identity;
}