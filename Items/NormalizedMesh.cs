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
    Disabled = 1 << 0,
    Invisible = 1 << 1,
    NonCollidable = 1 << 2
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

    public Vec3[]? TangentUs { get; set; }  // per vertex, same length as Positions
    public Vec3[]? TangentVs { get; set; }  // per vertex, same length as Positions

    public SubmeshType Type { get; set; } = SubmeshType.Mesh;
    public SubmeshProperties Properties { get; set; } = SubmeshProperties.None;

    public string Name { get; set; } = string.Empty;
    public NormalizedMesh AsMesh()
    {
        return new NormalizedMesh()
        {
            Submeshes = [this],
        };
    }
}

public class NormalizedMesh
{

    public NormalizedSubmesh[] Submeshes { get; set; } = [];

    public object? SourceData { get; set; } // Data where this data comes from
    public CGameItemPlacementParam? PlacementParam { get; set; }
    public byte[]? IconWebP { get; set; }
    public Color[,]? Icon { get; set; }

}
