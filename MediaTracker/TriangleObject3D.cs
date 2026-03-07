using GBX.NET;
using System;
using System.Numerics;
using TM_GenericMapping.Common;
using static GBX.NET.Engines.Plug.CPlugVisual;
using static System.Collections.Specialized.BitVector32;
using static TM_GenericMapping.Common.Spline;
using Color = System.Drawing.Color;

namespace TM_GenericMapping.Common;

public class TriangleObject3D : TriangleObject
{
    public TriangleObject3D(TriangleObject3D other) : base(other)
    {
        Name = "TriangleObject3D";
    }
    /// <summary>
    /// Emnpt
    /// </summary>
    /// <param name="uniqueVertices"></param>
    /// <param name="renderer"></param>
    public TriangleObject3D(
        bool uniqueVertices = false,
        IRenderer renderer = null!) 
        : base(withOutline: false, withFill: true, filled: true, uniqueVertices: uniqueVertices, renderer: renderer)
    {
        Name = "TriangleObject3D";
    }
    public TriangleObject3D(
        ReadOnlySpan<Vector3> points,
        Color? fillColor = null,
        bool filled = true,
        bool uniqueVertices = false,
        IRenderer renderer = null!)
        : base(points: points,
            fillColor: fillColor,
            filled: filled,
            withOutline: false,
            uniqueVertices: uniqueVertices, 
            renderer: renderer)
    {
        Name = "TriangleObject3D";
    }
    public TriangleObject3D(
       ReadOnlySpan<Vector3> points,
       ReadOnlySpan<Int3> triangles,
       ReadOnlySpan<Color> colors,
       bool uniqueVertices = false,
       IRenderer renderer = null!
       ) : base(points, triangles, colors, uniqueVertices, renderer)
    {
        Name = "TriangleObject3D";
    }

    public TriangleObject3D(
        ReadOnlySpan<Vector3> points,
        ReadOnlySpan<Color> colors,
        bool uniqueVertices = false,
        IRenderer renderer = null!)
      : base(points: points,
          fillColors: colors,
          filled: true,
          withOutline: false,
          uniqueVertices: uniqueVertices,
          renderer: renderer)
    {
        Name = "TriangleObject3D";
    }
    protected override Int3[] Triangulate(ReadOnlySpan<Vector3> points)
        => ConvexHull3D.Triangulate(points);
    public override TriangleObject3D Clone()
    {
        return new TriangleObject3D(this);
    }
}
public class Cuboid : TriangleObject3D
{
    public Cuboid(
        Vector3 size,
        Color? fillColor = null,
        bool uniqueVertices = false,
        IRenderer renderer = null!)
        : base(points: 
            [
                new Vector3(-size.X / 2f, -size.Y / 2f, -size.Z / 2f),
                new Vector3(size.X / 2f, -size.Y / 2f, -size.Z / 2f),
                new Vector3(size.X / 2f, size.Y / 2f, -size.Z / 2f),
                new Vector3(-size.X / 2f, size.Y / 2f, -size.Z / 2f),
                new Vector3(-size.X / 2f, -size.Y / 2f, size.Z / 2f),
                new Vector3(size.X / 2f, -size.Y / 2f, size.Z / 2f),
                new Vector3(size.X / 2f, size.Y / 2f, size.Z / 2f),
                new Vector3(-size.X / 2f, size.Y / 2f, size.Z / 2f)
            ],
            filled: true,
            fillColor: fillColor,
            uniqueVertices: uniqueVertices,
            renderer: renderer)
    {
        Name = "Cuboid";
    }
}
public class Cube : Cuboid
{
    public Cube(
        Color? fillColor = null,
        float size = 1f,
        bool uniqueVertices = false,
        IRenderer renderer = null!)
        : base(size: new Vector3(size), fillColor: fillColor, uniqueVertices, renderer: renderer)
    {
        Name = "Cube";
    }
}

public class Plane : TriangleObject3D
{
    public Plane(
        float size,
        Color? color = null,
        bool uniqueVertices = false,
        IRenderer renderer = null!) : base(points:
            [
            new Vector3(-size / 2f, 0, size / 2f),
            new Vector3(size / 2f, 0, size / 2f),
            new Vector3(size / 2f, 0, -size / 2f),
            new Vector3(-size / 2f, 0, -size / 2f),
            ],
            triangles: [(0, 1, 2), (0, 2, 3)],
            colors: [color ?? Color.Black, color ?? Color.Black, color ?? Color.Black, color ?? Color.Black],
            uniqueVertices: uniqueVertices,
            renderer: renderer)
    {
        Name = "Plane";
    }
    public Plane(
        Vector3 br,
        Vector3 bl,
        Vector3 tl,
        Vector3 tr,
        Color? color = null,
        bool uniqueVertices = false,
        IRenderer renderer = null!) : base(points:
            [
            br, bl ,tl, tr
            ],
            triangles: [(0, 1, 2), (2, 0, 3)],
            colors: [color ?? Color.Black, color ?? Color.Black, color ?? Color.Black, color ?? Color.Black],
            uniqueVertices: uniqueVertices,
            renderer: renderer)
    {
        Name = "Plane";
    }

}

public class Sphere : TriangleObject3D
{
    public float Radius { get; private init; }
    public Int2 Resolution { get; private init; }
    /// <summary>
    /// 
    /// </summary>
    /// <param name="radius"></param>
    /// <param name="resolution">[phi, theta]</param>
    /// <param name="fillColor"></param>
    /// <param name="uniqueVertices"></param>
    /// <param name="renderer"></param>
    public Sphere(float radius = 1, 
        Int2? resolution = null,
        Color? fillColor = null,
        bool uniqueVertices = false,
        IRenderer renderer = null!)
        : base(uniqueVertices: uniqueVertices, renderer: renderer)
    {
        Resolution = resolution ?? (20, 20);
        ExceptionUtils.Ensure(Resolution.X >= 2, () => new ArgumentException("Latitute resolution must be at least 2"));
        ExceptionUtils.Ensure(Resolution.Y >= 3, () => new ArgumentException("Longitute resolution must be at least 3"));
        ExceptionUtils.Ensure(radius > 0, () => new ArgumentException("Radius must be at least 0"));
        Radius = radius;

        Name = "Sphere";

        ShapeUtils.GenerateSphereShape(Radius, Resolution, out var newVertices, out var newTriangles);
        ShapePoints = newVertices.ToArray();
        if (HasUniqueVertices)
            ShapeUtils.MakeVerticesUnique(ShapePoints, newTriangles.ToArray(), out newVertices, out newTriangles);
        Vertices = newVertices.ToArray();
        Triangles = newTriangles.ToArray();
        FillVertexCount = Vertices.Length;
        FillTrianglesCount = Triangles.Length;
        Colors = Enumerable.Repeat(fillColor ?? Color.Black, Vertices.Length).Select(c => c.ToVector4()).ToArray();
    }
    public Vector3 GetShapePointOnSphere(float phi, float theta)
    {
        float x = Radius * MathF.Sin(theta) * MathF.Cos(phi);
        float y = Radius * MathF.Cos(theta);
        float z = Radius * MathF.Sin(theta) * MathF.Sin(phi);

        return new Vector3(x, y, z);
    }
    public Vector3[] GetPointsOnSphereFromShape()
    {
        // No +1 in longitude (Y), only in latitude (X)
        Vector3[] vertices = new Vector3[(Resolution.X + 1) * Resolution.Y];

        for (int v = 0; v <= Resolution.X; v++)
        {
            float theta = v / (float)Resolution.X; // latitude 0..1

            for (int u = 0; u < Resolution.Y; u++) // strictly < Resolution.Y
            {
                float phi = u / (float)Resolution.Y; // longitude 0..1 (wrap in triangles)
                vertices[v * Resolution.Y + u] = GetShapePointOnSphere(phi * MathF.PI, theta * MathF.PI * 2);
            }
        }

        return vertices;
    }

}

public class SweepShape : TriangleObject3D
{
    public int NumSections { get; private set; }
    public Vector2[] CrossSection { get; private set; }

    public SweepShape(Spline shape,
        ReadOnlySpan<Vector2> crossSection,
        int sections,
        Vector3? forcedStartTangent = null,
        Vector3? forcedEndTangent = null,
        Color? color = null,
        bool uniqueVertices = false,
        IRenderer renderer = null!)
        : base(uniqueVertices, renderer)
    {
        NumSections = sections;
        CrossSection = crossSection.ToArray();
        GenerateShape(shape, crossSection, sections, color ?? Color.Black, forcedStartTangent, forcedEndTangent, out var vertices, out var triangles, out var colors);
        var vertArray = vertices.ToArray();
        var trisArray = triangles.ToArray();
        ShapeUtils.EnsureConsistentWinding(vertArray, trisArray);
        if (uniqueVertices)
            ShapeUtils.MakeVerticesUnique(vertArray, trisArray, colors.ToArray(), out vertices, out triangles, out colors);
        else
        {
            vertices = vertArray.ToList();
            triangles = triangles.ToList();
        }
        ShapePoints = vertices.ToArray();
        Vertices = vertices.ToArray();
        Triangles = triangles.ToArray();
        Colors = colors.ToArray();
        FillVertexCount = Vertices.Length;
        FillTrianglesCount = Triangles.Length;
    }


    static void GenerateShape(Spline spline, 
        ReadOnlySpan<Vector2> crossSection,
        int sections,
        Color color,
        Vector3? forcedStartTangent,
        Vector3? forcedEndTangent,
        out List<Vector3> vertices, out List<Int3> triangles, out List<Vector4> colors)
    {
        vertices = new List<Vector3>();
        triangles = new List<Int3>();

        int numSlices = spline.Type == SplineType.Linear ? spline.Knots.Length : sections + 1;

        Vector3 prevRight = Vector3.Zero;
        Vector3 prevUp = Vector3.Zero;
        bool hasPrev = false;

        for (int i = 0; i < numSlices; ++i)
        {
            Vector3 p, tangent;
            Quaternion q;

            if (spline.Type == SplineType.Linear)
            {
                var knot = spline.Knots[i];
                p = knot.Position;
                q = knot.Rotation;

                if (i == 0 && forcedStartTangent != null)
                {
                    tangent = (Vector3)forcedStartTangent;
                }
                else if (i == numSlices - 1 && forcedEndTangent != null)
                {
                    tangent = (Vector3)forcedEndTangent;
                }
                else if (i == 0) // first knot, no incoming segment
                {
                    tangent = Vector3.Normalize(spline.Knots[i + 1].Position - knot.Position);
                }
                else if (i == numSlices - 1) // last knot, no outgoing segment
                {
                    tangent = Vector3.Normalize(knot.Position - spline.Knots[i - 1].Position);
                }
                else
                {
                    // middle knots: average of incoming and outgoing segment directions
                    Vector3 inDir = Vector3.Normalize(knot.Position - spline.Knots[i - 1].Position);
                    Vector3 outDir = Vector3.Normalize(spline.Knots[i + 1].Position - knot.Position);
                    tangent = Vector3.Normalize(inDir + outDir);
                }
            }
            else
            {
                float t = i / (float)(numSlices - 1);
                p = spline.Evaluate(t);
                q = spline.EvaluateRotation(t);
                tangent = (i == 0 && forcedStartTangent != null) ? (Vector3)forcedStartTangent :
                          (i == numSlices - 1 && forcedEndTangent != null) ? (Vector3)forcedEndTangent :
                          spline.EvaluateTangent(t);
            }

            if (!hasPrev)
            {
                Vector3 upWorld = Math.Abs(Vector3.Dot(tangent, Vector3.UnitY)) > 0.999f
                    ? Vector3.UnitZ
                    : Vector3.UnitY;

                prevRight = Vector3.Normalize(Vector3.Cross(upWorld, tangent));
                prevUp = Vector3.Cross(tangent, prevRight);
                hasPrev = true;
            }
            else
            {
                // parallel transport
                Vector3 newRight = Vector3.Normalize(
                    prevRight - tangent * Vector3.Dot(prevRight, tangent)
                );

                if (newRight.LengthSquared() < 1e-6f)
                    newRight = prevRight;

                Vector3 newUp = Vector3.Cross(tangent, newRight);

                prevRight = newRight;
                prevUp = newUp;
            }

            Vector3 right = Vector3.Transform(prevRight, q);
            Vector3 up = Vector3.Transform(prevUp, q);

            foreach (var c in crossSection)
            {
                Vector3 v = p + right * c.X + up * c.Y;
                vertices.Add(v);
            }
        }

        int cs = crossSection.Length;
        for (int i = 0; i < numSlices - 1; i++)
        {
            int a = i * cs;
            int b = (i + 1) * cs;

            for (int j = 0; j < cs; j++)
            {
                int jNext = (j + 1) % cs;
                triangles.Add((a + j, a + jNext, b + j));
                triangles.Add((a + jNext, b + jNext, b + j));
            }
        }

        // Cap start
        int startOffset = 0;
        for (int j = 0; j < cs - 2; j++)
            triangles.Add((startOffset, startOffset + j + 2, startOffset + j + 1));

        // Cap end
        int endOffset = (numSlices - 1) * cs;
        for (int j = 0; j < cs - 2; j++)
            triangles.Add((endOffset,  endOffset + j + 1, endOffset + j + 2));

        colors = Enumerable.Repeat(color.ToVector4(), vertices.Count).ToList();
    }
}


