using GBX.NET;
using System;
using System.Linq;
using System.Numerics;
using TM_GenericMapping.IO;
using TM_GenericMapping.Common;
using static GBX.NET.Engines.Plug.CPlugCrystal;
using static GBX.NET.Engines.Plug.CPlugSolid2Model;
using Color = System.Drawing.Color;

namespace TM_GenericMapping.Common;

public static class EarClippingTriangulation
{
    public static Int3[] Triangulate(ReadOnlySpan<Vector3> polygon)
    {
        bool ccw = IsCCW(polygon);

        List<int> indices = Enumerable.Range(0, polygon.Length).ToList();
        List<Int3> triangles = new List<Int3>();

        while (indices.Count > 3)
        {
            bool earFound = false;
            for (int i = 0; i < indices.Count; i++)
            {
                int prev = indices[(i - 1 + indices.Count) % indices.Count];
                int curr = indices[i];
                int next = indices[(i + 1) % indices.Count];

                if (IsEar(polygon, prev, curr, next, indices, ccw))
                {
                    triangles.Add(MakeTriangle(polygon, prev, curr, next, ccw));
                    indices.RemoveAt(i);
                    earFound = true;
                    break;
                }
            }
            if (!earFound) throw new Exception("No ear found. Polygon might be invalid or self-intersecting.");
        }
        triangles.Add(MakeTriangle(polygon, indices[0], indices[1], indices[2], ccw));
        return triangles.ToArray();
    }

    static Int3 MakeTriangle(
        ReadOnlySpan<Vector3> p,
        int a, int b, int c,
        bool ccw)
    {
        float z = Vector3.Cross(p[b] - p[a], p[c] - p[a]).Z;
        bool triCCW = z > 0;

        if (triCCW == ccw)
            return new Int3(a, b, c);
        else
            return new Int3(a, c, b);
    }
    private static bool IsEar(
        ReadOnlySpan<Vector3> polygon,
        int prev, int curr, int next,
        List<int> indices,
        bool ccw)
    {
        if (!ShapeUtils.IsConvex(polygon[prev], polygon[curr], polygon[next], ccw))
            return false;

        foreach (int i in indices)
        {
            if (i != prev && i != curr && i != next &&
                IsPointInTriangle(polygon[i],
                                  polygon[prev],
                                  polygon[curr],
                                  polygon[next]))
                return false;
        }
        return true;
    }
    private static bool IsPointInTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 cross1 = Vector3.Cross(b - a, p - a);
        Vector3 cross2 = Vector3.Cross(c - b, p - b);
        Vector3 cross3 = Vector3.Cross(a - c, p - c);

        bool hasNeg = (cross1.Z < 0) || (cross2.Z < 0) || (cross3.Z < 0);
        bool hasPos = (cross1.Z > 0) || (cross2.Z > 0) || (cross3.Z > 0);

        return !(hasNeg && hasPos);
    }

    static bool IsCCW(ReadOnlySpan<Vector3> p)
    {
        float area = 0;
        for (int i = 0; i < p.Length; i++)
        {
            var a = p[i];
            var b = p[(i + 1) % p.Length];
            area += (b.X - a.X) * (b.Y + a.Y);
        }
        return area < 0;
    }
}

public static class ConvexHull3D
{
    private class Face
    {
        public int A, B, C;
        public Vector3 Normal;

        public Face(int a, int b, int c, ReadOnlySpan<Vector3> pts)
        {
            A = a; B = b; C = c;
            ComputeNormal(pts);
        }

        public void ComputeNormal(ReadOnlySpan<Vector3> pts)
        {
            var u = pts[B] - pts[A];
            var v = pts[C] - pts[A];
            Normal = Vector3.Normalize(Vector3.Cross(u, v));
        }

        public bool IsVisible(Vector3 p, ReadOnlySpan<Vector3> pts)
        {
            return Vector3.Dot(Normal, p - pts[A]) > 1e-6f;
        }

        public Vector3 Centroid(ReadOnlySpan<Vector3> pts)
        {
            return (pts[A] + pts[B] + pts[C]) / 3f;
        }
    }

    public static Int3[] Triangulate(ReadOnlySpan<Vector3> points)
    {
        if (points.Length < 4)
            throw new ArgumentException("Need at least 4 non-coplanar points.");

        // 1. Initial tetrahedron
        var (i0, i1, i2, i3) = FindInitialTetrahedron(points);
        var faces = new List<Face>
        {
            new Face(i0,i1,i2,points),
            new Face(i0,i3,i1,points),
            new Face(i0,i2,i3,points),
            new Face(i1,i3,i2,points)
        };

        // Ensure outward normals for initial tetrahedron
        var centroid = Vector3.Zero;
        foreach (var f in faces) centroid += f.Centroid(points);
        centroid /= faces.Count;
        for (int i = 0; i < faces.Count; i++)
        {
            Vector3 dir = faces[i].Centroid(points) - centroid;
            if (Vector3.Dot(faces[i].Normal, dir) < 0)
                faces[i] = new Face(faces[i].B, faces[i].A, faces[i].C, points);
        }

        // 2. Add remaining points
        for (int i = 0; i < points.Length; i++)
        {
            if (i == i0 || i == i1 || i == i2 || i == i3) continue;
            AddPointToHull(i, points, faces);
        }

        // 3. Output triangles
        var result = new Int3[faces.Count];
        for (int i = 0; i < faces.Count; i++)
            result[i] = new Int3(faces[i].A, faces[i].B, faces[i].C);

        return result;
    }

    private static void AddPointToHull(int p, ReadOnlySpan<Vector3> pts, List<Face> faces)
    {
        var visible = new List<Face>();
        foreach (var f in faces)
            if (f.IsVisible(pts[p], pts)) visible.Add(f);

        if (visible.Count == 0) return;

        // Current hull centroid
        var centroid = Vector3.Zero;
        foreach (var f in faces) centroid += f.Centroid(pts);
        centroid /= faces.Count;

        // Build horizon edges (edges with exactly one visible face)
        var edgeCount = new Dictionary<(int, int), int>();
        foreach (var f in visible)
        {
            AddEdge(edgeCount, f.A, f.B);
            AddEdge(edgeCount, f.B, f.C);
            AddEdge(edgeCount, f.C, f.A);
        }

        // Remove visible faces
        faces.RemoveAll(f => visible.Contains(f));

        // Add new faces along horizon
        foreach (var kvp in edgeCount)
        {
            if (kvp.Value != 1) continue;
            int a = kvp.Key.Item1;
            int b = kvp.Key.Item2;
            var newFace = new Face(a, b, p, pts);
            Vector3 dir = newFace.Centroid(pts) - centroid;
            if (Vector3.Dot(newFace.Normal, dir) < 0)
                newFace = new Face(b, a, p, pts);
            faces.Add(newFace);
        }
    }

    private static void AddEdge(Dictionary<(int, int), int> dict, int a, int b)
    {
        var key = a < b ? (a, b) : (b, a);
        if (dict.ContainsKey(key)) dict[key]++;
        else dict[key] = 1;
    }

    private static (int, int, int, int) FindInitialTetrahedron(ReadOnlySpan<Vector3> pts)
    {
        int i0 = 0;
        int i1 = Farthest(i0, pts);
        int i2 = FarthestFromLine(i0, i1, pts);
        int i3 = FarthestFromPlane(i0, i1, i2, pts);
        return (i0, i1, i2, i3);
    }

    private static int Farthest(int i0, ReadOnlySpan<Vector3> pts)
    {
        float max = 0; int best = 0;
        for (int i = 0; i < pts.Length; i++)
        {
            float d = Vector3.DistanceSquared(pts[i0], pts[i]);
            if (d > max) { max = d; best = i; }
        }
        return best;
    }

    private static int FarthestFromLine(int i0, int i1, ReadOnlySpan<Vector3> pts)
    {
        float max = 0; int best = 0;
        var dir = Vector3.Normalize(pts[i1] - pts[i0]);
        for (int i = 0; i < pts.Length; i++)
        {
            var toP = pts[i] - pts[i0];
            float dist = Vector3.Cross(dir, toP).LengthSquared();
            if (dist > max) { max = dist; best = i; }
        }
        return best;
    }

    private static int FarthestFromPlane(int i0, int i1, int i2, ReadOnlySpan<Vector3> pts)
    {
        float max = 0; int best = 0;
        var n = Vector3.Normalize(Vector3.Cross(pts[i1] - pts[i0], pts[i2] - pts[i0]));
        for (int i = 0; i < pts.Length; i++)
        {
            float dist = MathF.Abs(Vector3.Dot(pts[i] - pts[i0], n));
            if (dist > max) { max = dist; best = i; }
        }
        return best;
    }
}



public static class ShapeUtils
{
    public static Vector3[] CreateRegularPolygonPoints(int corners, float size = 1)
    {
        Vector3[] points = new Vector3[corners];
        float angleStep = 2f * MathF.PI / corners;

        for (int i = 0; i < corners; i++)
        {
            float angle = angleStep * i - MathF.PI / 2; // start at top
            points[i] = new Vector3(MathF.Cos(angle) * size, MathF.Sin(angle) * size, 0);
        }

        return points;
    }

    public static bool IsConvex(Vector3 a, Vector3 b, Vector3 c, bool ccw)
    {
        float z = Vector3.Cross(b - a, c - b).Z;
        return ccw ? z > 0 : z < 0;
    }
    public static Vector3 CalculateTangent(Vector3 currentPoint, Vector3 previousPoint, Vector3 nextPoint)
    {
        Vector3 previousToCurrent = currentPoint - previousPoint;
        Vector3 currentToNext = nextPoint - currentPoint;

        // Normalize the vectors (this ensures that the direction is preserved, regardless of distance)
        Vector3 normalizedToPrevious = Vector3.Normalize(previousToCurrent);
        Vector3 normalizedToNext = Vector3.Normalize(currentToNext);

        // Calculate the tangent as the average of both normalized vectors
        Vector3 tangent = normalizedToPrevious + normalizedToNext;

        // Normalize the resulting tangent to ensure it's a unit vector
        return Vector3.Normalize(tangent);
    }
    public static Vector3 CalculateTangent(ReadOnlySpan<Vector3> points, int pointIndex)
    {
        if (points.Length< 3)
            throw new ArgumentException("Must have at least 3 points.");

        // Handle wrap-around for the first and last points (treat polygon as closed)
        int prevIndex = (pointIndex - 1 + points.Length) % points.Length;
        int nextIndex = (pointIndex + 1) % points.Length;

        return CalculateTangent(points[pointIndex], points[prevIndex], points[nextIndex]);
    }
    public static Vector3 CalculatePolygonNormal(params ReadOnlySpan<Vector3> points)
    {
        if (points.Length < 3)
            throw new ArgumentException("A polygon must have at least 3 points.");

        // Use the first three points to calculate the polygon's normal
        Vector3 edge1 = points[1] - points[0];
        Vector3 edge2 = points[2] - points[0];

        // The normal is the cross product of the two edges
        Vector3 normal = Vector3.Cross(edge1, edge2);

        // Since the polygon is assumed to be flat in the XY plane, the normal will have a Z-component.
        return Vector3.Normalize(normal);
    }

    public static void MakeVerticesUnique(ReadOnlySpan<Vector3> vertices, ReadOnlySpan<Int3> triangles, out List<Vector3> newVertices, out List<Int3> newTriangles)
    {
        newVertices = new List<Vector3>();
        newTriangles = new List<Int3>();

        for (int i = 0; i < triangles.Length; i++)
        {
            int a = newVertices.Count;
            int b = newVertices.Count + 1;
            int c = newVertices.Count + 2;

            newVertices.Add(vertices[triangles[i].X]);
            newVertices.Add(vertices[triangles[i].Y]);
            newVertices.Add(vertices[triangles[i].Z]);

            newTriangles.Add(new Int3(a, b, c));
        }
    }
    public static void MakeVerticesUnique(ReadOnlySpan<Vector3> vertices, ReadOnlySpan<Int3> triangles, ReadOnlySpan<Vector4> colors, out List<Vector3> newVertices, out List<Int3> newTriangles, out List<Vector4> newColors)
    {
        newVertices = new List<Vector3>();
        newTriangles = new List<Int3>();
        newColors = new List<Vector4>();

        for (int i = 0; i < triangles.Length; i++)
        {
            int a = newVertices.Count;
            int b = newVertices.Count + 1;
            int c = newVertices.Count + 2;

            newVertices.Add(vertices[triangles[i].X]);
            newVertices.Add(vertices[triangles[i].Y]);
            newVertices.Add(vertices[triangles[i].Z]);

            newColors.Add(colors[triangles[i].X]);
            newColors.Add(colors[triangles[i].Y]);
            newColors.Add(colors[triangles[i].Z]);

            newTriangles.Add(new Int3(a, b, c));
        }
    }

    public static void GeneratePolygonOutline(ReadOnlySpan<Vector3> points, float width, OutlineExtendsDirection outlineExtends, out List<Vector3> outlineVertices, out List<Int3> outlineTriangles)
    {
        ExceptionUtils.Ensure(points.Length >= 2, () => new ArgumentException("Polygon must have at least 2 vertices"));
        outlineVertices = new List<Vector3>();
        outlineTriangles = new List<Int3>();

        // The first step is to add the top-left and top-right corners for the first rectangle
        // Bottom left and bottom right vertices will be aligned later
        Vector3 lastBottomLeft = Vector3.Zero;
        Vector3 lastBottomRight = Vector3.Zero;

        var innerVertices = new Vector3[points.Length];
        var outerVertices = points.ToArray();
        for (int i = 0; i < points.Length; i++)
        {
            // Get top-left and top-right points (top corners of the rectangle)
            Vector3 previousVertex = points[i == 0 ? points.Length - 1 : i - 1];
            Vector3 currentVertex = points[i];
            Vector3 nextVertex = points[(i + 1) % points.Length];

            var tangent = CalculateTangent(currentVertex, previousVertex, nextVertex);
            Vector3 planeNormal = ComputePlaneNormal(points);
            Vector3 perpendicular = Vector3.Normalize(
                Vector3.Cross(planeNormal, tangent));

            switch (outlineExtends)
            {
                case OutlineExtendsDirection.Inwards:
                    innerVertices[i] = currentVertex + perpendicular * width;
                    outerVertices[i] = currentVertex;
                    break;
                case OutlineExtendsDirection.Outwards:
                    innerVertices[i] = currentVertex;
                    outerVertices[i] = currentVertex - perpendicular * width;
                    break;
                case OutlineExtendsDirection.Bidirectional:
                    innerVertices[i] = currentVertex + perpendicular * width / 2f;
                    outerVertices[i] = currentVertex - perpendicular * width / 2f;
                    break;

            }
        }
        for (int i = 0; i < points.Length; ++i)
        {
            outlineVertices.Add(outerVertices[i]);
            outlineVertices.Add(innerVertices[i]);

            int currentPointIdx = i * 2;
            int currentInnerVertexIdx = (i * 2) + 1;
            int nextPointIdx = (currentPointIdx + 2) % (outerVertices.Length * 2);
            int nextInnerVertexIdx = (nextPointIdx + 1) % (outerVertices.Length * 2);

            outlineTriangles.Add(new Int3(currentPointIdx, nextPointIdx, currentInnerVertexIdx));
            outlineTriangles.Add(new Int3(nextPointIdx, nextInnerVertexIdx, currentInnerVertexIdx));
        }
    }

    public static void GenerateClosedPolygonOutline(
      ReadOnlySpan<Vector3> points,
      float width,
      OutlineExtendsDirection outlineExtends,
      out List<Vector3> outlineVertices,
      out List<Int3> outlineTriangles)
    {
        ExceptionUtils.Ensure(points.Length >= 2,
            () => new ArgumentException("Polygon must have at least 2 vertices"));

        outlineVertices = new List<Vector3>();
        outlineTriangles = new List<Int3>();

        Vector3 planeNormal = ComputePlaneNormal(points);

        var innerVertices = new Vector3[points.Length];
        var outerVertices = points.ToArray();

        for (int i = 0; i < points.Length; i++)
        {
            Vector3 prev = points[i == 0 ? points.Length - 1 : i - 1];
            Vector3 curr = points[i];
            Vector3 next = points[(i + 1) % points.Length];

            Vector3 dirA = Vector3.Normalize(curr - prev);
            Vector3 dirB = Vector3.Normalize(next - curr);

            // Perpendiculars inside plane
            Vector3 normalA = Vector3.Normalize(Vector3.Cross(planeNormal, dirA));
            Vector3 normalB = Vector3.Normalize(Vector3.Cross(planeNormal, dirB));

            // Miter
            Vector3 miter = Vector3.Normalize(normalA + normalB);

            float dot = Vector3.Dot(miter, normalA);
            float miterLength = width / MathF.Max(dot, 0.000001f);

            Vector3 offset = miter * miterLength;

            switch (outlineExtends)
            {
                case OutlineExtendsDirection.Inwards:
                    innerVertices[i] = curr + offset;
                    outerVertices[i] = curr;
                    break;

                case OutlineExtendsDirection.Outwards:
                    innerVertices[i] = curr;
                    outerVertices[i] = curr - offset;
                    break;

                case OutlineExtendsDirection.Bidirectional:
                    innerVertices[i] = curr + offset * 0.5f;
                    outerVertices[i] = curr - offset * 0.5f;
                    break;
            }
        }

        for (int i = 0; i < points.Length; ++i)
        {
            outlineVertices.Add(outerVertices[i]);
            outlineVertices.Add(innerVertices[i]);

            int current = i * 2;
            int next = (current + 2) % (points.Length * 2);

            outlineTriangles.Add(new Int3(current, next, current + 1));
            outlineTriangles.Add(new Int3(next, next + 1, current + 1));
        }
    }

    public static void GeneratePolyLineOutline(ReadOnlySpan<Vector3> points,
        Vector3 startBoundaryPoint,
        Vector3 endBoundaryPoint,
        float width,
        OutlineExtendsDirection outlineExtends, 
        out List<Vector3> outlineVertices, 
        out List<Int3> outlineTriangles)
    {
        ExceptionUtils.Ensure(points.Length >= 2, () => new ArgumentException("PolyLine must have at least 2 points"));
        var pointsWithBoundaries = points.ToArray().ToList();

        pointsWithBoundaries.Add(endBoundaryPoint);
        pointsWithBoundaries.Add(startBoundaryPoint);

        GenerateClosedPolygonOutline(pointsWithBoundaries.ToArray(), width, outlineExtends, out outlineVertices, out outlineTriangles);

        // remove start/end boundary and inner vertex
        outlineVertices.RemoveRange(outlineVertices.Count - 4, 4);

        // remove rectangle between boundaryEnd and boundaryStart
        outlineTriangles.RemoveRange(outlineTriangles.Count - 6, 6);
    }

    public static void GenerateClosedPolyLineOutline(ReadOnlySpan<Vector3> points,
      float width,
      OutlineExtendsDirection outlineExtends,
      out List<Vector3> outlineVertices,
      out List<Int3> outlineTriangles)
    {
        ExceptionUtils.Ensure(points.Length >= 2, () => new ArgumentException("PolyLine must have at least 2 points"));
        var pointsWithBoundaries = points.ToArray().ToList();


        GenerateClosedPolygonOutline(pointsWithBoundaries.ToArray(), width, outlineExtends, out outlineVertices, out outlineTriangles);

        //// remove start/end boundary and inner vertex
        //outlineVertices.RemoveRange(outlineVertices.Count - 4, 4);

        //// remove rectangle between boundaryEnd and boundaryStart
        //outlineTriangles.RemoveRange(outlineTriangles.Count - 6, 6);
    }

    static Vector3 ComputePlaneNormal(ReadOnlySpan<Vector3> pts)
    {
        for (int i = 2; i < pts.Length; i++)
        {
            var n = Vector3.Cross(pts[1] - pts[0], pts[i] - pts[0]);
            if (n.LengthSquared() > 1e-6f)
                return Vector3.Normalize(n);
        }
        return Vector3.UnitZ; // fallback if degenerate
    }
    public static Vector3 GetCentroid(TriangleObject obj)
        => GetCentroid(obj.Vertices.Take(obj.FillVertexCount).ToArray());
    public static Vector3 GetCentroid(ReadOnlySpan<Vector3> vertices)
    {
        ExceptionUtils.Ensure(vertices.Length > 0, () => new ArgumentException("Must have at least one vertex."));

        Vector3 sum = Vector3.Zero;
        foreach (var v in vertices)
            sum += v;

        return sum / vertices.Length; // Arithmetic mean
    }
    public static Vector3 GetWeightedCenter(TriangleObject obj)
        => GetWeightedCenter(obj.Vertices.Take(obj.FillVertexCount).ToArray(), obj.Triangles.Take(obj.FillTrianglesCount).ToArray());
    public static Vector3 GetWeightedCenter(ReadOnlySpan<Vector3> vertices, ReadOnlySpan<Int3> triangles)
    {
        ExceptionUtils.Ensure(vertices.Length > 0, () => new ArgumentException("Must have at least one vertex."));
        ExceptionUtils.Ensure(triangles.Length > 0, () => new ArgumentException("Must have at least one triangle."));

        Vector3 center = Vector3.Zero;
        float totalArea = 0;

        foreach (var tri in triangles)
        {
            Vector3 a = vertices[tri.X];
            Vector3 b = vertices[tri.Y];
            Vector3 c = vertices[tri.Z];

            // Triangle centroid
            Vector3 triCenter = (a + b + c) / 3f;

            // Triangle area (using cross product)
            float area = Vector3.Cross(b - a, c - a).Length() / 2f;

            center += triCenter * area;
            totalArea += area;
        }

        return totalArea > 0 ? center / totalArea : GetCentroid(vertices);
    }

    public static Vector3 GetBoundingBoxCenter(TriangleObject obj)
       => GetBoundingBoxCenter(obj.Vertices.Take(obj.FillVertexCount).ToArray());
    public static Vector3 GetBoundingBoxCenter(ReadOnlySpan<Vector3> vertices)
    {
        ExceptionUtils.Ensure(vertices.Length > 0, () => new ArgumentException("Must have at least one vertex."));

        Vector3 min = vertices[0];
        Vector3 max = vertices[0];

        foreach (var v in vertices)
        {
            min = Vector3.Min(min, v);
            max = Vector3.Max(max, v);
        }

        return (min + max) / 2f;
    }

    public static void RandomizeTriangleColors(TriangleObject obj, bool fillTriangles = true, bool outlineTriangles = false, bool distinctColors = true, int distinctValuesByChannel = 8)
    {
        ExceptionUtils.Ensure(fillTriangles || outlineTriangles, () => new ArgumentNullException(""));

        int startIdx = fillTriangles ? 0 : obj.FillTrianglesCount;
        int count = fillTriangles
            ? (outlineTriangles ? obj.Triangles.Length : obj.FillTrianglesCount)
            : obj.Triangles.Length - obj.FillTrianglesCount;
        foreach(var t in obj.Triangles.Skip(startIdx).Take(count))
        {
            Color newColor = distinctColors ? RandomUtils.RandomDistinctColor(distinctValuesByChannel) : RandomUtils.RandomColor();
            obj.Colors[t.X] = newColor.ToVector4();
            obj.Colors[t.Y] = newColor.ToVector4();
            obj.Colors[t.Z] = newColor.ToVector4();
        }
    }

    public static TriangleObject InverseWindingOrder(TriangleObject obj)
    {
        var clone = obj.Clone();
        for (int i = 0; i < obj.Triangles.Length; ++i)
        {
            clone.Triangles[i] = (obj.Triangles[i].X, obj.Triangles[i].Z, obj.Triangles[i].Y);
        }
        return clone;
    }
    public static float Distance(Bounds a, Bounds b)
    {
        float dx = MathF.Max(b.Min.X - a.Max.X, a.Min.X - b.Max.X);
        float dy = MathF.Max(b.Min.Y - a.Max.Y, a.Min.Y - b.Max.Y);
        float dz = MathF.Max(b.Min.Z - a.Max.Z, a.Min.Z - b.Max.Z);

        float max = MathF.Max(dx, MathF.Max(dy, dz));
        return max; // Negative means overlap
    }
    public static float Distance(Bounds a, Vector3 p)
    {
        var min = a.Min;
        var max = a.Max;

        float dx = MathF.Max(min.X - p.X, p.X - max.X);
        float dy = MathF.Max(min.Y - p.Y, p.Y - max.Y);
        float dz = MathF.Max(min.Z - p.Z, p.Z - max.Z);

        return MathF.Max(dx, MathF.Max(dy, dz));
    }
    public static Vector3 ClosesPoint(Bounds a, Vector3 p)
    {
        var min = a.Min;
        var max = a.Max;

        return new Vector3(
            Math.Clamp(p.X, min.X, max.X),
            Math.Clamp(p.Y, min.Y, max.Y),
            Math.Clamp(p.Z, min.Z, max.Z));
    }
    public static Bounds GetAABB(TriangleObject obj)
    {
        ExceptionUtils.Ensure(obj.Vertices.Length > 0, () => new ArgumentException("Must have at least one vertex."));
        Vector3 transformed = Vector3.Transform(obj.Vertices[0], obj.LocalToWorldTRS);
        Vector3 min = transformed;
        Vector3 max = transformed;
        foreach (var v in obj.Vertices)
        {
            transformed = Vector3.Transform(v, obj.LocalToWorldTRS);
            min = Vector3.Min(min, transformed);
            max = Vector3.Max(max, transformed);
        }
        var bounds = new Bounds
        {
            Center = (min + max) / 2f,
            Size = (max - min)
        };

        return bounds;
    }
    public static Bounds CombineAABB(params ReadOnlySpan<Bounds> bounds)
    {
        ExceptionUtils.Ensure(bounds.Length > 0, () => new ArgumentException("Must have at least one bounds."));
        var aabb = bounds[0];
        foreach(var b in bounds)
        {
            Vector3 min = new Vector3(
                MathF.Min(aabb.Min.X, b.Min.X),
                MathF.Min(aabb.Min.Y, b.Min.Y),
                MathF.Min(aabb.Min.Z, b.Min.Z));

            Vector3 max = new Vector3(
                MathF.Max(aabb.Max.X, b.Max.X),
                MathF.Max(aabb.Max.Y, b.Max.Y),
                MathF.Max(aabb.Max.Z, b.Max.Z)
            );

            Vector3 size = max - min;
            Vector3 center = (min + max) * 0.5f;
            aabb = new Bounds { Center = center, Size = size };
        }
        return aabb;
    }
    public static Bounds GetHierarchyAABB(TriangleObject obj)
    {
        return CombineAABB(GetFlattenedHierarchyObjects(obj).OfType<TriangleObject>().Where(o => o.Vertices.Length > 0).Select(o => o.GetAABB()).ToArray());
    }


    public static Vector4[] GenerateVertexVisualizationColors(ReadOnlySpan<Vector3> vertices, ReadOnlySpan<Int3> triangles, int distinctColors = 12, bool uniqueVertices = false)
    {
        var colors = new Vector4[vertices.Length];

        float hueStep = 1f / distinctColors;

        if (uniqueVertices)
        {
            for (int i = 0; i < triangles.Length; ++i)
            {
                float hue = (i % distinctColors) / (float)distinctColors;
                var color = ColorUtils.HSVToRGB(new Vector4(hue, 1f, 1f, 1f)).ToVector4();
                colors[triangles[i].X] = color;
                colors[triangles[i].Y] = color;
                colors[triangles[i].Z] = color;
            }
        }
        else
        {
            // build adjacency
            List<int>[] adjacency = new List<int>[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
                adjacency[i] = new List<int>();

            for (int t = 0; t < triangles.Length; t++)
            {
                int a = triangles[t].X;
                int b = triangles[t].Y;
                int c = triangles[t].Z;

                // Add neighbors for edge AB
                if (!adjacency[a].Contains(b)) adjacency[a].Add(b);
                if (!adjacency[b].Contains(a)) adjacency[b].Add(a);

                // Add neighbors for edge BC
                if (!adjacency[b].Contains(c)) adjacency[b].Add(c);
                if (!adjacency[c].Contains(b)) adjacency[c].Add(b);

                // Add neighbors for edge CA
                if (!adjacency[c].Contains(a)) adjacency[c].Add(a);
                if (!adjacency[a].Contains(c)) adjacency[a].Add(c);
            }

            // 2. Assign colors using greedy coloring in HSV space

            int[] hueIndices = new int[vertices.Length];
            for (int i = 0; i < vertices.Length; i++) hueIndices[i] = -1;
            for (int i = 0; i < vertices.Length; i++)
            {
                // track neighbor hues
                List<float> neighborHues = new List<float>();
                foreach (var n in adjacency[i])
                    if (hueIndices[n] != -1)
                        neighborHues.Add(hueIndices[n] / (float)distinctColors);

                int chosen = 0;
                float bestDistance = -1f;

                // pick the hue index that maximizes minimal distance to neighbors
                for (int c = 0; c < distinctColors; c++)
                {
                    float candidateHue = c / (float)distinctColors;
                    float minDist = 1f; // max circular distance is 0.5, so initialize large
                    foreach (var nh in neighborHues)
                    {
                        float dist = MathF.Abs(candidateHue - nh);
                        dist = MathF.Min(dist, 1 - dist); // circular distance
                        if (dist < minDist) minDist = dist;
                    }

                    if (minDist > bestDistance)
                    {
                        bestDistance = minDist;
                        chosen = c;
                    }
                }

                hueIndices[i] = chosen;

                // convert to hue in 0..1
                float hue = chosen / (float)distinctColors;

                // optional: add tiny jitter
                hue += (Random.Shared.NextSingle() - 0.5f) * (1f / distinctColors * 0.3f);
                hue = (hue + 1f) % 1f; // wrap around

                colors[i] = ColorUtils.HSVToRGB(new Vector4(hue, 1f, 1f, 1f)).ToVector4();
            }

        }
        return colors;
    }

    public static void GenerateSphereShape(float radius, Int2 resolution, out List<Vector3> vertices, out List<Int3> triangles)
    {
        int lat = Math.Max(2, resolution.X); // latitude (θ)
        int lon = Math.Max(3, resolution.Y); // longitude (φ)
        vertices = new List<Vector3>((lat + 1) * lon);
        triangles = new List<Int3>();

        // vertices
        for (int v = 0; v <= lat; v++)
        {
            float θ = MathF.PI * v / lat; // 0..π
            float sinθ = MathF.Sin(θ);
            float cosθ = MathF.Cos(θ);

            for (int u = 0; u < lon; u++)
            {
                float φ = 2f * MathF.PI * u / lon; // 0..2π
                float sinφ = MathF.Sin(φ);
                float cosφ = MathF.Cos(φ);

                int i = v * lon + u;
                vertices.Add(new Vector3(
                    radius * sinθ * cosφ,
                    radius * cosθ,
                    radius * sinθ * sinφ));
            }
        }

        // --- Triangles ---
        for (int v = 0; v < lat; v++)
        {
            for (int u = 0; u < lon; u++)
            {
                int uNext = (u + 1) % lon;
                int i0 = v * lon + u;
                int i1 = v * lon + uNext;
                int i2 = (v + 1) * lon + u;
                int i3 = (v + 1) * lon + uNext;

                // Two triangles per quad
                triangles.Add(new Int3(i0, i2, i1));
                triangles.Add(new Int3(i1, i2, i3));
            }
        }
    }


    public static void Extrude(ReadOnlySpan<Vector3> vertices, ReadOnlySpan<Int3> triangles, ReadOnlySpan<Vector4> colors, Vector3 direction, out List<Vector3> outVertices, out List<Int3> outTriangles, out List<Vector4> outColors)
    {
        bool ccw = SignedAreaXY(vertices) > 0;

        int vCount = vertices.Length;
        outVertices = new List<Vector3>(vCount * 2);
        outTriangles = new List<Int3>(triangles.Length * 2 + vCount * 2);
        outColors = new List<Vector4>(vCount * 2);


        // bottom and top vertices
        for (int i = 0; i < vCount; i++)
        {
            outVertices.Add(vertices[i]); // original
            outColors.Add(colors[i]);
        }
        for (int i = 0; i < vCount; i++)
        {
            outVertices.Add(vertices[i] + direction); // extruded
            outColors.Add(colors[i]);
        }

        // bottom faces (same order)
        foreach (var tri in triangles)
            outTriangles.Add(new Int3(tri.X, tri.Y, tri.Z));

        foreach (var tri in triangles)
            outTriangles.Add(new Int3(tri.X + vCount, tri.Z + vCount, tri.Y + vCount));

        // side faces
        for (int i = 0; i < vCount; i++)
        {
            int next = (i + 1) % vCount;
            int a = i;
            int b = next;
            int c = i + vCount;
            int d = next + vCount;

            // two triangles per side quad
            if (ccw)
            {
                // outward normals
                outTriangles.Add(new Int3(a, b, d));
                outTriangles.Add(new Int3(a, d, c));
            }
            else
            {
                outTriangles.Add(new Int3(a, d, b));
                outTriangles.Add(new Int3(a, c, d));
            }
        }

    }

    public static void Extrude(ReadOnlySpan<Vector3> vertices, ReadOnlySpan<Int3> triangles, Vector3 extrusion, out List<Vector3> outVertices, out List<Int3> outTriangles)
        => Extrude(vertices, triangles, Enumerable.Repeat(Color.Black.ToVector4(), vertices.Length).ToArray(), extrusion, out outVertices, out outTriangles, out _);

    static float SignedAreaXY(ReadOnlySpan<Vector3> v)
    {
        float area = 0;
        for (int i = 0; i < v.Length; i++)
        {
            var a = v[i];
            var b = v[(i + 1) % v.Length];
            area += (b.X - a.X) * (b.Y + a.Y);
        }
        return area * 0.5f;
    }

    public static Vector3[] Translate(Vector3 translation, ReadOnlySpan<Vector3> points)
    {
        Vector3[] result = new Vector3[points.Length];
        for (int i = 0; i < result.Length; ++i)
        {
            result[i] = points[i] + translation;
        }
        return result;
    }

    public static Vector3[] CreateCuboidShapePoints(Vector3 size)
    {
        return [
            new Vector3(-size.X / 2f, -size.Y / 2f, -size.Z / 2f),
            new Vector3(size.X / 2f, -size.Y / 2f, -size.Z / 2f),
            new Vector3(size.X / 2f, size.Y / 2f, -size.Z / 2f),
            new Vector3(-size.X / 2f, size.Y / 2f, -size.Z / 2f),
            new Vector3(-size.X / 2f, -size.Y / 2f, size.Z / 2f),
            new Vector3(size.X / 2f, -size.Y / 2f, size.Z / 2f),
            new Vector3(size.X / 2f, size.Y / 2f, size.Z / 2f),
            new Vector3(-size.X / 2f, size.Y / 2f, size.Z / 2f)
            ];
    }

    public static TriangleObject FlattenHierarchy(TriangleObject obj)
    {
        return Merge(false, GetFlattenedHierarchyObjects(obj).OfType<TriangleObject>().Where(o => o.FillVertexCount > 0));
    }

    public static IEnumerable<MediaObject> GetFlattenedHierarchyObjects(MediaObject obj)
    {
        return [obj, .. obj.SubObjects.SelectMany(GetFlattenedHierarchyObjects)];
    }

    public static TriangleObject Merge(bool keepWorldPosition, params IEnumerable<TriangleObject> objs)
    {
        List<Vector3> fillVertices = [];
        List<Int3> fillTriangles = [];
        List<Vector4> fillColors = [];


        int triangleOffset = 0;
        foreach (var objectInHierarchy in objs)
        {
            fillVertices.AddRange(objectInHierarchy.Vertices.Take(objectInHierarchy.FillVertexCount).Select(v => Vector3.Transform(v, objectInHierarchy.LocalToWorldTRS)));
            fillTriangles.AddRange(objectInHierarchy.Triangles.Take(objectInHierarchy.FillTrianglesCount).Select(t => new Int3(triangleOffset + t.X, triangleOffset + t.Y, triangleOffset + t.Z)));
            fillColors.AddRange(objectInHierarchy.Colors.Take(objectInHierarchy.FillVertexCount));
            triangleOffset = fillTriangles.Count;
        }

        List<Vector3> edgeVertices = [];
        List<Int3> edgeTriangles = [];
        List<Vector4> edgeColors = [];

        int fillVerticesCount = fillVertices.Count;
        int fillTrianglesCount = fillTriangles.Count;
        foreach (var objectInHierarchy in objs)
        {
            fillVertices.AddRange(objectInHierarchy.Vertices.Skip(objectInHierarchy.FillVertexCount).Select(v => Vector3.Transform(v, objectInHierarchy.LocalToWorldTRS)));
            fillTriangles.AddRange(objectInHierarchy.Triangles.Skip(objectInHierarchy.FillTrianglesCount).Select(t => new Int3(triangleOffset + t.X, triangleOffset + t.Y, triangleOffset + t.Z)));
            fillColors.AddRange(objectInHierarchy.Colors.Skip(objectInHierarchy.FillVertexCount));
            triangleOffset = fillTriangles.Count;
        }
        return new TriangleObject(points: fillVertices.ToArray(),
            triangles: fillTriangles.ToArray(),
            colors: fillColors.Select(c => c.ToColor()).ToArray(),
            uniqueVertices: !objs.Any(fo => !fo.HasUniqueVertices))
        {
            FillVertexCount = fillVerticesCount,
            FillTrianglesCount = fillTrianglesCount,
            HasOutline = fillVerticesCount != fillVertices.Count
        };
    }

    public static TriangleObject CreateSquareWithHole(Circle circle, Square square)
    {
        ExceptionUtils.Ensure(circle.Vertices.Length % 4 == 0, () => new ArgumentException("Hole must have %4 vertices"));
        ExceptionUtils.Ensure(circle.Radius < square.Height / 2f, () => new ArgumentException($"Circle does not fit inside square: {circle.Radius * 2f} diameter > {square.Height} size"));

        List<Vector3> vertices = 
            [
            ..square.Vertices,
            ..circle.Vertices,
            ];
        List<Int3> triangles = [];
        List<Vector4> colors = 
            [
            ..square.Colors,
            ..circle.Colors,
            ];



        int lastClosestOtherSquareIdx = -1;
        int lastClosestSquareIdx = -1;
        int lastVertexIdx = -1;
        for (int i = 4; i < vertices.Count; ++i)
        {
            int closestOtherSquareIdx = -1;
            int closestSquareIdx = 0;
            float closestDistance = float.MaxValue;
            var circleVertPos = vertices[i];
            for (int j = 0; j < 4; ++j)
            {
                var squareVertPos = vertices[j];
                var dist = Vector3.Distance(circleVertPos, squareVertPos);
                if (float.NearlyEqual(dist, closestDistance, 0.00001f))
                {
                    closestOtherSquareIdx = j;
                }
                else if (dist < closestDistance)
                {
                    closestSquareIdx = j;
                    closestDistance = dist;
                    closestOtherSquareIdx = -1;
                }
            }


            if (closestOtherSquareIdx >= 0) // exact on center between 2 corner distances, create triangle with only 1 vertex on circle
            {
                triangles.Add((i, closestSquareIdx, closestOtherSquareIdx));
            }

            if (lastVertexIdx < 0) // init
            {
                lastVertexIdx = i;
                lastClosestSquareIdx = closestSquareIdx;
                lastClosestOtherSquareIdx = closestOtherSquareIdx;
                continue;
            }

            if(lastClosestOtherSquareIdx != -1)
            {
                // last vertex had 2 closest: select best
                if (closestOtherSquareIdx != -1)
                    throw new InvalidOperationException(); // should not happen if circle has > 4 vetices

                triangles.Add((i, lastVertexIdx, closestSquareIdx));
            }
            else
            {
                // create triangle between this and last vertex - last nearest corner vertex
                triangles.Add((i, lastVertexIdx, lastClosestSquareIdx));
            }
            if(i == vertices.Count - 1) // last wrap
            {
                triangles.Add((i, 4, closestSquareIdx));
            }
            lastVertexIdx = i;
            lastClosestSquareIdx = closestSquareIdx;
            lastClosestOtherSquareIdx = closestOtherSquareIdx;
        }
        return new TriangleObject(vertices.ToArray(), triangles.ToArray(), colors.Select(c => c.ToColor()).ToArray());
    }

    public static bool HasHoles(ReadOnlySpan<Vector3> vertices, ReadOnlySpan<Int3> triangles)
    {
        // Dictionary to count edge occurrences
        Dictionary<(int, int), int> edgeCount = new Dictionary<(int, int), int>();

        foreach (var tri in triangles)
        {
            int[] inds = { tri.X, tri.Y, tri.Z };
            for (int i = 0; i < 3; i++)
            {
                int v0 = inds[i];
                int v1 = inds[(i + 1) % 3];
                var edge = v0 < v1 ? (v0, v1) : (v1, v0); // store edges consistently
                if (edgeCount.ContainsKey(edge)) edgeCount[edge]++;
                else edgeCount[edge] = 1;
            }
        }

        // Count boundary edges (edges used only once)
        int boundaryEdges = 0;
        foreach (var kv in edgeCount)
            if (kv.Value == 1) boundaryEdges++;

        // A mesh has a hole if there is at least one boundary edge
        return boundaryEdges > 0;
    }

    public static void EnsureConsistentWinding(Memory<Vector3> vertices, Memory<Int3> triangles)
    {
        var tris = triangles.Span;
        int n = tris.Length;

        // Map undirected edge -> list of triangle indices sharing it
        var edgeToTris = new Dictionary<(int, int), List<int>>();
        for (int i = 0; i < n; i++)
        {
            var t = tris[i];
            AddEdge(t.X, t.Y, i);
            AddEdge(t.Y, t.Z, i);
            AddEdge(t.Z, t.X, i);
        }

        var visited = new bool[n];
        var stack = new Stack<int>();

        // Start from first triangle
        visited[0] = true;
        stack.Push(0);

        while (stack.Count > 0)
        {
            int i = stack.Pop();
            var t = tris[i];

            // Process all edges
            ProcessEdge(t.X, t.Y, i, tris);
            ProcessEdge(t.Y, t.Z, i, tris);
            ProcessEdge(t.Z, t.X, i, tris);
        }

        void AddEdge(int a, int b, int triIndex)
        {
            var key = a < b ? (a, b) : (b, a);
            if (!edgeToTris.TryGetValue(key, out var list))
            {
                list = new List<int>();
                edgeToTris[key] = list;
            }
            list.Add(triIndex);
        }

        void ProcessEdge(int a, int b, int fromTri, Span<Int3> tris)
        {
            var key = a < b ? (a, b) : (b, a);
            foreach (int neighbor in edgeToTris[key])
            {
                if (neighbor == fromTri || visited[neighbor]) continue;

                var tNeighbor = tris[neighbor];

                // Determine if neighbor shares the edge in the same direction
                bool sameDir = SharesEdgeSameDirection(tNeighbor, a, b);

                if (sameDir)
                {
                    // Flip neighbor
                    tNeighbor = new Int3(tNeighbor.X, tNeighbor.Z, tNeighbor.Y);
                    tris[neighbor] = tNeighbor;
                }

                visited[neighbor] = true;
                stack.Push(neighbor);
            }
        }

        bool SharesEdgeSameDirection(Int3 t, int a, int b)
        {
            return (t.X == a && t.Y == b) ||
                   (t.Y == a && t.Z == b) ||
                   (t.Z == a && t.X == b);
        }
    }
}


public struct Bounds
{
    public Vector3 Center { get; init; }
    public Vector3 Extends => Size / 2f;
    public Vector3 Size { get; init; }
    public Vector3 Min => Center - Extends;
    public Vector3 Max => Center + Extends;

    public bool Intersects(Bounds other) => ShapeUtils.Distance(this, other) < 0;
    public float Distance(Bounds other) => ShapeUtils.Distance(this, other);
    public bool Contains(Vector3 point) => ShapeUtils.Distance(this, point) < 0;
    public float Distance(Vector3 point) => ShapeUtils.Distance(this, point);
    public bool IsEmpty => Size.X <= 0 || Size.Y <= 0 || Size.Z <= 0;
    public float Volume => Size.X * Size.Y * Size.Z;


    public static Bounds FromCorners(Vector3 a, Vector3 b)
    {
        var min = Vector3.Min(a, b);
        var max = Vector3.Max(a, b);

        return new Bounds
        {
            Center = (min + max) * 0.5f,
            Size = max - min
        };
    }
}
