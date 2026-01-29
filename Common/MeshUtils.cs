using System.Numerics;
using TM_GenericMapping.MediaTracker.IO;

namespace TM_GenericMapping.Common;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;


public class QEMSimplifier
{
    struct Quadric
    {
        public Matrix4x4 M;

        public static Quadric FromPlane(Vector3 n, float d)
        {
            var v = new Vector4(n, d);
            var Q = new Matrix4x4(
                v.X * v.X, v.X * v.Y, v.X * v.Z, v.X * v.W,
                v.Y * v.X, v.Y * v.Y, v.Y * v.Z, v.Y * v.W,
                v.Z * v.X, v.Z * v.Y, v.Z * v.Z, v.Z * v.W,
                v.W * v.X, v.W * v.Y, v.W * v.Z, v.W * v.W);
            return new Quadric() { M = Q };
        }

        public static Quadric operator +(Quadric a, Quadric b) => new Quadric() { M = a.M + b.M };

        public float Evaluate(Vector3 p)
        {
            var v = new Vector4(p, 1);
            Vector4 mv = Vector4.Transform(v, M);
            return Vector4.Dot(v, mv);
        }
    }

    class Edge
    {
        public int V0, V1;
        public float Cost;
        public Vector3 Optimal;
        public bool IsValid = true;
        public int HeapIndex = -1;
    }

    class MinHeap
    {
        private List<Edge> heap = new List<Edge>();

        public int Count => heap.Count;

        public void Add(Edge edge)
        {
            heap.Add(edge);
            edge.HeapIndex = heap.Count - 1;
            HeapifyUp(heap.Count - 1);
        }

        public Edge ExtractMin()
        {
            if (heap.Count == 0) return null;
            var min = heap[0];
            heap[0] = heap[heap.Count - 1];
            heap[0].HeapIndex = 0;
            heap.RemoveAt(heap.Count - 1);
            if (heap.Count > 0) HeapifyDown(0);
            min.HeapIndex = -1;
            return min;
        }

        public void UpdateCost(Edge edge, float newCost)
        {
            if (edge.HeapIndex < 0 || edge.HeapIndex >= heap.Count) return;
            float oldCost = edge.Cost;
            edge.Cost = newCost;
            if (newCost < oldCost) HeapifyUp(edge.HeapIndex);
            else HeapifyDown(edge.HeapIndex);
        }

        private void HeapifyUp(int idx)
        {
            while (idx > 0)
            {
                int parent = (idx - 1) / 2;
                if (heap[idx].Cost >= heap[parent].Cost) break;
                Swap(idx, parent);
                idx = parent;
            }
        }

        private void HeapifyDown(int idx)
        {
            while (true)
            {
                int left = 2 * idx + 1;
                int right = 2 * idx + 2;
                int smallest = idx;

                if (left < heap.Count && heap[left].Cost < heap[smallest].Cost)
                    smallest = left;
                if (right < heap.Count && heap[right].Cost < heap[smallest].Cost)
                    smallest = right;

                if (smallest == idx) break;
                Swap(idx, smallest);
                idx = smallest;
            }
        }

        private void Swap(int i, int j)
        {
            var temp = heap[i];
            heap[i] = heap[j];
            heap[j] = temp;
            heap[i].HeapIndex = i;
            heap[j].HeapIndex = j;
        }
    }

    TriangleObjectData mesh;
    Quadric[] Q;
    Dictionary<(int, int), Edge> edgeMap;
    List<HashSet<int>> vertexEdges; // Which edges use each vertex
    List<HashSet<int>> vertexTriangles; // Which triangles use each vertex
    List<int> vertices; // Indirection layer for vertex removal
    Vector3[] positions;
    System.Drawing.Color[] colors;
    bool[] isBoundary; // Track boundary vertices

    public QEMSimplifier(TriangleObjectData data)
    {
        mesh = data;
        Logger.Info($"QEMSimplifier initialized with {data.Vertices.Length} vertices and {data.Triangles.Length / 3} triangles");
    }

    public void Simplify(int targetVertexCount)
    {
        Logger.Info($"Starting simplification from {mesh.Vertices.Length} to {targetVertexCount} vertices");

        if (targetVertexCount >= mesh.Vertices.Length)
        {
            Logger.Warn("Target vertex count >= current vertex count, no simplification needed");
            return;
        }

        Initialize();
        BuildQuadrics();
        BuildEdgesAndAdjacency();

        var heap = new MinHeap();
        foreach (var edge in edgeMap.Values)
        {
            heap.Add(edge);
        }

        Logger.Info($"Priority queue initialized with {heap.Count} edges");

        int iterationCount = 0;
        int validVertices = positions.Length;

        while (validVertices > targetVertexCount && heap.Count > 0)
        {
            Edge edge;
            // Skip invalid edges
            do
            {
                edge = heap.ExtractMin();
                if (edge == null) break;
            } while (!edge.IsValid);

            if (edge == null || !edge.IsValid) break;

            if (vertices[edge.V0] < 0 || vertices[edge.V1] < 0)
            {
                continue; // Vertices already collapsed
            }

            CollapseEdgeFast(edge, heap);
            validVertices--;

            iterationCount++;
            if (iterationCount % 1000 == 0)
            {
                Logger.Info($"Progress: {validVertices} vertices remaining (target: {targetVertexCount})");
            }
        }

        RebuildMesh();
        Logger.Info($"Simplification complete: {mesh.Vertices.Length} vertices, {mesh.Triangles.Length / 3} triangles");
    }

    void Initialize()
    {
        positions = mesh.Vertices.ToArray();
        colors = mesh.Colors?.ToArray();
        vertices = Enumerable.Range(0, positions.Length).ToList();
        vertexEdges = new List<HashSet<int>>();
        vertexTriangles = new List<HashSet<int>>();
        isBoundary = new bool[positions.Length];

        for (int i = 0; i < positions.Length; i++)
        {
            vertexEdges.Add(new HashSet<int>());
            vertexTriangles.Add(new HashSet<int>());
        }

        // Detect boundary edges and vertices
        DetectBoundaries();
    }

    void DetectBoundaries()
    {
        Logger.Debug("Detecting boundary vertices");

        // Count how many times each edge appears
        var edgeCount = new Dictionary<(int, int), int>();

        for (int i = 0; i < mesh.Triangles.Length; i += 3)
        {
            int[] tri = { mesh.Triangles[i], mesh.Triangles[i + 1], mesh.Triangles[i + 2] };

            for (int j = 0; j < 3; j++)
            {
                int v0 = tri[j], v1 = tri[(j + 1) % 3];
                var key = v0 < v1 ? (v0, v1) : (v1, v0);

                if (!edgeCount.ContainsKey(key))
                    edgeCount[key] = 0;
                edgeCount[key]++;
            }
        }

        // Boundary edges appear only once (not shared by 2 triangles)
        int boundaryEdgeCount = 0;
        foreach (var kvp in edgeCount)
        {
            if (kvp.Value == 1)
            {
                isBoundary[kvp.Key.Item1] = true;
                isBoundary[kvp.Key.Item2] = true;
                boundaryEdgeCount++;
            }
        }

        int boundaryVertCount = isBoundary.Count(b => b);
        Logger.Info($"Found {boundaryVertCount} boundary vertices on {boundaryEdgeCount} boundary edges");
    }

    void BuildQuadrics()
    {
        Logger.Debug("Building quadrics");
        Q = new Quadric[positions.Length];

        for (int i = 0; i < Q.Length; i++)
        {
            Q[i] = new Quadric() { M = Matrix4x4.Identity * 0 };
        }

        for (int triIdx = 0; triIdx < mesh.Triangles.Length; triIdx += 3)
        {
            int a = mesh.Triangles[triIdx];
            int b = mesh.Triangles[triIdx + 1];
            int c = mesh.Triangles[triIdx + 2];

            vertexTriangles[a].Add(triIdx / 3);
            vertexTriangles[b].Add(triIdx / 3);
            vertexTriangles[c].Add(triIdx / 3);

            if (a == b || b == c || a == c) continue;

            var p0 = positions[a];
            var p1 = positions[b];
            var p2 = positions[c];

            var cross = Vector3.Cross(p1 - p0, p2 - p0);
            if (cross.LengthSquared() < 1e-10f) continue;

            var n = Vector3.Normalize(cross);
            float d = -Vector3.Dot(n, p0);
            var q = Quadric.FromPlane(n, d);

            Q[a] = Q[a] + q;
            Q[b] = Q[b] + q;
            Q[c] = Q[c] + q;
        }
    }

    void BuildEdgesAndAdjacency()
    {
        Logger.Debug("Building edges");
        edgeMap = new Dictionary<(int, int), Edge>();

        for (int i = 0; i < mesh.Triangles.Length; i += 3)
        {
            int[] tri = { mesh.Triangles[i], mesh.Triangles[i + 1], mesh.Triangles[i + 2] };

            for (int j = 0; j < 3; j++)
            {
                int v0 = tri[j], v1 = tri[(j + 1) % 3];
                var key = v0 < v1 ? (v0, v1) : (v1, v0);

                if (!edgeMap.ContainsKey(key))
                {
                    var e = new Edge() { V0 = key.Item1, V1 = key.Item2 };
                    e.Optimal = ComputeOptimalPosition(e.V0, e.V1);
                    e.Cost = (Q[e.V0] + Q[e.V1]).Evaluate(e.Optimal);

                    // BOUNDARY PENALTY: Make boundary edges much more expensive to preserve shape
                    if (isBoundary[e.V0] && isBoundary[e.V1])
                    {
                        e.Cost *= 1000.0f; // Collapse boundary edges last
                        Logger.Trace($"Boundary edge ({e.V0}, {e.V1}) cost increased to {e.Cost}");
                    }

                    edgeMap[key] = e;

                    vertexEdges[v0].Add(key.GetHashCode());
                    vertexEdges[v1].Add(key.GetHashCode());
                }
            }
        }
    }

    Vector3 ComputeOptimalPosition(int v0, int v1)
    {
        // Combine quadrics
        var Qsum = Q[v0].M + Q[v1].M;

        // Extract upper-left 3x3 matrix and solve for optimal position
        Matrix4x4 Qmatrix = Qsum;
        float a11 = Qmatrix.M11, a12 = Qmatrix.M12, a13 = Qmatrix.M13;
        float a21 = Qmatrix.M21, a22 = Qmatrix.M22, a23 = Qmatrix.M23;
        float a31 = Qmatrix.M31, a32 = Qmatrix.M32, a33 = Qmatrix.M33;

        float b1 = -Qmatrix.M14;
        float b2 = -Qmatrix.M24;
        float b3 = -Qmatrix.M34;

        float[,] A = new float[3, 4] {
            { a11, a12, a13, b1 },
            { a21, a22, a23, b2 },
            { a31, a32, a33, b3 }
        };

        // Forward elimination with partial pivoting
        for (int col = 0; col < 3; col++)
        {
            int maxRow = col;
            float maxVal = Math.Abs(A[col, col]);
            for (int row = col + 1; row < 3; row++)
            {
                float val = Math.Abs(A[row, col]);
                if (val > maxVal)
                {
                    maxVal = val;
                    maxRow = row;
                }
            }

            if (maxVal < 1e-8f)
            {
                return FindFallbackPosition(v0, v1);
            }

            if (maxRow != col)
            {
                for (int k = 0; k < 4; k++)
                {
                    float tmp = A[col, k];
                    A[col, k] = A[maxRow, k];
                    A[maxRow, k] = tmp;
                }
            }

            for (int row = col + 1; row < 3; row++)
            {
                float factor = A[row, col] / A[col, col];
                for (int k = col; k < 4; k++)
                {
                    A[row, k] -= factor * A[col, k];
                }
            }
        }

        // Back substitution
        float z = A[2, 3] / A[2, 2];
        float y = (A[1, 3] - A[1, 2] * z) / A[1, 1];
        float x = (A[0, 3] - A[0, 2] * z - A[0, 1] * y) / A[0, 0];

        Vector3 optimal = new Vector3(x, y, z);

        // Sanity checks
        Vector3 midpoint = (positions[v0] + positions[v1]) * 0.5f;
        float edgeLength = Vector3.Distance(positions[v0], positions[v1]);
        float distToMid = Vector3.Distance(optimal, midpoint);

        if (float.IsNaN(optimal.X) || float.IsNaN(optimal.Y) || float.IsNaN(optimal.Z) ||
            float.IsInfinity(optimal.X) || float.IsInfinity(optimal.Y) || float.IsInfinity(optimal.Z) ||
            distToMid > edgeLength * 3.0f)
        {
            return FindFallbackPosition(v0, v1);
        }

        return optimal;
    }


    Vector3 FindFallbackPosition(int v0, int v1)
    {
        // Try the three candidates: v0, v1, and midpoint
        // Pick the one with lowest quadric error
        var Qsum = Q[v0] + Q[v1];

        Vector3 p0 = positions[v0];
        Vector3 p1 = positions[v1];
        Vector3 mid = (p0 + p1) * 0.5f;

        float cost0 = Qsum.Evaluate(p0);
        float cost1 = Qsum.Evaluate(p1);
        float costMid = Qsum.Evaluate(mid);

        if (cost0 <= cost1 && cost0 <= costMid)
            return p0;
        else if (cost1 <= costMid)
            return p1;
        else
            return mid;
    }

    void CollapseEdgeFast(Edge edge, MinHeap heap)
    {
        int v0 = edge.V0;
        int v1 = edge.V1;

        // Update position and quadric
        positions[v0] = edge.Optimal;
        Q[v0] = Q[v0] + Q[v1];

        if (colors != null && colors.Length > v0 && colors.Length > v1)
        {
            colors[v0] = BlendColor(colors[v0], colors[v1]);
        }

        // Mark v1 as collapsed
        vertices[v1] = -1;

        // Find all edges connected to v1 and invalidate them
        var v1EdgesSnapshot = vertexEdges[v1].ToList();
        foreach (var edgeHash in v1EdgesSnapshot)
        {
            var edgeToInvalidate = edgeMap.Values.FirstOrDefault(e =>
                e.GetHashCode() == edgeHash ||
                ((e.V0 == v1 || e.V1 == v1) && e.IsValid));

            if (edgeToInvalidate != null)
            {
                edgeToInvalidate.IsValid = false;
            }
        }

        // Update triangles: replace v1 with v0, remove degenerate
        var affectedTriangles = new HashSet<int>(vertexTriangles[v1]);
        affectedTriangles.UnionWith(vertexTriangles[v0]);

        foreach (int triIdx in affectedTriangles)
        {
            int idx = triIdx * 3;
            if (idx >= mesh.Triangles.Length) continue;

            for (int i = 0; i < 3; i++)
            {
                if (mesh.Triangles[idx + i] == v1)
                    mesh.Triangles[idx + i] = v0;
            }
        }

        // Merge vertex adjacency lists
        vertexTriangles[v0].UnionWith(vertexTriangles[v1]);
        vertexTriangles[v1].Clear();

        // Recompute affected edges connected to v0
        var affectedEdgeKeys = new HashSet<(int, int)>();
        foreach (int triIdx in vertexTriangles[v0])
        {
            int idx = triIdx * 3;
            if (idx >= mesh.Triangles.Length) continue;

            int[] tri = { mesh.Triangles[idx], mesh.Triangles[idx + 1], mesh.Triangles[idx + 2] };

            // Skip degenerate triangles
            if (tri[0] == tri[1] || tri[1] == tri[2] || tri[0] == tri[2])
                continue;

            for (int j = 0; j < 3; j++)
            {
                int va = tri[j], vb = tri[(j + 1) % 3];
                if (va == v0 || vb == v0)
                {
                    var key = va < vb ? (va, vb) : (vb, va);
                    affectedEdgeKeys.Add(key);
                }
            }
        }

        // Update or create edges
        foreach (var key in affectedEdgeKeys)
        {
            if (edgeMap.TryGetValue(key, out var existingEdge))
            {
                existingEdge.Optimal = ComputeOptimalPosition(key.Item1, key.Item2);
                existingEdge.Cost = (Q[key.Item1] + Q[key.Item2]).Evaluate(existingEdge.Optimal);

                // Reapply boundary penalty
                if (isBoundary[key.Item1] && isBoundary[key.Item2])
                {
                    existingEdge.Cost *= 1000.0f;
                }

                existingEdge.IsValid = true;

                if (existingEdge.HeapIndex >= 0)
                {
                    heap.UpdateCost(existingEdge, existingEdge.Cost);
                }
                else
                {
                    heap.Add(existingEdge);
                }
            }
            else
            {
                var newEdge = new Edge() { V0 = key.Item1, V1 = key.Item2 };
                newEdge.Optimal = ComputeOptimalPosition(key.Item1, key.Item2);
                newEdge.Cost = (Q[key.Item1] + Q[key.Item2]).Evaluate(newEdge.Optimal);

                // Apply boundary penalty
                if (isBoundary[key.Item1] && isBoundary[key.Item2])
                {
                    newEdge.Cost *= 1000.0f;
                }

                edgeMap[key] = newEdge;
                heap.Add(newEdge);
            }
        }
    }

    void RebuildMesh()
    {
        Logger.Debug("Rebuilding mesh");

        // Create mapping from old to new indices
        var indexMap = new Dictionary<int, int>();
        var newPositions = new List<Vector3>();
        var newColors = new List<System.Drawing.Color>();

        for (int i = 0; i < vertices.Count; i++)
        {
            if (vertices[i] >= 0)
            {
                indexMap[i] = newPositions.Count;
                newPositions.Add(positions[i]);
                if (colors != null && i < colors.Length)
                    newColors.Add(colors[i]);
            }
        }

        // Rebuild triangles
        var newTriangles = new List<int>();
        for (int i = 0; i < mesh.Triangles.Length; i += 3)
        {
            int a = mesh.Triangles[i];
            int b = mesh.Triangles[i + 1];
            int c = mesh.Triangles[i + 2];

            // Skip if any vertex was collapsed
            if (!indexMap.ContainsKey(a) || !indexMap.ContainsKey(b) || !indexMap.ContainsKey(c))
                continue;

            int na = indexMap[a];
            int nb = indexMap[b];
            int nc = indexMap[c];

            // Skip degenerate
            if (na == nb || nb == nc || na == nc)
                continue;

            newTriangles.Add(na);
            newTriangles.Add(nb);
            newTriangles.Add(nc);
        }

        mesh.Vertices = newPositions.ToArray();
        mesh.Triangles = newTriangles.ToArray();
        if (colors != null)
            mesh.Colors = newColors.ToArray();
    }

    System.Drawing.Color BlendColor(System.Drawing.Color a, System.Drawing.Color b)
    {
        return System.Drawing.Color.FromArgb(
            (a.R + b.R) / 2,
            (a.G + b.G) / 2,
            (a.B + b.B) / 2);
    }
}

