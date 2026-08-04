using GBX.NET;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Plug;
using static GBX.NET.Engines.Plug.CPlugSurface;

namespace TM_GenericMapping.Items;

public enum SubmeshType
{
    Mesh,
    Dyna_Shape,
    Static_Shape,
    Trigger_Special,
    Trigger_Waypoint,
}

[Flags]
public enum SubmeshProperties
{
    None = 0,
    Enabled = 1 << 0,
    Visible = 1 << 1,
    Collidable = 1 << 2,
    LOD = 1 << 3
}

public class NormalizedSubmesh
{
    public Vec3[] Positions { get; set; } = [];
    public Vec3[] Normals { get; set; } = [];
    public Vec2[]? TexCoords { get; set; }
    public Vec2[]? LightmapCoords { get; set; }
    public int[]? Colors { get; set; }
    public int[] Indices { get; set; } = [];     // local 0-based
    public CPlugMaterialUserInst Material { get; set; }
    public MaterialId[]? SurfaceMaterialIds { get; set; }

    public int LODMask { get; set; } = 1;

    public Vec3[]? TangentUs { get; set; }  // per vertex, same length as Positions
    public Vec3[]? TangentVs { get; set; }  // per vertex, same length as Positions

    public SubmeshType Type { get; set; } = SubmeshType.Mesh;
    public SubmeshProperties Properties { get; set; } = SubmeshProperties.None;

    public string Name { get; set; } = string.Empty;
    //public NormalizedMesh AsMesh()
    //{
    //    return new NormalizedMesh()
    //    {
    //        Submeshes = [this],
    //    };
    //}
}

public class NormalizedMesh
{

    public NormalizedSubmesh[] Submeshes { get; set; } = [];

    public object? SourceData { get; set; } // Data where this data comes from
    public CGameItemPlacementParam? PlacementParam { get; set; }
    public byte[]? IconWebP { get; set; }
    public Color[,]? Icon { get; set; }

    public float[] LODDistances { get; set; } = [];


    public static bool IsVisibleInLod(int lodMask, int lod)
    {
        return (lodMask & (1 << lod)) != 0;
    }
    public static int LodMaskFromLods(params int[] lods)
    {
        int mask = 0;

        foreach (int lod in lods)
            mask |= 1 << lod;

        return mask;
    }

    public static bool IsVisibleInAllLods(int lodMask, int lodCount)
    {
        return lodMask == GetAllLodsMask(lodCount);
    }

    public static int GetAllLodsMask(int lodCount)
    {
        return (1 << lodCount) - 1;
    }
    public static int SetLod(int lodMask, int lod, bool enabled)
    {
        int bit = 1 << lod;

        return enabled
            ? lodMask | bit      // set bit to 1
            : lodMask & ~bit;    // set bit to 0
    }
}
