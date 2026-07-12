
using GBX.NET;
using LibTessDotNet.Double;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Numerics;
using Color = System.Drawing.Color;

namespace TM_GenericMapping.Common;

#pragma warning disable CA1416 // Validate platform compatibility

/// <summary>
/// High Poly count!!!
/// </summary>
public class TriangleText : TriangleObject
{
    public TriangleText(string character, Font font, Color? fillColor = null,
        bool withOutline = false, Color? outlineColor = null, float outlineWidth = 0.1f, 
        OutlineExtendsDirection outlineExtends = OutlineExtendsDirection.Outwards,
        float smoothness = 1f,
        float outlineSmoothness = 1f,
        IKeysRenderer renderer = null!)
        : base(withOutline: withOutline,
            outlineWidth: outlineWidth,
            outlineExtends: outlineExtends,
            withFill: true,
            filled: true,
            uniqueVertices: false,
            renderer: renderer)
    {
        GeneratePointData(character, font, withOutline, outlineWidth,
            smoothness, outlineSmoothness, outlineExtends,
            out var vertices, out var triangles,
            out var outlineVertices, out var outlineTriangles);

        // Build combined vertex/triangle list — outline first (drawn behind)
        var allVertices = new List<Vector3>();
        var allTriangles = new List<Int3>();
        var allColors = new List<Vector4>();

        if (withOutline && outlineVertices != null && outlineTriangles != null)
        {
            int offset = allVertices.Count;
            allVertices.AddRange(outlineVertices.Select(v=> outlineExtends switch 
            { 
                OutlineExtendsDirection.Inwards => v + new Vector3(0, 0, TrianglesUtils.SafeClippingOffset),
                OutlineExtendsDirection.Outwards => v + new Vector3(0, 0, -TrianglesUtils.SafeClippingOffset),
                OutlineExtendsDirection.Bidirectional => v + new Vector3(0, 0, TrianglesUtils.SafeClippingOffset),
                _ => v
            }));
            allTriangles.AddRange(outlineTriangles.Select(t =>
                new Int3(t.X + offset, t.Y + offset, t.Z + offset)));
            allColors.AddRange(outlineVertices.Select(_ =>
                (outlineColor ?? Color.Black).ToVector4()));
        }
        
        {
            int offset = allVertices.Count;
            allVertices.AddRange(vertices);
            allTriangles.AddRange(triangles.Select(t =>
                new Int3(t.X + offset, t.Y + offset, t.Z + offset)));
            allColors.AddRange(vertices.Select(_ =>
                (fillColor ?? Color.Black).ToVector4()));
        }
        this.FillVertexCount = vertices.Count;
        this.FillTrianglesCount = triangles.Count;
        this.Vertices = allVertices.ToArray();
        this.Triangles = allTriangles.ToArray();
        this.Colors = allColors.ToArray();
    }

    List<List<PointF>> GetContours(GraphicsPath path)
    {
        List<List<PointF>> contours = new();
        List<PointF> current = new();
        var points = path.PathPoints;
        var types = path.PathTypes;

        for (int i = 0; i < points.Length; i++)
        {
            byte type = (byte)(types[i] & 0x07);
            if (type == (byte)PathPointType.Start)
            {
                if (current.Count > 0)
                    contours.Add(current);
                current = new List<PointF>();
            }
            current.Add(points[i]);
        }

        if (current.Count > 0)
            contours.Add(current);

        return contours;
    }

    // Expand a contour outward by `amount` using per-vertex edge normals
    List<PointF> ExpandContour(List<PointF> contour, float amount)
    {
        // Flip direction for holes (clockwise = negative area)
        float dir = SignedArea(contour) >= 0 ? 1f : -1f;
        int n = contour.Count;
        var result = new List<PointF>(n);

        for (int i = 0; i < n; i++)
        {
            var prev = contour[(i - 1 + n) % n];
            var curr = contour[i];
            var next = contour[(i + 1) % n];

            // Normals of the two adjacent edges
            var e1 = new Vector2(curr.X - prev.X, curr.Y - prev.Y);
            var e2 = new Vector2(next.X - curr.X, next.Y - curr.Y);

            var n1 = Vector2.Normalize(new Vector2(-e1.Y, e1.X));
            var n2 = Vector2.Normalize(new Vector2(-e2.Y, e2.X));

            var avg = Vector2.Normalize(n1 + n2);

            // Miter correction so the offset distance stays consistent at corners
            float dot = Vector2.Dot(n1, avg);
            float miter = dot > 0.01f ? amount / dot : amount; // clamp to avoid spikes

            result.Add(new PointF(curr.X + avg.X * miter * dir, curr.Y + avg.Y * miter * dir));
        }

        return result;
    }

    void TessellateContours(List<List<PointF>> contours, WindingRule winding,
    out List<Vector3> vertices, out List<Int3> triangles)
    {
        var tess = new Tess();
        foreach (var contour in contours)
        {
            if (contour.Count < 3) continue; // skip degenerate contours

            var verts = contour.Select(p => new ContourVertex
            {
                Position = new LibTessDotNet.Double.Vec3 { X = p.X, Y = p.Y, Z = 0 }
            }).ToArray();
            tess.AddContour(verts, ContourOrientation.Original);
        }

        tess.Tessellate(winding, ElementType.Polygons, 3);

        vertices = tess.Vertices?
            .Select(v => new Vector3((float)v.Position.X, -(float)v.Position.Y, (float)v.Position.Z)
                - new Vector3(0.5f, -0.5f, 0))
            .ToList() ?? new List<Vector3>();

        triangles = new List<Int3>();
        if (tess.Elements == null || tess.Vertices == null) return;

        for (int i = 0; i + 2 < tess.Elements.Length; i += 3)
        {
            int a = tess.Elements[i];
            int b = tess.Elements[i + 1];
            int c = tess.Elements[i + 2];

            // Guard against out-of-range indices
            if (a < tess.Vertices.Length && b < tess.Vertices.Length && c < tess.Vertices.Length)
                triangles.Add(new Int3(a, b, c));
        }
    }
    float SignedArea(List<PointF> contour)
    {
        float area = 0;
        int n = contour.Count;
        for (int i = 0; i < n; i++)
        {
            var a = contour[i];
            var b = contour[(i + 1) % n];
            area += (a.X * b.Y) - (b.X * a.Y);
        }
        return area / 2f;
    }

    void GeneratePointData(string character, Font font,
     bool withOutline, float outlineSize, float smoothness, float outlineSmoothness, OutlineExtendsDirection extendsDirection,
     out List<Vector3> vertices, out List<Int3> triangles,
     out List<Vector3>? outlineVertices, out List<Int3>? outlineTriangles)
    {
        var path = new GraphicsPath();
        path.AddString(character, font.FontFamily, (int)font.Style, font.Size,
            new Point(0, 0), StringFormat.GenericDefault);
        path.Flatten(null, font.Size * 0.005f * smoothness);



        var contours = GetContours(path);
        TessellateContours(contours, WindingRule.NonZero, out vertices, out triangles);

        if (withOutline)
        {
            // Widen expands the path outward correctly, handling all curves and corners
            var outlinePath = (GraphicsPath)path.Clone();
            outlinePath.Flatten(null, font.Size * 0.005f * outlineSmoothness); // before Widen
            outlinePath.Widen(new Pen(Color.Black, outlineSize * 2));

            switch (extendsDirection)
            {
                case OutlineExtendsDirection.Outwards:
                    outlinePath.AddPath(path, false);
                    TessellateContours(GetContours(outlinePath), WindingRule.EvenOdd,
                        out outlineVertices, out outlineTriangles);
                    break;
                case OutlineExtendsDirection.Inwards:
                    TessellateContours(GetContours(outlinePath), WindingRule.EvenOdd,
                        out outlineVertices, out outlineTriangles);
                    break;
                case OutlineExtendsDirection.Bidirectional:
                    TessellateContours(GetContours(outlinePath), WindingRule.NonZero,
                        out outlineVertices, out outlineTriangles);
                    break;
                default:
                    outlineVertices = [] ;
                    outlineTriangles = [];
                    break;
            }

        }
        else
        {
            outlineVertices = null;
            outlineTriangles = null;
        }
    }
}
#pragma warning restore CA1416 // Validate platform compatibility
