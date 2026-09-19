/*
using Assimp;
using EarcutDotNet;
using System.Numerics;
using TM_GenericMapping.Items.FbxGbxConverter;
using Matrix4x4 = System.Numerics.Matrix4x4;

namespace TM_GenericMapping.Items.FbxGbxConversion.Importing;

/// <summary>
/// TODO: If used needs to be re-implemented for current required scene structure.
/// </summary>
public class AssimpNetSceneImporter : ISceneImporter
{
    static readonly Matrix4x4 GbxCoordinateSpaceFixup =
        Matrix4x4.Create(
            1, 0, 0, 0,
            0, 1, 0, 0,
            0, 0, 1, 0,
            0, 0, 0, 1) *
        Matrix4x4.CreateScale(new Vector3(0.01f, 0.01f, 0.01f));
    public ImportedScene Import(Stream stream)
    {
        using var context = new AssimpContext();
        //context.SetConfig(new Assimp.Configs.FloatPropertyConfig("AI_CONFIG_GLOBAL_SCALE_FACTOR_KEY", 1f));

        if (stream.CanSeek)
            stream.Seek(0, SeekOrigin.Begin);

        PostProcessSteps postProcess =
            PostProcessSteps.CalculateTangentSpace |
            PostProcessSteps.FlipWindingOrder;
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

        importedScene.RootNode = ConvertNode(scene.RootNode, null, Assimp.Matrix4x4.Identity);

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

    ImportedNode ConvertNode(Node node, ImportedNode? parent, Assimp.Matrix4x4 parentGlobal)
    {
        // apply cm -> m conversion exactly once, at the root, consistent with legacy FbxSceneReader.GetGlobalTransform
        Assimp.Matrix4x4 local = node.Transform;
        Assimp.Matrix4x4 global = local * parentGlobal;

        var importedNode = new ImportedNode
        {
            Name = node.Name,
            LocalTransform = ToNumerics(local),
            GlobalTransform = ToNumerics(global),
            MeshIndices = node.MeshIndices?.ToList() ?? new List<int>(),
            Parent = parent,
        };

        foreach (var child in node.Children)
            importedNode.Children.Add(ConvertNode(child, importedNode, global));

        return importedNode;
    }

    ImportedMesh ConvertMesh(Assimp.Mesh mesh)
    {
        var indices = Triangulate(mesh).ToArray();

        var texChannels = new Vector2[mesh.TextureCoordinateChannelCount][];
        for (int c = 0; c < mesh.TextureCoordinateChannelCount; c++)
            texChannels[c] = mesh.TextureCoordinateChannels[c].Select(tc => new Vector2(tc.X, tc.Y)).ToArray();

        var colorChannels = new Vector4[mesh.VertexColorChannelCount][];
        for (int c = 0; c < mesh.VertexColorChannelCount; c++)
            colorChannels[c] = mesh.VertexColorChannels[c].Select(cc => new Vector4(cc.R, cc.G, cc.B, cc.A)).ToArray();

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

    ImportedMaterial ConvertMaterial(Assimp.Material material)
    {
        return new ImportedMaterial
        {
            Name = material.Name,
        };
    }

    static Vector3 ToNumerics(Vector3D v) => new(v.X, v.Y, v.Z);

    /// <summary>
    /// Assimp matrices are row-major with translation stored in the last COLUMN (column-vector
    /// convention, v' = M * v). System.Numerics.Matrix4x4 is row-major with translation stored in
    /// the last ROW (row-vector convention, v' = v * M). Converting between the two requires a
    /// transpose, not a direct field copy, otherwise translation ends up in the wrong slot.
    /// </summary>
    static Matrix4x4 ToNumerics(Assimp.Matrix4x4 m) => new(
        m.A1, m.B1, m.C1, m.D1,
        m.A2, m.B2, m.C2, m.D2,
        m.A3, m.B3, m.C3, m.D3,
        m.A4, m.B4, m.C4, m.D4);

    static float GetDeterminant3x3(Assimp.Matrix4x4 m)
    {
        // Assimp.Matrix4x4 fields are A1..D4 (row-major: A=row1, B=row2, C=row3)
        return m.A1 * (m.B2 * m.C3 - m.B3 * m.C2)
             - m.A2 * (m.B1 * m.C3 - m.B3 * m.C1)
             + m.A3 * (m.B1 * m.C2 - m.B2 * m.C1);
    }

    static int[] FlipWinding(int[] indices)
    {
        var result = new int[indices.Length];
        for (int i = 0; i < indices.Length; i += 3)
        {
            result[i] = indices[i];
            result[i + 1] = indices[i + 2]; // swap 1 and 2
            result[i + 2] = indices[i + 1];
        }
        return result;
    }

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

    /// <summary>
    /// Rebuilds a full triangle index list for an Assimp Mesh, using our own
    /// per-face earcut triangulation instead of Assimp's built-in Triangulate postprocess step.
    /// Assumes the mesh was imported WITHOUT PostProcessSteps.Triangulate,
    /// so mesh.Faces may still contain n-gons (Face.IndexCount > 3).
    /// </summary>
    static List<int> Triangulate(Assimp.Mesh mesh)
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

                Vector3D n0 = mesh.Normals[i0];
                Vector3D n1 = mesh.Normals[i1];
                Vector3D n2 = mesh.Normals[i2];
                Vector3D n3 = mesh.Normals[i3];
                n0.Normalize();
                n1.Normalize();
                n2.Normalize();
                n3.Normalize();

                // Dot product of the two corners each diagonal would connect.
                // Higher dot = more similar normals = smoother interpolation along that seam.
                float dot02 = Vector3D.Dot(n0, n2);
                float dot13 = Vector3D.Dot(n1, n3);

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
                Vector3D v = mesh.Vertices[idx];
                faceVerts[i] = new Vector3(v.X, v.Y, v.Z);
            }

            var localTris = FaceTriangulator.Triangulate(faceVerts.AsSpan());

            // Map local (0..N-1) indices back to this face's actual mesh-vertex indices
            foreach (int localIdx in localTris)
                globalIndices.Add(face.Indices[localIdx]);
        }
        return globalIndices;
    }
    static class FaceTriangulator
    {
        [ThreadStatic] static double[] _scratchFlat = null!;

        /// <summary>
        /// Computes a robust face normal using Newell's method.
        /// Works even for near-planar / slightly warped or collinear-heavy polygons,
        /// unlike a naive cross-product of the first 3 vertices.
        /// </summary>
        static Vector3 ComputeNewellNormalAssimp(ReadOnlySpan<Vector3> verts)
        {
            float nx = 0, ny = 0, nz = 0;
            int n = verts.Length;
            for (int i = 0; i < n; i++)
            {
                var c = verts[i];
                var nx2 = verts[(i + 1) % n];
                nx += (c.Y - nx2.Y) * (c.Z + nx2.Z);
                ny += (c.Z - nx2.Z) * (c.X + nx2.X);
                nz += (c.X - nx2.X) * (c.Y + nx2.Y);
            }
            return new Vector3(nx, ny, nz);
        }

        public enum Axis { X, Y, Z }

        /// <summary>
        /// Picks the two axes to project onto by dropping whichever axis
        /// the normal points most strongly along (largest abs component).
        /// Returns axes in an order that preserves consistent winding
        /// (see note in Triangulate below re: flipping for negative dominant axis).
        /// </summary>
        public static (Axis axis1, Axis axis2, bool flip) PickProjectionAxes(Vector3 normal)
        {
            float ax = MathF.Abs(normal.X);
            float ay = MathF.Abs(normal.Y);
            float az = MathF.Abs(normal.Z);

            if (az >= ax && az >= ay)
                return (Axis.X, Axis.Y, normal.Z < 0);
            if (ax >= ay && ax >= az)
                return (Axis.Y, Axis.Z, normal.X < 0);
            return (Axis.Z, Axis.X, normal.Y < 0);

        }

        static float GetComponent(Vector3 v, Axis axis) => axis switch
        {
            Axis.X => v.X,
            Axis.Y => v.Y,
            Axis.Z => v.Z,
            _ => throw new ArgumentOutOfRangeException()
        };

        /// <summary>
        /// Triangulates a single face (list of 3D vertex positions, in original polygon order)
        /// and returns local indices (0..N-1) into that same list, in triangle-list order (3 per tri).
        /// Handles winding correction so output triangles face the same way as the input normal.
        /// </summary>
        public static int[] Triangulate(ReadOnlySpan<Vector3> faceVerts)
        {
            int n = faceVerts.Length;
            if (n < 3) return Array.Empty<int>();
            if (n == 3) return new int[] { 0, 1, 2 };

            Vector3 normal = ComputeNewellNormalAssimp(faceVerts);
            var (axis1, axis2, flip) = PickProjectionAxes(normal);

            if (_scratchFlat == null || _scratchFlat.Length < n * 2)
                _scratchFlat = new double[Math.Max(n * 2, 64)];

            for (int i = 0; i < n; i++)
            {
                _scratchFlat[i * 2] = GetComponent(faceVerts[i], axis1);
                _scratchFlat[i * 2 + 1] = GetComponent(faceVerts[i], axis2);
            }

            // Only pass the actually-used slice if your earcut overload supports Span/ArraySegment;
            // otherwise trim to a right-sized array only when count doesn't match buffer length.
            double[] flatInput = (_scratchFlat.Length == n * 2)
                ? _scratchFlat
                : _scratchFlat[0..(n * 2)];

            var localIndices = Earcut.Triangulate(flatInput, null, 2);

            if (flip)
                for (int i = 0; i + 2 < localIndices.Length; i += 3)
                    (localIndices[i + 1], localIndices[i + 2]) = (localIndices[i + 2], localIndices[i + 1]);

            return localIndices;
        }
    }
}



*/