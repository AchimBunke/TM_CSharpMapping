using Assimp;
using EarcutDotNet;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace TM_GenericMapping.Items.FbxGbxConverter;


public static class FaceTriangulator
{
    [ThreadStatic] static double[] _scratchFlat;

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
