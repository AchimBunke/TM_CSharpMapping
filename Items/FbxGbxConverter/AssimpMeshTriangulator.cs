using System;
using System.Collections.Generic;
using System.Numerics;
using Assimp;

namespace TM_GenericMapping.Items.FbxGbxConverter;

/*
/// <summary>
/// Modifies an AssimpNet Assimp.Mesh in place:
///   1. Rebuilds mesh.Faces, triangulating quads by choosing the diagonal
///      that produces the most coplanar triangle pair (FBX SDK-style),
///      instead of a fixed 0-2 split.
///   2. Recomputes mesh.Normals from the final triangle list (angle-weighted
///      accumulation per vertex index).
///   3. Recomputes mesh.Tangents / mesh.BiTangents from the final triangle
///      list (Lengyel's method), orthogonalized against the new normals.
///
/// Vertex count and order are NOT changed — only Faces, Normals, Tangents,
/// and BiTangents are rewritten. This relies on your import already giving
/// one unique vertex per loop (Assimp's default when position+normal+UV
/// differ per loop, which is standard for FBX/Blender-exported meshes even
/// without post-processing flags). If two loops share position but differ
/// in normal/UV, they are already separate entries in mesh.Vertices, so
/// accumulation naturally respects hard edges / UV seams without extra
/// bucketing logic.
///
/// Usage:
///   AssimpMeshTriangulator.Process(mesh, tangentUVChannel: 0);
/// </summary>
public static class AssimpMeshTriangulator
{
    /// <param name="weldPositionEpsilon">
    /// Vertices within this distance are considered "the same point" for
    /// welding purposes. Tune to your model's scale.
    /// </param>
    /// <param name="weldNormalAngleDegrees">
    /// Two duplicate-position vertices are welded into the same smoothing
    /// cluster only if their ORIGINAL (pre-recompute) normals are within
    /// this angle of each other. This is what preserves curvature across
    /// adjacent panels (e.g. a balloon) while still respecting genuine
    /// hard edges, where the original normals actually diverge sharply.
    /// Small (~1-5°) = trust the original smoothing exactly.
    /// Larger (~30-45°) = smooth more aggressively, ignoring small
    /// original discontinuities (use if the source normals are noisy).
    /// </param>
    public static void Process(
        Mesh mesh,
        int tangentUVChannel = 0,
        float weldPositionEpsilon = 1e-4f,
        float weldNormalAngleDegrees = 5f)
    {
        if (mesh == null)
            throw new ArgumentNullException(nameof(mesh));
        if (mesh.VertexCount == 0)
            return;

        Vector3[] positions = ToVector3Array(mesh.Vertices);

        // Capture the ORIGINAL imported normals before we overwrite anything.
        // These encode the artist/exporter's smoothing intent and are the
        // signal we use to decide which duplicate-position vertices should
        // be treated as one smooth point vs. a real hard edge.
        Vector3[] originalNormals = mesh.HasNormals
            ? ToVector3Array(mesh.Normals)
            : null;

        List<Face> newFaces = TriangulateFaces(mesh.Faces, positions);
        mesh.Faces.Clear();
        mesh.Faces.AddRange(newFaces);
        mesh.SetIndices(BuildFlatIndices(newFaces), 3);
        mesh.PrimitiveType = PrimitiveType.Triangle;

        var triangles = new List<(int i0, int i1, int i2)>(newFaces.Count);
        foreach (var f in newFaces)
            triangles.Add((f.Indices[0], f.Indices[1], f.Indices[2]));

        int[] clusterOf = BuildSmoothingClusters(
            positions, originalNormals, weldPositionEpsilon, weldNormalAngleDegrees, out int clusterCount);

        Vector3[] normals = RecomputeNormals(positions, triangles, clusterOf, clusterCount);
        mesh.Normals.Clear();
        mesh.Normals.AddRange(ToVector3DArray(normals));

        if (mesh.TextureCoordinateChannels.Length > tangentUVChannel &&
            mesh.TextureCoordinateChannels[tangentUVChannel].Count == mesh.VertexCount)
        {
            Vector2[] uvs = ToVector2Array(mesh.TextureCoordinateChannels[tangentUVChannel]);
            (Vector3[] tangents, Vector3[] bitangents) = RecomputeTangents(
                positions, uvs, normals, triangles, clusterOf, clusterCount);

            mesh.Tangents.Clear();
            mesh.Tangents.AddRange(ToVector3DArray(tangents));

            mesh.BiTangents.Clear();
            mesh.BiTangents.AddRange(ToVector3DArray(bitangents));
        }
        // else: no UV channel available at that index, skip tangent recompute
        // (caller can check mesh.HasTangentBasis afterward).
    }

    // ---------------------------------------------------------------
    // Weld duplicate-position vertices into smoothing clusters, using
    // the ORIGINAL normals to decide what counts as "the same point"
    // vs. a genuine hard edge. Vertices with no original normal data
    // each get their own singleton cluster (falls back to flat/per-face
    // behavior for that vertex only).
    // ---------------------------------------------------------------
    private static int[] BuildSmoothingClusters(
        Vector3[] positions, Vector3[] originalNormals,
        float posEpsilon, float angleDegrees, out int clusterCount)
    {
        int n = positions.Length;
        var clusterOf = new int[n];
        for (int i = 0; i < n; i++)
            clusterOf[i] = -1;

        float cosThreshold = MathF.Cos(angleDegrees * MathF.PI / 180f);
        float invEps = 1f / MathF.Max(posEpsilon, 1e-8f);

        // Spatial hash: rounded position -> list of vertex indices.
        var buckets = new Dictionary<(long, long, long), List<int>>();
        for (int i = 0; i < n; i++)
        {
            var key = (
                (long)MathF.Round(positions[i].X * invEps),
                (long)MathF.Round(positions[i].Y * invEps),
                (long)MathF.Round(positions[i].Z * invEps));
            if (!buckets.TryGetValue(key, out var list))
                buckets[key] = list = new List<int>();
            list.Add(i);
        }

        int nextCluster = 0;
        // Each cluster tracks a running average normal so clustering is
        // order-independent-ish and tolerant of gradual normal drift
        // within the same weld point (rather than only comparing to the
        // very first member).
        var clusterAvgNormal = new List<Vector3>();
        var clusterMemberCount = new List<int>();

        foreach (var kvp in buckets)
        {
            var members = kvp.Value;
            var localClusters = new List<(Vector3 avg, int count, int id)>();

            foreach (int vi in members)
            {
                if (originalNormals == null)
                {
                    // No original normal data: each vertex is its own cluster.
                    clusterOf[vi] = nextCluster;
                    clusterAvgNormal.Add(positions[vi]); // placeholder, unused
                    clusterMemberCount.Add(1);
                    nextCluster++;
                    continue;
                }

                Vector3 n0 = originalNormals[vi];
                bool matched = false;

                for (int c = 0; c < localClusters.Count; c++)
                {
                    var (avg, count, id) = localClusters[c];
                    if (avg.LengthSquared() > 1e-12f && Vector3.Dot(Vector3.Normalize(avg), n0) >= cosThreshold)
                    {
                        Vector3 newAvg = avg + n0;
                        localClusters[c] = (newAvg, count + 1, id);
                        clusterOf[vi] = id;
                        matched = true;
                        break;
                    }
                }

                if (!matched)
                {
                    int id = nextCluster++;
                    localClusters.Add((n0, 1, id));
                    clusterOf[vi] = id;
                }
            }
        }

        clusterCount = nextCluster;
        return clusterOf;
    }

    // ---------------------------------------------------------------
    // Face triangulation with FBX-style diagonal selection
    // ---------------------------------------------------------------
    private static List<Face> TriangulateFaces(List<Face> sourceFaces, Vector3[] positions)
    {
        var result = new List<Face>(sourceFaces.Count * 2);

        foreach (var face in sourceFaces)
        {
            int n = face.IndexCount;

            if (n == 3)
            {
                result.Add(CloneTriFace(face.Indices[0], face.Indices[1], face.Indices[2]));
            }
            else if (n == 4)
            {
                int i0 = face.Indices[0], i1 = face.Indices[1], i2 = face.Indices[2], i3 = face.Indices[3];
                Vector3 v0 = positions[i0], v1 = positions[i1], v2 = positions[i2], v3 = positions[i3];

                Vector3 nA1 = Vector3.Cross(v1 - v0, v2 - v0);
                Vector3 nA2 = Vector3.Cross(v2 - v0, v3 - v0);
                float errorA = CoplanarError(nA1, nA2);

                Vector3 nB1 = Vector3.Cross(v2 - v1, v3 - v1);
                Vector3 nB2 = Vector3.Cross(v3 - v1, v0 - v1);
                float errorB = CoplanarError(nB1, nB2);

                if (errorA <= errorB)
                {
                    result.Add(CloneTriFace(i0, i1, i2));
                    result.Add(CloneTriFace(i0, i2, i3));
                }
                else
                {
                    result.Add(CloneTriFace(i1, i2, i3));
                    result.Add(CloneTriFace(i1, i3, i0));
                }
            }
            else if (n > 4)
            {
                // N-gon fallback: simple fan triangulation from the first
                // vertex. Fine for convex n-gons; if your source data has
                // concave n-gons, triangulate those in Blender before export
                // instead of relying on this fallback.
                for (int k = 1; k < n - 1; k++)
                    result.Add(CloneTriFace(face.Indices[0], face.Indices[k], face.Indices[k + 1]));
            }
            // n < 3 (degenerate/point/line): dropped.
        }

        return result;
    }

    private static Face CloneTriFace(int i0, int i1, int i2)
    {
        var f = new Face();
        f.Indices.Add(i0);
        f.Indices.Add(i1);
        f.Indices.Add(i2);
        return f;
    }

    private static int[] BuildFlatIndices(List<Face> faces)
    {
        var flat = new int[faces.Count * 3];
        int k = 0;
        foreach (var f in faces)
        {
            flat[k++] = f.Indices[0];
            flat[k++] = f.Indices[1];
            flat[k++] = f.Indices[2];
        }
        return flat;
    }

    private static float CoplanarError(Vector3 nA, Vector3 nB)
    {
        float lenA = nA.Length();
        float lenB = nB.Length();
        if (lenA < 1e-12f || lenB < 1e-12f)
            return 0f;
        float cosAngle = Vector3.Dot(nA / lenA, nB / lenB);
        return 1f - cosAngle;
    }

    // ---------------------------------------------------------------
    // Normal recomputation: angle-weighted face-normal accumulation
    // ---------------------------------------------------------------
    private static Vector3[] RecomputeNormals(
        Vector3[] positions, List<(int i0, int i1, int i2)> triangles, int[] clusterOf, int clusterCount)
    {
        // Accumulate into per-CLUSTER buckets, not per-vertex-index, so a
        // vertex's normal is blended across every triangle touching its
        // weld point — including triangles from neighboring quads/faces
        // that don't share its raw index. This is what restores curvature
        // (e.g. balloon panels) instead of flat-per-face normals.
        var clusterAccum = new Vector3[clusterCount];

        foreach (var (i0, i1, i2) in triangles)
        {
            Vector3 p0 = positions[i0], p1 = positions[i1], p2 = positions[i2];

            Vector3 faceNormalUn = Vector3.Cross(p1 - p0, p2 - p0);
            float faceLen = faceNormalUn.Length();
            if (faceLen < 1e-12f)
                continue;
            Vector3 faceNormal = faceNormalUn / faceLen;

            clusterAccum[clusterOf[i0]] += faceNormal * AngleAt(p2, p0, p1);
            clusterAccum[clusterOf[i1]] += faceNormal * AngleAt(p0, p1, p2);
            clusterAccum[clusterOf[i2]] += faceNormal * AngleAt(p1, p2, p0);
        }

        var clusterNormal = new Vector3[clusterCount];
        for (int c = 0; c < clusterCount; c++)
        {
            clusterNormal[c] = clusterAccum[c].LengthSquared() > 1e-12f
                ? Vector3.Normalize(clusterAccum[c])
                : Vector3.UnitY;
        }

        var result = new Vector3[positions.Length];
        for (int i = 0; i < positions.Length; i++)
            result[i] = clusterNormal[clusterOf[i]];
        return result;
    }

    private static float AngleAt(Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 ba = a - b;
        Vector3 bc = c - b;
        float lenBa = ba.Length();
        float lenBc = bc.Length();
        if (lenBa < 1e-12f || lenBc < 1e-12f)
            return 0f;
        float d = Vector3.Dot(ba / lenBa, bc / lenBc);
        d = Math.Clamp(d, -1f, 1f);
        return MathF.Acos(d);
    }

    // ---------------------------------------------------------------
    // Tangent/bitangent recomputation: Lengyel's method per triangle,
    // accumulated per vertex, orthogonalized + handedness-fixed.
    // ---------------------------------------------------------------
    private static (Vector3[] tangents, Vector3[] bitangents) RecomputeTangents(
        Vector3[] positions, Vector2[] uvs, Vector3[] normals,
        List<(int i0, int i1, int i2)> triangles, int[] clusterOf, int clusterCount)
    {
        // Same clustering as normals: blend tangent contributions across
        // every triangle at the weld point, not just the owning face's
        // two triangles. This is what removes the specular seam across
        // panel boundaries (the original problem), on top of the
        // curvature fix from clustered normals.
        var tanAccum = new Vector3[clusterCount];
        var bitanAccum = new Vector3[clusterCount];

        foreach (var (i0, i1, i2) in triangles)
        {
            Vector3 p0 = positions[i0], p1 = positions[i1], p2 = positions[i2];
            Vector2 uv0 = uvs[i0], uv1 = uvs[i1], uv2 = uvs[i2];

            Vector3 edge1 = p1 - p0;
            Vector3 edge2 = p2 - p0;
            Vector2 deltaUV1 = uv1 - uv0;
            Vector2 deltaUV2 = uv2 - uv0;

            float denom = deltaUV1.X * deltaUV2.Y - deltaUV2.X * deltaUV1.Y;
            if (MathF.Abs(denom) < 1e-12f)
                continue;

            float r = 1f / denom;
            Vector3 tangent = (edge1 * deltaUV2.Y - edge2 * deltaUV1.Y) * r;
            Vector3 bitangent = (edge2 * deltaUV1.X - edge1 * deltaUV2.X) * r;

            int c0 = clusterOf[i0], c1 = clusterOf[i1], c2 = clusterOf[i2];
            tanAccum[c0] += tangent;
            tanAccum[c1] += tangent;
            tanAccum[c2] += tangent;
            bitanAccum[c0] += bitangent;
            bitanAccum[c1] += bitangent;
            bitanAccum[c2] += bitangent;
        }

        var clusterTangent = new Vector3[clusterCount];
        var clusterBitangent = new Vector3[clusterCount];

        for (int c = 0; c < clusterCount; c++)
        {
            // Use any vertex's normal from this cluster for orthogonalization.
            // (All vertices sharing a cluster now have the same normal anyway,
            // by construction from RecomputeNormals.)
            Vector3 n = Vector3.UnitY; // set below once we find a member
            clusterTangent[c] = Vector3.Zero; // placeholder, filled below
        }

        var tangents = new Vector3[positions.Length];
        var bitangents = new Vector3[positions.Length];
        var clusterDone = new bool[clusterCount];

        for (int i = 0; i < positions.Length; i++)
        {
            int c = clusterOf[i];
            if (!clusterDone[c])
            {
                Vector3 n = normals[i];
                Vector3 t = tanAccum[c];

                if (t.LengthSquared() < 1e-12f)
                    t = Math.Abs(n.X) > 0.9f ? Vector3.Cross(n, Vector3.UnitY) : Vector3.Cross(n, Vector3.UnitX);

                Vector3 tOrtho = t - n * Vector3.Dot(n, t);
                if (tOrtho.LengthSquared() < 1e-12f)
                    tOrtho = Math.Abs(n.X) > 0.9f ? Vector3.Cross(n, Vector3.UnitY) : Vector3.Cross(n, Vector3.UnitX);
                tOrtho = Vector3.Normalize(tOrtho);

                Vector3 computedBitan = Vector3.Cross(n, tOrtho);
                float handedness = Vector3.Dot(computedBitan, bitanAccum[c]) < 0f ? -1f : 1f;

                clusterTangent[c] = tOrtho;
                clusterBitangent[c] = computedBitan * handedness;
                clusterDone[c] = true;
            }

            tangents[i] = clusterTangent[c];
            bitangents[i] = clusterBitangent[c];
        }

        return (tangents, bitangents);
    }

    // ---------------------------------------------------------------
    // Assimp.Vector3D / Vector3D(UV) <-> System.Numerics conversion helpers
    // ---------------------------------------------------------------
    private static Vector3[] ToVector3Array(List<Vector3D> src)
    {
        var arr = new Vector3[src.Count];
        for (int i = 0; i < src.Count; i++)
            arr[i] = new Vector3(src[i].X, src[i].Y, src[i].Z);
        return arr;
    }

    private static Vector2[] ToVector2Array(List<Vector3D> src)
    {
        // Assimp stores UVs as Vector3D (Z usually 0/unused for 2D UVs).
        var arr = new Vector2[src.Count];
        for (int i = 0; i < src.Count; i++)
            arr[i] = new Vector2(src[i].X, src[i].Y);
        return arr;
    }

    private static Vector3D[] ToVector3DArray(Vector3[] src)
    {
        var arr = new Vector3D[src.Length];
        for (int i = 0; i < src.Length; i++)
            arr[i] = new Vector3D(src[i].X, src[i].Y, src[i].Z);
        return arr;
    }
}
*/