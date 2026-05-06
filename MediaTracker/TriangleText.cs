
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

    public TriangleText(string character, Font font, IKeysRenderer renderer = null!) 
        : base(withOutline: false,
            withFill: true,
            filled: true,
            uniqueVertices: false,
            renderer: renderer)
    {

        GeneratePointData(character, font, out var vertices, out var triangles);
        this.Triangles = triangles.ToArray();
        this.Vertices = vertices.ToArray();
        this.Colors = Vertices.Select(_ => Color.Black.ToVector4()).ToArray();

    }

    List<List<PointF>> GetContours(GraphicsPath path)
    {
        List<List<PointF>> contours = new List<List<PointF>>();
        List<PointF> current = new List<PointF>();
        var points = path.PathPoints;
        byte[] types = path.PathTypes;

        for (int i = 0; i < points.Length; i++)
        {
            byte type = (byte)(types[i] & 0x07); // lower 3 bits = point type

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

    void GeneratePointData(string character, Font font, out List<Vector3> vertices, out List<Int3> triangles)
    {
        var path = new GraphicsPath();
        path.AddString(character, font.FontFamily, (int)font.Style, font.Size, new Point(0, 0), StringFormat.GenericDefault);
        var contours = GetContours(path);

        var tess = new Tess();
        foreach (var contour in contours)
        {
            ContourVertex[] verts = contour.Select(p => new ContourVertex
            {
                Position = new LibTessDotNet.Double.Vec3 { X = p.X, Y = p.Y, Z = 0 }
            }).ToArray();

            tess.AddContour(verts, ContourOrientation.Original);
        }
        tess.Tessellate(WindingRule.NonZero, ElementType.Polygons, 3);
        vertices = tess.Vertices.Select(v => new Vector3((float)v.Position.X, -(float)v.Position.Y, (float)v.Position.Z) - new Vector3(0.5f,-0.5f,0)).ToList();
        triangles = new List<Int3>();
        for (int i = 0; i < tess.Elements.Length; i += 3)
        {
            triangles.Add(new Int3(tess.Elements[i], tess.Elements[i + 1], tess.Elements[i + 2]));
        }

    }


}
#pragma warning restore CA1416 // Validate platform compatibility
