using GBX.NET;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Plug;

namespace TM_GenericMapping.Items;


public class NormalizedSubmesh
{
    public int IndexStart { get; set; }
    public int IndexCount { get; set; }
    public CPlugMaterialUserInst Material { get; set; }
}
public class NormalizedMesh
{
    public Vec3[] Positions { get; set; } = [];
    public Vec3[] Normals { get; set; } = [];
    public Vec2[] TexCoords { get; set; } = [];        // diffuse
    public Vec2[] LightmapCoords { get; set; } = [];        // lightmap (can be null/auto-generated)
    public int[] Indices { get; set; } = [];    // triangles
    public NormalizedSubmesh[] Submeshes { get; set; } = [];

    public object? SourceData { get; set; } // Data where this data comes from
    public CGameItemPlacementParam? PlacementParam { get; set; }
    public byte[]? IconWebP { get; set; }

}
