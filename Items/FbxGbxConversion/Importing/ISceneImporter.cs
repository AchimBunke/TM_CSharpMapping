using System.Numerics;

namespace TM_GenericMapping.Items.FbxGbxConversion.Importing;

/// <summary>
/// Neutral, library-agnostic representation of an imported 3D scene (FBX or otherwise).
/// Everything downstream of <see cref="ISceneImporter"/> works exclusively against these types
/// and <see cref="System.Numerics"/> math - no importer-library types (e.g. Assimp.*) should leak
/// past this boundary. Coordinate-space, unit-scale, and up-axis conversions are the sole
/// responsibility of the <see cref="ISceneImporter"/> implementation.
/// </summary>
public interface ISceneImporter
{
    ImportedScene Import(Stream stream);
}

public class ImportedScene
{
    public ImportedNode RootNode { get; set; } = null!;
    public List<ImportedMesh> Meshes { get; set; } = new();
    public List<ImportedMaterial> Materials { get; set; } = new();
    public List<ImportedLight> Lights { get; set; } = new();

    public IEnumerable<ImportedNode> CollectNodes() => CollectNodes(RootNode);

    private static IEnumerable<ImportedNode> CollectNodes(ImportedNode node)
    {
        yield return node;
        foreach (var child in node.Children)
            foreach (var n in CollectNodes(child))
                yield return n;
    }
}

public class ImportedNode
{
    public required string Name { get; set; }
    public Matrix4x4 LocalTransform { get; set; }

    /// <summary>Transform of this node relative to the scene root, in normalized (meters) space, already
    /// including any importer-specific unit/axis conversion.</summary>
    public Matrix4x4 GlobalTransform { get; set; }

    public List<int> MeshIndices { get; set; } = new();
    public List<ImportedNode> Children { get; set; } = new();
    public ImportedNode? Parent { get; set; }
}

public class ImportedMesh
{
    public required string Name { get; set; }
    public int MaterialIndex { get; set; }

    public Vector3[] Positions { get; set; } = [];
    public Vector3[] Normals { get; set; } = [];
    public Vector3[]? Tangents { get; set; }
    public Vector3[]? BiTangents { get; set; }

    /// <summary>Texture coordinate channels, indexed [channel][vertex].</summary>
    public Vector2[][] TextureCoordinateChannels { get; set; } = [];

    /// <summary>Vertex color channels, indexed [channel][vertex] as (R,G,B,A) in 0..1 range.</summary>
    public Vector4[][] VertexColorChannels { get; set; } = [];

    /// <summary>Triangle indices (already triangulated), 3 per face.</summary>
    public int[] Indices { get; set; } = [];
}

public class ImportedMaterial
{
    public required string Name { get; set; }
}

public enum ImportedLightType
{
    Point,
    Spot,
    Directional,
    Area,
}

public class ImportedLight
{
    public required string NodeName { get; set; }
    public ImportedLightType Type { get; set; }
    public Matrix4x4 GlobalTransform { get; set; }
}
