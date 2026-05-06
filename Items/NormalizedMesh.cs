using GBX.NET;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Plug;

namespace TM_GenericMapping.Items;


public class NormalizedSubmesh
{
    public Vec3[] Positions { get; set; } = [];
    public Vec3[] Normals { get; set; } = [];
    public Vec2[]? TexCoords { get; set; }
    public Vec2[]? LightmapCoords { get; set; }
    public int[]? Colors { get; set; }
    public int[] Indices { get; set; } = [];     // local 0-based
    public CPlugMaterialUserInst Material { get; set; }

    public Vec3[]? TangentUs { get; set; }  // per vertex, same length as Positions
    public Vec3[]? TangentVs { get; set; }  // per vertex, same length as Positions

}
public class NormalizedMesh
{

    public NormalizedSubmesh[] Submeshes { get; set; } = [];

    public object? SourceData { get; set; } // Data where this data comes from
    public CGameItemPlacementParam? PlacementParam { get; set; }
    public byte[]? IconWebP { get; set; }
    public Color[,]? Icon { get; set; }

}
