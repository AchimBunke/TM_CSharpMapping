
/*
using SharpAssimp;
using SharpAssimp.Configs;
using System.Numerics;
using TM_GenericMapping.Common;
using TM_GenericMapping.Items.FbxGbxConverter;
using Matrix4x4 = System.Numerics.Matrix4x4;

namespace TM_GenericMapping.Items.FbxGbxConversion.Importing;


public class SharpAssimpSceneImporter : ISceneImporter
{
    /// <summary>
    /// Scene-wide transform applied once, up front (see class remarks), to convert from Assimp's
    /// normalized import space (cm units, right-handed, Y-up, X-right, Z-towards-viewer) into GBX's
    /// coordinate space (meters, right-handed, X-left, Y-up, Z-back): a 0.01 scale combined with a
    /// 180deg rotation around Y.
    /// </summary>
    static readonly Matrix4x4 GbxCoordinateSpaceFixup =
    Matrix4x4.CreateRotationX(-MathF.PI / 2f) *
    Matrix4x4.CreateScale(new Vector3(0.01f, 0.01f, -0.01f));

    public ImportedScene Import(Stream stream)
    {
        using var context = new AssimpContext();
        context.SetConfig(new FloatPropertyConfig("AI_CONFIG_GLOBAL_SCALE_FACTOR_KEY", 1f));

        if (stream.CanSeek)
            stream.Seek(0, SeekOrigin.Begin);

        PostProcessSteps postProcess =
            PostProcessSteps.CalculateTangentSpace |
            PostProcessSteps.Triangulate;
        var scene = context.ImportFileFromStream(stream, postProcess);

        var importedScene = Convert(scene);

        ApplyGbxCoordinateSpaceFixup(importedScene);

        return importedScene;
    }

    ImportedScene Convert(Scene scene)
    {
        var importedScene = new ImportedScene();

        importedScene.Meshes = scene.Meshes.Select(ConvertMesh).ToList();
        importedScene.Materials = scene.Materials.Select(ConvertMaterial).ToList();

        importedScene.RootNode = ConvertNode(scene.RootNode, null, Matrix4x4.Identity);

        if (scene.HasLights)
        {
            var nodesByName = importedScene.CollectNodes().ToDictionary(n => n.Name, n => n, StringComparer.Ordinal);
            foreach (var light in scene.Lights)
            {
                if (!nodesByName.TryGetValue(light.Name, out var node))
                    continue;

                importedScene.Lights.Add(new ImportedLight
                {
                    NodeName = light.Name,
                    Type = light.LightType switch
                    {
                        LightSourceType.Directional => ImportedLightType.Directional,
                        LightSourceType.Point => ImportedLightType.Point,
                        LightSourceType.Spot => ImportedLightType.Spot,
                        LightSourceType.Area => ImportedLightType.Area,
                        _ => ImportedLightType.Point,
                    },
                    Direction = new Vector3(light.Direction.X, light.Direction.Y, light.Direction.Z),
                    GlobalTransform = node.GlobalTransform,
                });
            }
        }

        return importedScene;
    }

    ImportedNode ConvertNode(Node node, ImportedNode? parent, Matrix4x4 parentGlobal)
    {
        Matrix4x4 local = node.Transform;
        Matrix4x4 global = local * parentGlobal;

        var importedNode = new ImportedNode
        {
            Name = node.Name,
            LocalTransform = local,
            GlobalTransform = global,
            MeshIndices = node.MeshIndices?.ToList() ?? new List<int>(),
            Parent = parent,
        };

        foreach (var child in node.Children)
            importedNode.Children.Add(ConvertNode(child, importedNode, global));

        return importedNode;
    }

    ImportedMesh ConvertMesh(Mesh mesh)
    {
        var indices = Triangulate(mesh).ToArray();

        var texChannels = new Vector2[mesh.TextureCoordinateChannelCount][];
        for (int c = 0; c < mesh.TextureCoordinateChannelCount; c++)
            texChannels[c] = mesh.TextureCoordinateChannels[c].Select(tc => new Vector2(tc.X, tc.Y)).ToArray();

        var colorChannels = new Vector4[mesh.VertexColorChannelCount][];
        for (int c = 0; c < mesh.VertexColorChannelCount; c++)
            colorChannels[c] = mesh.VertexColorChannels[c].Select(cc =>cc).ToArray();

        return new ImportedMesh
        {
            Name = mesh.Name,
            MaterialIndex = mesh.MaterialIndex,
            Positions = mesh.Vertices.Select(v => new Vector3(v.X, v.Y, v.Z)).ToArray(),
            Normals = mesh.Normals.Select(v => new Vector3(v.X, v.Y, v.Z)).ToArray(),
            Tangents = mesh.HasTangentBasis ? mesh.Tangents.Select(v => new Vector3(v.X, v.Y, v.Z)).ToArray() : null,
            BiTangents = mesh.HasTangentBasis ? mesh.BiTangents.Select(v => new Vector3(v.X, v.Y, v.Z)).ToArray() : null,
            TextureCoordinateChannels = texChannels,
            VertexColorChannels = colorChannels,
            Indices = indices,
        };
    }

    ImportedMaterial ConvertMaterial(Material material)
    {
        return new ImportedMaterial
        {
            Name = material.Name,
        };
    }

    /// <summary>
    /// Applies <see cref="GbxCoordinateSpaceFixup"/> once, uniformly, to every piece of imported data
    /// that lives in scene/world space: node local and global transforms, mesh vertex positions,
    /// normals, tangents and bitangents (via the normal matrix, so shearing/non-uniform scale in the
    /// fixup - none currently, but keeps this correct if that ever changes), and light directions.
    /// This is the single place that knows how to go from Assimp/FBX space to GBX space; everything
    /// else downstream just consumes already-correct, GBX-space data.
    /// </summary>
    static void ApplyGbxCoordinateSpaceFixup(ImportedScene scene)
    {
        Matrix4x4.Invert(GbxCoordinateSpaceFixup, out var inverseFixup);
        Matrix4x4 normalFixup = Matrix4x4.Transpose(inverseFixup);

        foreach (var node in scene.CollectNodes())
        {
            float det = node.LocalTransform.GetDeterminant();
            if (det < 0)
                Console.WriteLine($"Node '{node.Name}' has NEGATIVE determinant local transform ({det})");
        }

        foreach (var node in scene.CollectNodes())
        {
            node.LocalTransform = inverseFixup * node.LocalTransform * GbxCoordinateSpaceFixup;
            node.GlobalTransform = inverseFixup * node.GlobalTransform * GbxCoordinateSpaceFixup;

        }

        foreach (var mesh in scene.Meshes)
        {
            for (int i = 0; i + 2 < mesh.Indices.Length; i += 3)
                (mesh.Indices[i + 1], mesh.Indices[i + 2]) = (mesh.Indices[i + 2], mesh.Indices[i + 1]);

            for (int i = 0; i < mesh.Positions.Length; i++)
                mesh.Positions[i] = Vector3.Transform(mesh.Positions[i], GbxCoordinateSpaceFixup);

            for (int i = 0; i < mesh.Normals.Length; i++)
                mesh.Normals[i] = Vector3.Normalize(Vector3.TransformNormal(mesh.Normals[i], normalFixup));

            if (mesh.Tangents is not null)
                for (int i = 0; i < mesh.Tangents.Length; i++)
                    mesh.Tangents[i] = Vector3.Normalize(Vector3.TransformNormal(mesh.Tangents[i], GbxCoordinateSpaceFixup));

            if (mesh.BiTangents is not null)
                for (int i = 0; i < mesh.BiTangents.Length; i++)
                    mesh.BiTangents[i] = Vector3.Normalize(Vector3.TransformNormal(mesh.BiTangents[i], GbxCoordinateSpaceFixup));
        }

        foreach (var light in scene.Lights)
        {
            light.Direction = Vector3.Normalize(Vector3.TransformNormal(light.Direction, GbxCoordinateSpaceFixup));
            light.GlobalTransform *= GbxCoordinateSpaceFixup;
        }
    }

    static List<int> Triangulate(Mesh mesh)
    {
        var globalIndices = new List<int>(mesh.FaceCount * 3); // rough capacity guess
        foreach (Face face in mesh.Faces)
        {
            if (face.IndexCount < 3)
                continue; // degenerate line/point face, skip

            if (face.IndexCount == 3)
            {
                // Already a triangle — no need to run it through earcut
                globalIndices.Add(face.Indices[0]);
                globalIndices.Add(face.Indices[1]);
                globalIndices.Add(face.Indices[2]);
                continue;
            }

            if (face.IndexCount == 4)
            {
                int i0 = face.Indices[0], i1 = face.Indices[1], i2 = face.Indices[2], i3 = face.Indices[3];

                Vector3 n0 = mesh.Normals[i0];
                Vector3 n1 = mesh.Normals[i1];
                Vector3 n2 = mesh.Normals[i2];
                Vector3 n3 = mesh.Normals[i3];
                n0 = n0.Normalized();
                n1 = n1.Normalized();
                n2 = n2.Normalized();
                n3 = n3.Normalized();

                // Dot product of the two corners each diagonal would connect.
                // Higher dot = more similar normals = smoother interpolation along that seam.
                float dot02 = Vector3.Dot(n0, n2);
                float dot13 = Vector3.Dot(n1, n3);

                // Pick the diagonal whose two endpoints have the MOST similar normals —
                // that's the seam that will interpolate most smoothly, minimizing visible discontinuity.
                if (dot02 >= dot13)
                {
                    globalIndices.Add(i0);
                    globalIndices.Add(i1);
                    globalIndices.Add(i2);
                    globalIndices.Add(i0);
                    globalIndices.Add(i2);
                    globalIndices.Add(i3);
                }
                else
                {
                    globalIndices.Add(i0);
                    globalIndices.Add(i1);
                    globalIndices.Add(i3);
                    globalIndices.Add(i1);
                    globalIndices.Add(i2);
                    globalIndices.Add(i3);
                }
                continue;
            }

            // Gather this face's vertex positions in polygon order
            var faceVerts = new Vector3[face.IndexCount];
            for (int i = 0; i < face.IndexCount; i++)
            {
                int idx = face.Indices[i];
                Vector3 v = mesh.Vertices[idx];
                faceVerts[i] = new Vector3(v.X, v.Y, v.Z);
            }

            var localTris = FaceTriangulator.Triangulate(faceVerts.AsSpan());

            // Map local (0..N-1) indices back to this face's actual mesh-vertex indices
            foreach (int localIdx in localTris)
                globalIndices.Add(face.Indices[localIdx]);
        }
        return globalIndices;
    }
}



*/