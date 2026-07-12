using GBX.NET;
using System.Numerics;
using System.Drawing;
using TM_GenericMapping.Common;
using Color = System.Drawing.Color;

namespace TM_GenericMapping.MediaTracker;

public static class TriangleObjectFactory
{
    public static TriangleObject Create(
        ShapeParameter shape,
        FillParameter? fill = null,
        OutlineParameter? outline = null,
        RenderingParameter? rendering = null)
    {
        rendering ??= new();
        fill ??= new();
        outline ??= new();

        var renderer = rendering.Renderer!;

        var points = shape.Points;
        var hasTriangles = shape.Triangles is { Length: > 0 };
        var hasVertexColors = shape.Colors is { Length: > 0 } &&
                               shape.Colors.Length == points.Length;

        var colors = hasVertexColors
            ? shape.Colors
            : Enumerable.Repeat(fill.FillColor, points.Length).ToArray();

        // mesh path
        if (hasTriangles)
        {
            return new TriangleObject(
                points,
                shape.Triangles,
                colors,
                rendering.UniqueVertices,
                renderer);
        }

        // polygon path (single constructor handles both fill styles internally)
        return new TriangleObject(
            points: points,
            fillColors: colors,
            outlineColor: outline.OutlineColor,
            withOutline: outline.WithOutline,
            outlineExtends: outline.OutlineExtendsDirection,
            outlineWidth: outline.OutlineWidth,
            withFill: fill.WithFill,
            filled: fill.Filled,
            uniqueVertices: rendering.UniqueVertices,
            renderer: renderer);
    }
    public static TriangleObject Create(
        ReadOnlySpan<Vector3> points,
        FillParameter? fill = null,
        OutlineParameter? outline = null,
        RenderingParameter? rendering = null)
        => Create(new ShapeParameter { Points = points.ToArray() }, fill, outline, rendering);

    public static Square CreateSquare(float size,
        FillParameter? fill = null,
        OutlineParameter? outline = null,
        RenderingParameter? rendering = null)
    {
        rendering ??= new();
        fill ??= new();
        outline ??= new();
        var renderer = rendering.Renderer!;

        return new Square(
            size: size,
            fillColor: fill.FillColor,
            outlineColor: outline.OutlineColor,
            withOutline: outline.WithOutline,
            outlineExtends: outline.OutlineExtendsDirection,
            outlineWidth: outline.OutlineWidth,
            withFill: fill.WithFill,
            filled: fill.Filled,
            uniqueVertices: rendering.UniqueVertices,
            renderer: renderer);
    }


}

public record ShapeParameter
{
    public Vector3[] Points { get; set; } = [];
    public Int3[] Triangles { get; set; } = [];
    public Color[] Colors { get; set; } = [];
}
public record RenderingParameter
{
    public IKeysRenderer? Renderer { get; set; } = null;
    public bool UniqueVertices { get; set; } = false;
}
public record FillParameter
{
    public Color FillColor { get; set; } = Color.Black;
    public bool WithFill { get; set; } = true;
    public bool Filled { get; set; } = true;
}
public record OutlineParameter
{
    public Color OutlineColor { get; set; } = Color.Black;
    public bool WithOutline { get; set; } = true;
    public OutlineExtendsDirection OutlineExtendsDirection { get; set; } = OutlineExtendsDirection.Outwards;
    public float OutlineWidth { get; set; } = 0.1f;
}