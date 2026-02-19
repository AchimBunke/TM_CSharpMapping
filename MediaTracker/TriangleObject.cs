using GBX.NET;
using GBX.NET.Engines.Game;
using GBX.NET.Inputs;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Numerics;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using TM_GenericMapping.Common;
using TM_GenericMapping.Common.IO;
using static GBX.NET.Engines.Plug.CPlugSurface.Mesh;
using Color = System.Drawing.Color;

namespace TM_GenericMapping.Common;

public interface IMorphable
{
    Memory<Vector3> Vertices { get; }
    Memory<Int3> Triangles { get; }
    int FillVertexCount { get; }
    int FillTrianglesCount { get; }
    bool CanMorph();
    int VertexIdxOffset { get; }
    IEnumerable<IMorphable> SubObjects { get; }

}

public interface IAABB
{
    Bounds GetAABB();
}

/// <summary>
/// Only modify Triangles/Vertices/Colors directly if you know what your doing.
/// </summary>
public class TriangleObject : RenderObject, IFillable, IOutlineable, IMorphable, IPointPath, IAABB
{
    // Setter on shape ??
    public Int3[] Triangles { get; set; } = [];
    public Vector3[] Vertices { get; set; } = [];
    public Vector4[] Colors { get; set; } = [];
    public Vector3[] ShapePoints { get; protected set; } = [];

    public int FillVertexCount { get; set; }
    public int FillTrianglesCount { get; set; }

    public bool HasOutline { get; set; }
    public float OutlineWidth { get; private set; }
    public virtual void SetOutlineWidth(float width)
    {
        if (OutlineWidth == width)
            return;
        ExceptionUtils.Ensure(HasOutline, () => throw new InvalidOperationException($"Cannot Outline {Name}"));
        CreateOutlineShape(ShapePoints, Color.Black, OutlineExtends, HasUniqueVertices, out var outlineVertices, out _, out _);
        for (int i = 0; i < outlineVertices.Count; ++i)
        {
            Vertices[FillVertexCount + i] = outlineVertices[i];
        }
        OutlineWidth = width;
    }
    public OutlineExtendsDirection OutlineExtends { get; init; } = OutlineExtendsDirection.Outwards;

    public bool CanFill { get; init; }
    public bool IsFilled { get; protected set; }
    public virtual void SetFilled(bool value)
    {
        if (IsFilled == value)
            return;
        if (!value)
            ExceptionUtils.Ensure(HasOutline, () => throw new InvalidOperationException($"Cannot 'unfill' {Name}"));
        //Todo
        if (value)
        {
            for (int i = 0; i < FillVertexCount; ++i)
            {
                // Set every point to same position for no visual triangles
                // Maybe change to other behavior (move away from screen -> maybe wors for incremental fill)
                Vertices[i] = ShapePoints[i];
            }
        }
        else
        {
            for (int i = 0; i < FillVertexCount; ++i)
            {
                // Set every point to same position for no visual triangles
                // Maybe change to other behavior (move away from screen -> maybe wors for incremental fill)
                Vertices[i] = ShapePoints[0];
            }
        }
        IsFilled = value;
    }
    public bool HasUniqueVertices { get; init; }

    Memory<Vector3> IMorphable.Vertices => Vertices;

    Memory<Int3> IMorphable.Triangles => Triangles;


    public int VertexIdxOffset => 0;

    IEnumerable<IMorphable> IMorphable.SubObjects => SubObjects.OfType<IMorphable>();

    protected TriangleObject(IRenderer renderer) : base(renderer ?? Rendering.DefaultTriangleRenderer)
    {
        CanShareBlock = true;
        Name = "TriangleObject";
    }

    public TriangleObject(TriangleObject other) : base(other)
    {
        ShapePoints = other.ShapePoints.ToArray();
        Vertices = other.Vertices.ToArray();
        Triangles = other.Triangles.ToArray();
        Colors = other.Colors.ToArray();
        FillVertexCount = other.FillVertexCount;
        FillTrianglesCount = other.FillTrianglesCount;
        HasOutline = other.HasOutline;
        OutlineWidth = other.OutlineWidth;
        OutlineExtends = other.OutlineExtends;
        CanFill = other.CanFill;
        IsFilled = other.IsFilled;
        HasUniqueVertices = other.HasUniqueVertices;
    }

    /// <summary>
    /// Points must be Counterclockwise
    /// </summary>
    /// <param name="points">Counterclockwise points of shape</param>
    /// <param name="withOutline"></param>
    /// <param name="withFill"></param>
    /// <param name="filled"></param>
    /// <param name="outlineWidth"></param>
    /// <param name="uniqueVertices"></param>
    /// <exception cref="ArgumentException"></exception>
    public TriangleObject(ReadOnlySpan<Vector3> points,
        Color? fillColor = null,
        Color? outlineColor = null,
        bool withOutline = false,
        bool withFill = true,
        bool filled = true,
        OutlineExtendsDirection outlineExtends = OutlineExtendsDirection.Outwards,
        float outlineWidth = 0.1f,
        bool uniqueVertices = false,
        IRenderer renderer = null!) : this(withOutline, withFill, filled, outlineExtends, outlineWidth, uniqueVertices, renderer)
    {
        ShapePoints = points.ToArray();
        CreateShape(points, fillColor ?? Color.Black, outlineColor ?? Color.Black);
        SetFilled(filled);
    }
    /// <summary>
    /// Initialize an Object with no points
    /// </summary>
    /// <param name="fillColor"></param>
    /// <param name="outlineColor"></param>
    /// <param name="withOutline"></param>
    /// <param name="withFill"></param>
    /// <param name="filled"></param>
    /// <param name="outlineWidth"></param>
    /// <param name="uniqueVertices"></param>
    /// <exception cref="ArgumentException"></exception>
    public TriangleObject(
        bool withOutline = false,
        bool withFill = true,
        bool filled = true,
        OutlineExtendsDirection outlineExtends = OutlineExtendsDirection.Outwards,
        float outlineWidth = 0.1f,
        bool uniqueVertices = false,
        IRenderer renderer = null!) : this(renderer)
    {
        if (!withFill && !withOutline)
            throw new ArgumentException("One of Outline or Fill must be set to create shape");
        HasUniqueVertices = uniqueVertices;
        HasOutline = withOutline;
        CanFill = withFill;
        IsFilled = filled;
        OutlineWidth = outlineWidth;
        OutlineExtends = outlineExtends;
    }

    public TriangleObject(
        ReadOnlySpan<Vector3> points,
        ReadOnlySpan<Int3> triangles,
        ReadOnlySpan<Color> colors,
        bool uniqueVertices = false,
        IRenderer renderer = null!
        ) : this(points, triangles, colors.ToArray().Select(c=>c.ToVector4()).ToArray(), uniqueVertices, renderer){}
    public TriangleObject(
        ReadOnlySpan<Vector3> points,
        ReadOnlySpan<Int3> triangles,
        ReadOnlySpan<Vector4> colors,
        bool uniqueVertices = false,
        IRenderer renderer = null!
        ) : this(
            withOutline: false,
            withFill: true,
            filled: true,
            uniqueVertices: uniqueVertices,
            renderer: renderer)
    {
        ExceptionUtils.Ensure(points.Length > 0, () => new ArgumentException("Num Points must be greater than 0"));
        ExceptionUtils.Ensure(colors.Length == points.Length, () => new ArgumentException("Num colors must be same size as vertices"));
        //ExceptionUtils.Ensure(triangles.Length > 0, () => new ArgumentException("Num Triangles must be greater than 0"));
        ShapePoints = points.ToArray();
        Vertices = points.ToArray();
        Triangles = triangles.ToArray();
        Colors = colors.ToArray();
        FillVertexCount = points.Length;
        FillTrianglesCount = triangles.Length;

        SetFilled(true);
    }

    public TriangleObject(TriangleObjectData data, IRenderer renderer = null!) : this(renderer)
    {
        Vertices = data.Vertices;
        Colors = data.Colors.Select(c => ColorUtils.ToVector4(c)).ToArray();
        Triangles = data.Triangles.Chunk(3).Select(c => new Int3(c[0], c[1], c[2])).ToArray();
        Name = data.Name;
        FillVertexCount = data.FillVertexCount;
        FillTrianglesCount = data.FillTrianglesCount;
        HasOutline = data.HasOutline;
        OutlineWidth = data.OutlineWidth;
        OutlineExtends = data.OutlineExtends;
        CanFill = data.CanFill;
        IsFilled = data.IsFilled;
        HasUniqueVertices = data.HasUniqueVertices;
        CanShareBlock = data.CanShareBlock;
        LocalPosition = data.LocalPosition;
        LocalRotation = data.LocalRotation;
        LocalScale = data.LocalScale;
        AddSubObjects(data.SubObjects.Select(s => new TriangleObject(s)).ToArray());
        AddComponents(data.SerializableComponents);
    }
    public TriangleObjectData AsTriangleObjectData()
    {
        var triangleObjectData = new TriangleObjectData()
        {
            Vertices = Vertices,
            Colors = Colors.Select(c => ColorUtils.ToColor(c)).ToArray(),
            Triangles = Triangles.SelectMany(t => new int[] { t.X, t.Y, t.Z }).ToArray(),
            Name = Name,
            FillVertexCount = FillVertexCount,
            FillTrianglesCount = FillTrianglesCount,
            HasOutline = HasOutline,
            OutlineWidth = OutlineWidth,
            OutlineExtends = OutlineExtends,
            CanFill = CanFill,
            IsFilled = IsFilled,
            HasUniqueVertices = HasUniqueVertices,
            CanShareBlock = CanShareBlock,
            LocalPosition = LocalPosition,
            LocalRotation = LocalRotation,
            LocalScale = LocalScale,
            SubObjects = SubObjects.Where(s => s is TriangleObject).Select(s => (s as TriangleObject).AsTriangleObjectData()).ToArray(),
            SerializableComponents = Components.OfType<ISerializableComponent>().ToArray(),
        };
        return triangleObjectData;
    }


    //public override int AddRenderDataToBlock(CGameCtnMediaBlock block)
    //{
    //    var triangleBlock = block as CGameCtnMediaBlockTriangles2D;
    //    int idx = triangleBlock.Vertices.Length;
    //    Int3 triangleOffset = (idx, idx, idx);
    //    triangleBlock.Vertices = triangleBlock.Vertices.Concat(Colors.Select(c => new Vec4(c.X, c.Y, c.Z, c.W))).ToArray();
    //    triangleBlock.Triangles = triangleBlock.Triangles.Concat(Triangles.Select(t => t + triangleOffset)).ToArray();
    //    return idx;
    //}

    //public override CGameCtnMediaBlock CreateEmptyBlock(BlockTemplates templates)
    //{
    //    var block = MediaTrackerUtils.DeepCopyBlockTriangles2D(templates.Triangles2D);
    //    block.Keys.Clear();
    //    return block;
    //}

    //public override IKey CreateAndAddEmptyKey(CGameCtnMediaBlock block)
    //{
    //    var key = new CGameCtnMediaBlockTriangles2D.Key(block as CGameCtnMediaBlockTriangles2D);
    //    (block as CGameCtnMediaBlockTriangles2D).Keys.Add(key);
    //    return key;
    //}

    //public override void SetKeyFrameData(CGameCtnMediaBlock block, IKey key, int idx, RenderData renderData, PostProcessingEffectData postProcessingEffectData)
    //{
    //    var triangleKey = key as CGameCtnMediaBlockTriangles2D.Key;
    //    for (int i = 0; i < Vertices.Length; ++i)
    //    {
    //        var v = ToMediaTrackerCoordinates(Vertices[i], renderData, postProcessingEffectData);
    //        triangleKey.Positions[idx + i] = new Vec3(v.X, v.Y, v.Z);
    //    }
    //}

    //public override bool CanShareBlockWith(MediaObject other)
    //{
    //    return other is Triangle2DObject;
    //}

    public void SetRenderer2D() => Renderer = new Triangle2DRenderer();
    public void SetRenderer3D() => Renderer = new Triangle3DRenderer();
    protected virtual Int3[] Triangulate(ReadOnlySpan<Vector3> points)
        => EarClippingTriangulation.Triangulate(points);

    protected virtual void CreateFilledShape(ReadOnlySpan<Vector3> points, Color fillColor, bool uniqueVertices, out List<Vector3> vertices, out List<Int3> triangles, out List<Vector4> colors)
    {
        var fillTriangles = Triangulate(points).ToArray();
        var fillVertices = points.ToArray().ToArray();
        ShapeUtils.EnsureConsistentWinding(fillVertices, fillTriangles);
        if (uniqueVertices)
            ShapeUtils.MakeVerticesUnique(fillVertices, fillTriangles, out vertices, out triangles);
        else
        {
            vertices = fillVertices.ToList();
            triangles = fillTriangles.ToList();
        }
        colors = Enumerable.Repeat(fillColor.ToVector4(), fillVertices.Length).ToList();
    }
    protected virtual void CreateOutlineShape(ReadOnlySpan<Vector3> points, Color outlineColor, OutlineExtendsDirection extendsOutwards, bool uniqueVertices,  out List<Vector3> vertices, out List<Int3> triangles, out List<Vector4> colors)
    {
        ShapeUtils.GenerateClosedPolygonOutline(points, OutlineWidth, extendsOutwards, out var outlineVertices, out var outlineTriangles);
        var vertArray = outlineVertices.ToArray();
        var trisArray = outlineTriangles.ToArray();
        ShapeUtils.EnsureConsistentWinding(vertArray, trisArray);
        if (uniqueVertices)
            ShapeUtils.MakeVerticesUnique(vertArray, trisArray, out vertices, out triangles);
        else
        {
            vertices = vertArray.ToList();
            triangles = trisArray.ToList();
        }
        colors = Enumerable.Repeat(outlineColor.ToVector4(), vertices.Count).ToList();
    }
    protected void CreateShape(ReadOnlySpan<Vector3> points, Color fillColor, Color outlineColor)
    {
        List<Int3> triangles = [];
        List<Vector3> vertices = [];
        List<Vector4> colors = [];
        if (CanFill)
        {
            CreateFilledShape(points, fillColor, HasUniqueVertices, out var fillVertices, out var fillTriangles, out var fillColors);
            triangles.AddRange(fillTriangles);
            vertices.AddRange(fillVertices);
            FillVertexCount = vertices.Count;
            FillTrianglesCount = triangles.Count;
            colors.AddRange(fillColors);
            IsFilled = true;
        }
        if (HasOutline)
        {
            CreateOutlineShape(points, outlineColor, OutlineExtends, HasUniqueVertices, out var outlineVertices, out var outlineTriangles, out var outlineColors);
            triangles.AddRange(outlineTriangles.Select(t => t + new Int3(FillVertexCount, FillVertexCount, FillVertexCount)));
            vertices.AddRange(outlineVertices);
            colors.AddRange(outlineColors);
        }
        Triangles = triangles.ToArray();
        Vertices = vertices.ToArray();
        Colors = colors.ToArray();
    }

    public new TriangleObjectAnimator<TriangleObject> Animate(bool continuosKeyFrames = false, ulong keyframeGenerationRateMillis = 0)
        => new TriangleObjectAnimator<TriangleObject>(this) { ContinuosKeyFrames = continuosKeyFrames, KeyframeGenerationRateMillis = keyframeGenerationRateMillis};

    public bool CanMorph()
        => !HasOutline && HasUniqueVertices;

    public Vector3 GetCentroid()
    {
        return ShapeUtils.GetCentroid(Vertices);
    }

    public Vector3 GetWeightedCenter()
    {
        return ShapeUtils.GetWeightedCenter(Vertices, Triangles);
    }

    public Vector3 GetBoundingBoxCenter()
    {
        return ShapeUtils.GetBoundingBoxCenter(Vertices);
    }

    public ReadOnlySpan<Vector3> GetPoints()
    {
        return ShapePoints.Select(p => Vector3.Transform(p, LocalToWorldTRS)).ToArray();
    }

    public virtual Bounds GetAABB()
    {
        return ShapeUtils.GetAABB(this);
    }

    public void SetFillColor(Color color)
    {
        for (int i = 0; i < FillVertexCount; ++i)
        {
            Colors[i] = color.ToVector4();
        }
    }
    public void SetOutlineColor(Color color)
    {
        if (!HasOutline) return;
        for (int i = FillVertexCount; i < Colors.Length; ++i)
        {
            Colors[i] = color.ToVector4();
        }
    }

    public void WithTriangleVisualizationColors(bool includeSubObjects = false)
    {
        var colors = ShapeUtils.GenerateVertexVisualizationColors(
            Vertices.AsSpan().Slice(0, FillVertexCount),
            Triangles.AsSpan().Slice(0, FillTrianglesCount),
            uniqueVertices: HasUniqueVertices);
        colors.CopyTo(Colors.AsMemory());
        foreach(TriangleObject obj in SubObjects)
        {
            obj.WithTriangleVisualizationColors(includeSubObjects);
        }
    }

    public override TriangleObject Clone()
    {
        return new TriangleObject(this);
    }
}
public class TriangleGroup : TriangleObject
{
    public TriangleGroup(IRenderer renderer = null!) : base(renderer)
    {
        Name = "TriangleGroup";
    }
    public override Bounds GetAABB()
    {
        return ShapeUtils.CombineAABB(SubObjects.Where(o => o is IAABB).Cast<IAABB>().Select(o => o.GetAABB()).ToArray());
    }
}
[Obsolete]
public abstract class CompositeTriangle2DObject : TriangleObject
{
    protected List<TriangleObject> Parts { get; init; } = [];
    private Dictionary<TriangleObject, int> PartsToVerticesCount { get; } = [];
    //public override int AddRenderDataToBlock(CGameCtnMediaBlock block)
    //{
    //    int vertexCount = (block as CGameCtnMediaBlockTriangles2D).Vertices.Length;
    //    int idxOffset = 0;
    //    foreach (var part in Parts)
    //    {
    //        part.AddRenderDataToBlock(block);
    //        PartsToVerticesCount[part] = idxOffset;
    //        idxOffset += part.Vertices.Length;
    //    }
    //    return vertexCount;
    //}
    //public override void SetKeyFrameData(CGameCtnMediaBlock block, IKey key, int idx, RenderData renderData, PostProcessingEffectData postProcessingEffectData)
    //{
    //    foreach (var part in Parts) 
    //    {
    //        part.SetParent(this);
    //        part.SetKeyFrameData(block, key, idx + PartsToVerticesCount[part], renderData, postProcessingEffectData);
    //    }
    //}
    public override Bounds GetAABB()
    {
        return ShapeUtils.CombineAABB(Parts.Select(p => p.GetAABB()).ToArray());
    }

}
public class Rectangle : TriangleObject
{
    public float Width { get; init; }
    public float Height { get; init; }

    protected Rectangle(Rectangle other) : base(other)
    {
        Width = other.Width;
        Height = other.Height;
    }
    public Rectangle(float width = 2f,
        float height = 1f,
        Color? fillColor = null,
        Color? outlineColor = null,
        bool withOutline = false,
        bool withFill = true,
        bool filled = true,
        float outlineWidth = 0.1f,
        bool uniqueVertices = false,
        IRenderer renderer = null!) : base(
            points: new Vector3[] { new Vector3(-width / 2f, -height / 2f, 0), new Vector3(width / 2f, -height / 2f, 0), new Vector3(width / 2f, height / 2f, 0), new Vector3(-width / 2f, height / 2f, 0) },
            fillColor: fillColor,
            outlineColor: outlineColor,
            withOutline: withOutline,
            withFill: withFill,
            filled: filled,
            outlineWidth: outlineWidth,
            uniqueVertices: uniqueVertices,
            renderer: renderer)
    {
        Width = width;
        Height = height;

        Name = "Rectangle";
    }

    public override Rectangle Clone()
    {
        return new Rectangle(this);
    }
}
public class Square : Rectangle
{
    public Square(float size = 1f,
        Color? fillColor = null,
        Color? outlineColor = null,
        bool withOutline = false,
        bool withFill = true,
        bool filled = true,
        float outlineWidth = 0.1f,
        bool uniqueVertices = false,
        IRenderer renderer = null!) : base(
            width: size,
            height: size,
            fillColor: fillColor,
            outlineColor: outlineColor,
            withOutline: withOutline,
            withFill: withFill,
            filled: filled,
            outlineWidth: outlineWidth,
            uniqueVertices: uniqueVertices,
            renderer: renderer)
    {
        Name = "Square";
    }
}


public class SquareDot : Square
{
    public SquareDot(Color? color = null, IRenderer renderer = null!) : base(
        size: 0.1f, 
        fillColor: color,
        withOutline: false,
        uniqueVertices: false,
        filled: true,
        renderer: renderer)
    {
        Name = "SquareDot";
    }
}

public class Triangle : TriangleObject
{
    public Triangle(ReadOnlySpan<Vector3> cornerPoints,
        Color? fillColor = null,
        Color? outlineColor = null,
        bool withOutline = false,
        bool withFill = true,
        bool filled = true,
        float outlineWidth = 0.1f,
        OutlineExtendsDirection outlineExtends = OutlineExtendsDirection.Inwards,
        bool uniqueVertices = false,
        IRenderer renderer = null!) : base(
            points: cornerPoints,
            fillColor: fillColor,
            outlineColor: outlineColor,
            withOutline: withOutline,
            withFill: withFill,
            filled: filled,
            outlineWidth: outlineWidth,
            uniqueVertices: uniqueVertices,
            outlineExtends: outlineExtends,
            renderer: renderer)
    {
        Name = "Triangle";
    }

    public Triangle(float width, float height,
        Color? fillColor = null,
        Color? outlineColor = null,
        bool withOutline = false,
        bool withFill = true,
        bool filled = true,
        float outlineWidth = 0.1f,
        OutlineExtendsDirection outlineExtends = OutlineExtendsDirection.Inwards,
        bool uniqueVertices = false,
        IRenderer renderer = null!) : this(cornerPoints: [new Vector3(width / 2f, -height / 2f, 0), new Vector3(0, height / 2f, 0), new Vector3(-width / 2f, -height / 2f, 0)],
            fillColor: fillColor,
            outlineColor: outlineColor,
            withOutline: withOutline,
            withFill: withFill,
            filled: filled,
            outlineWidth: outlineWidth,
            uniqueVertices: uniqueVertices,
            outlineExtends: outlineExtends,
            renderer: renderer)
    { }
    public static Vector3[] GetTrianglePoints(Vector3 origin, float size, Vector3 direction)
    {
        direction = Vector3.Normalize(direction);
        Vector3 right = Vector3.Normalize(Vector3.Cross(direction, Vector3.UnitZ));
        Vector3 p1 = origin;
        Vector3 p2 = origin + direction * size;
        Vector3 mid = (p1 + p2) * 0.5f;
        Vector3 p3 = mid + right * (size * (float)Math.Sqrt(3) / 2); // height of equilateral
        return [p1, p2, p3];
    }
    public static Vector3[] GetTrianglePoints(Vector3 center, float width, float height)
    {
        Vector3 p1 = center + new Vector3(-width / 2, -height / 2, 0);
        Vector3 p2 = center + new Vector3(width / 2, -height / 2, 0);
        Vector3 p3 = center + new Vector3(0, height / 2, 0);
        return new[] { p1, p2, p3 };
    }
    public static Vector3[] GetTrianglePoints(Vector3 baseStart, Vector3 baseEnd, float height)
    {
        Vector3 baseDir = Vector3.Normalize(baseEnd - baseStart);
        Vector3 up = Vector3.Normalize(Vector3.Cross(baseDir, Vector3.UnitZ));
        Vector3 p3 = (baseStart + baseEnd) / 2 + up * height;
        return new[] { baseStart, baseEnd, p3 };
    }
    public static Vector3[] GetTrianglePoints(float angleDeg, float a, float b)
    {
        float angleRad = MathF.PI * angleDeg / 180f;
        Vector3 p1 = Vector3.Zero;
        Vector3 p2 = new Vector3(a, 0, 0);
        float x = b * MathF.Cos(angleRad);
        float y = b * MathF.Sin(angleRad);
        Vector3 p3 = new Vector3(x, y, 0);
        return new[] { p1, p2, p3 };
    }
    public static Vector3[] GetTrianglePoints(CircularArc arc)
    {
        float a1 = arc.Angle * MathF.PI / 180f;
        float a2 = a1 + 2 * MathF.PI / 3;
        float a3 = a1 + 4 * MathF.PI / 3;

        Vector3 p1 = new Vector3(MathF.Cos(a1), MathF.Sin(a1), 0) * arc.Radius;
        Vector3 p2 = new Vector3(MathF.Cos(a2), MathF.Sin(a2), 0) * arc.Radius;
        Vector3 p3 = new Vector3(MathF.Cos(a3), MathF.Sin(a3), 0) * arc.Radius;

        return [p1, p2, p3];
    }
}

public class TestObject : TriangleObject
{
    static Vector3[] testPoints = [
        new Vector3(-1, 1, 0),
        new Vector3(-1, -1, 0),
        new Vector3(1, -1, 0),
        new Vector3(1, 1, 0),
        ];
    public TestObject() : base(testPoints, outlineColor: Color.Magenta, withOutline: true, withFill: true, filled: true, outlineWidth: 0.1f, uniqueVertices: false)
    {
    }
    public void LogStringCoordinates(RenderData renderData)
    {
        StringBuilder sb = new();
        sb.AppendLine($"------ {Name} ------");
        var localTrs = GetLocalTRS();
        sb.AppendLine("LocalTRS:");
        sb.AppendLine(localTrs.ToString());
        sb.AppendLine("LocalToWorldTRS:");
        sb.AppendLine(LocalToWorldTRS.ToString());

        sb.AppendLine("Mesh:");
        for (int i = 0; i < Vertices.Length; ++i)
        {
            sb.AppendLine($"  - {Vertices[i]}");
        }
        sb.AppendLine("LocalSpace:");
        for (int i = 0; i < Vertices.Length; ++i)
        {
           
            var v = Vector3.Transform(Vertices[i], localTrs);
            sb.AppendLine($"  - {v}");
        }
        sb.AppendLine("WorldSpace:");
        for (int i = 0; i < Vertices.Length; ++i)
        {
            var worldTry = LocalToWorldTRS;
            var v = Vector3.Transform(Vertices[i], worldTry);
            sb.AppendLine($"  - {v}");
        }
        sb.Append("------ ---- ------");
        Debug.WriteLine(sb.ToString());
    }
}


public class Line : TriangleObject
{
    public bool IsClosed { get; init; } = false;
    public Line(ReadOnlySpan<Vector3> points,
        Color? color = null,
        float width = 0.1f,
        bool closed = false,
        OutlineExtendsDirection extendsDirection = OutlineExtendsDirection.Bidirectional,
        bool uniqueVertices = false,
        IRenderer renderer = null!) 
        : base(
            points: points,
            outlineColor: color,
            withOutline: true,
            withFill: false,
            outlineExtends: extendsDirection,
            outlineWidth: width,
            uniqueVertices: uniqueVertices,
            renderer: renderer)
    {
        ExceptionUtils.Ensure(points.Length >= 2, () => new ArgumentException("Line must have at least 2 points"));
        ExceptionUtils.Ensure(!closed || (closed && points.Length >= 3), () => new ArgumentException("Closed Line must have at least 3 points"));
        Name = "Line";
        IsClosed = closed;

        ShapePoints = points.ToArray();
        CreateShape(ShapePoints, Color.Black, color ?? Color.Black);
    }
    protected override void CreateOutlineShape(ReadOnlySpan<Vector3> points, Color outlineColor, OutlineExtendsDirection extendsOutwards, bool uniqueVertices, out List<Vector3> vertices, out List<Int3> triangles, out List<Vector4> colors)
    {
        List<Vector3> outlineVertices;
        List<Int3> outlineTriangles;
        if (IsClosed)
        {
            ShapeUtils.GenerateClosedPolyLineOutline(points, OutlineWidth, extendsOutwards, out outlineVertices, out outlineTriangles);
        }
        else
        {
            var boundaryStart = points[0] + (points[0] - points[1]);
            var boundaryEnd = points[^1] + (points[^1] - points[^2]);
            ShapeUtils.GeneratePolyLineOutline(points, boundaryStart, boundaryEnd, OutlineWidth, extendsOutwards, out outlineVertices, out outlineTriangles);
        }

        if (uniqueVertices)
            ShapeUtils.MakeVerticesUnique(outlineVertices.ToArray(), outlineTriangles.ToArray(), out outlineVertices, out outlineTriangles);
        vertices = outlineVertices;
        triangles = outlineTriangles;
        colors = Enumerable.Repeat(outlineColor.ToVector4(), outlineVertices.Count).ToList();
    }

}


public abstract class ArcBase : TriangleObject
{
    public float Angle { get; private init; }
    public int NumComponents { get; private init; }
    private bool filledToCenter;

    private bool IsClosed => MathF.Abs(Angle - (MathF.PI * 2)) < 1e-6f;
    public abstract Vector3 GetShapePointOnArc(float theta);
    public Vector3[] GetPointsOnArcFromShape()
    {
        bool isFullCircle = IsClosed;
        var points = new Vector3[NumComponents];
        float stepAngle = isFullCircle ? Angle / NumComponents : Angle / (NumComponents - 1); // Step per segment
        float startAngle = (MathF.PI - Angle) / 2f;

        for (int i = 0; i < NumComponents; i++)
        {
            float theta = startAngle + i * stepAngle;
            points[i] = GetShapePointOnArc(theta);
        }

        return points;
    }

    protected Vector3 outlineStartBoundaryPoint;
    protected Vector3 outlineEndBoundaryPoint;
    protected void CreateOutlineBoundaryPoints()
    {
        float startAngle = (MathF.PI - Angle) / 2f;
        float stepAngle = Angle / (NumComponents - 1);

        var thetaStart = startAngle - stepAngle;
        outlineStartBoundaryPoint = GetShapePointOnArc(thetaStart);

        var thetaEnd = startAngle + NumComponents * stepAngle;
        outlineEndBoundaryPoint = GetShapePointOnArc(thetaEnd);
    }

    public ArcBase(float angle = MathF.PI,
        int numComponents = 9,
        bool filledToCenter = true,
        Color? fillColor = null,
        Color? outlineColor = null,
        bool withOutline = false,
        bool withFill = true,
        bool filled = true,
        float outlineWidth = 0.1f,
        OutlineExtendsDirection outlineExtends = OutlineExtendsDirection.Bidirectional,
        bool uniqueVertices = false,
        IRenderer renderer = null!) : base(
            withOutline: withOutline,
            withFill: withFill,
            filled: filled,
            outlineExtends: outlineExtends,
            outlineWidth: outlineWidth,
            uniqueVertices: uniqueVertices, 
            renderer: renderer)
    {
        ExceptionUtils.Ensure(numComponents > 1, () => new ArgumentException("NumComponents must be greater than 1"));
        this.filledToCenter = filledToCenter;
        this.Angle = Math.Clamp(angle, 0, MathF.PI * 2f);
        this.NumComponents = numComponents;
    }

    protected ArcBase(ArcBase other) : base(other)
    {
        Angle = other.Angle;
        NumComponents = other.NumComponents;
        filledToCenter = other.filledToCenter;
        outlineStartBoundaryPoint = other.outlineStartBoundaryPoint;
        outlineEndBoundaryPoint = other.outlineEndBoundaryPoint;
    }

    protected override void CreateFilledShape(ReadOnlySpan<Vector3> points, Color fillColor, bool uniqueVertices, out List<Vector3> vertices, out List<Int3> triangles, out List<Vector4> colors)
    {
        List<Vector3> pointsList = points.ToArray().ToList();
        if (filledToCenter && !IsClosed)
        {
            pointsList.Add(new Vector3(0, 0, 0));
        }
        base.CreateFilledShape(pointsList.ToArray(), fillColor, uniqueVertices, out vertices, out triangles, out colors);
    }
    protected override void CreateOutlineShape(ReadOnlySpan<Vector3> points, Color outlineColor, OutlineExtendsDirection extendsOutwards, bool uniqueVertices, out List<Vector3> vertices, out List<Int3> triangles, out List<Vector4> colors)
    {
        List<Vector3> outlineVertices;
        List<Int3> outlineTriangles;
        if (filledToCenter)
        {
            ShapeUtils.GenerateClosedPolyLineOutline(points, OutlineWidth, extendsOutwards, out outlineVertices, out outlineTriangles);
        }
        else
        {
            ShapeUtils.GeneratePolyLineOutline(points, outlineStartBoundaryPoint, outlineEndBoundaryPoint, OutlineWidth, extendsOutwards, out outlineVertices, out outlineTriangles);
        }

        if (uniqueVertices)
            ShapeUtils.MakeVerticesUnique(outlineVertices.ToArray(), outlineTriangles.ToArray(), out outlineVertices, out outlineTriangles);
        vertices = outlineVertices;
        triangles = outlineTriangles;
        colors = Enumerable.Repeat(outlineColor.ToVector4(), outlineVertices.Count).ToList();
    }
}
public class CircularArc : ArcBase
{
    public float Radius { get; private init; }

    public CircularArc(float radius = 1f,
        float angle = MathF.PI,
        int numComponents = 9,
        bool filledToCenter = true,
        Color? fillColor = null,
        Color? outlineColor = null,
        bool withOutline = false,
        bool withFill = true,
        bool filled = true,
        float outlineWidth = 0.1f,
        OutlineExtendsDirection outlineExtends = OutlineExtendsDirection.Bidirectional,
        bool uniqueVertices = false,
        IRenderer renderer = null!) : base(
            angle: angle,
            numComponents: numComponents,
            filledToCenter: filledToCenter,
            fillColor: fillColor,
            outlineColor: outlineColor,
            withOutline: withOutline,
            withFill: withFill,
            filled: filled,
            outlineExtends: outlineExtends,
            outlineWidth: outlineWidth,
            uniqueVertices: uniqueVertices,
            renderer: renderer)
    {
        Radius = radius;

        ShapePoints = GetPointsOnArcFromShape();
        if (withOutline)
            CreateOutlineBoundaryPoints();
        CreateShape(ShapePoints, fillColor ?? Color.Black, outlineColor ?? Color.Black);
        SetFilled(filled);

        Name = "Circular Arc";
    }

    protected CircularArc(CircularArc other) : base(other)
    {
        Radius = other.Radius;
    }
    public override CircularArc Clone()
    {
        return new CircularArc(this);
    }

    public override Vector3 GetShapePointOnArc(float theta)
    {
        return GetShapePointOnArc(Radius, theta);
    }
    public static Vector3 GetShapePointOnArc(float radius, float theta)
        => new Vector3(MathF.Cos(theta), MathF.Sin(theta), 0) * radius;
}
public class Circle : CircularArc
{
    public Circle(float radius = 1f,
        int numComponents = 16,
        Color? fillColor = null,
        Color? outlineColor = null,
        bool withOutline = false,
        bool withFill = true,
        bool filled = true,
        OutlineExtendsDirection outlineExtends = OutlineExtendsDirection.Bidirectional,
        float outlineWidth = 0.1f,
        bool uniqueVertices = false,
        IRenderer renderer = null!) : base(
            radius: radius,
            angle: MathF.PI * 2f,
            numComponents: numComponents,
            filledToCenter: true,
            fillColor: fillColor,
            outlineColor: outlineColor,
            withOutline: withOutline,
            withFill: withFill,
            filled: filled,
            outlineExtends: outlineExtends,
            outlineWidth: outlineWidth,
            uniqueVertices: uniqueVertices,
            renderer: renderer)
    {
        Name = "Circle";
    }

    //protected override void CreateOutlineShape(ReadOnlySpan<Vector3> points, Color outlineColor, OutlineExtendsDirection extendsOutwards, bool uniqueVertices, out List<Vector3> vertices, out List<Int3> triangles, out List<Vector4> colors)
    //{
    //    ShapeUtils.GenerateClosedPolygonOutline(points, OutlineWidth, extendsOutwards, out var outlineVertices, out var outlineTriangles);
    //    if (uniqueVertices)
    //        ShapeUtils.MakeVerticesUnique(outlineVertices.ToArray(), outlineTriangles.ToArray(), out outlineVertices, out outlineTriangles);
    //    vertices = outlineVertices;
    //    triangles = outlineTriangles;
    //    colors = Enumerable.Repeat(outlineColor.ToVector4(), outlineVertices.Count).ToList();
    //}

}

public class EllipticalArc : ArcBase
{
    public float RadiusX { get; private init; }
    public float RadiusY { get; private init; }
    public EllipticalArc(float radiusX = 2f,
        float radiusY = 1f,
        float angle = MathF.PI,
        int numComponents = 9,
        bool filledToCenter = true,
        Color? fillColor = null,
        Color? outlineColor = null,
        bool withOutline = false,
        bool withFill = true,
        bool filled = true,
        float outlineWidth = 0.1f,
        OutlineExtendsDirection outlineExtends = OutlineExtendsDirection.Bidirectional,
        bool uniqueVertices = false,
        IRenderer renderer = null!) : base(
            angle: angle,
            numComponents: numComponents,
            filledToCenter: filledToCenter,
            fillColor: fillColor,
            outlineColor: outlineColor,
            withOutline: withOutline,
            withFill: withFill,
            filled: filled,
            outlineExtends: outlineExtends,
            outlineWidth: outlineWidth,
            uniqueVertices: uniqueVertices, 
            renderer: renderer)
    {
        RadiusX = radiusX;
        RadiusY = radiusY;

        ShapePoints = GetPointsOnArcFromShape();
        if (withOutline)
            CreateOutlineBoundaryPoints();
        CreateShape(ShapePoints, fillColor ?? Color.Black, outlineColor ?? Color.Black);
        SetFilled(filled);

        Name = "Elliptical Arc";
    }

    protected EllipticalArc(EllipticalArc other) : base(other)
    {
        RadiusX = other.RadiusX;
        RadiusY = other.RadiusY;
    }
    public override EllipticalArc Clone()
    {
        return new EllipticalArc(this);
    }

    public override Vector3 GetShapePointOnArc(float theta)
        => GetShapePointOnArc(RadiusX, RadiusY, theta);

    public static Vector3 GetShapePointOnArc(float radiusX, float radiusY, float theta)
     => new Vector3(radiusX * MathF.Cos(theta), radiusY * MathF.Sin(theta), 0);
}
public class Ellipse : EllipticalArc
{
    public Ellipse(float radiusX = 2f,
       float radiusY = 1f,
       int numComponents = 16,
       Color? fillColor = null,
       Color? outlineColor = null,
       bool withOutline = false,
       bool withFill = true,
       bool filled = true,
       float outlineWidth = 0.1f,
       OutlineExtendsDirection outlineExtends = OutlineExtendsDirection.Bidirectional,
       bool uniqueVertices = false, 
       IRenderer renderer = null!) : base(
           radiusX: radiusX,
           radiusY: radiusY,
           angle: MathF.PI * 2f,
           numComponents: numComponents,
           filledToCenter: true,
           fillColor: fillColor,
           outlineColor: outlineColor,
           withOutline: withOutline,
           withFill: withFill,
           filled: filled,
           outlineExtends: outlineExtends,
           outlineWidth: outlineWidth,
           uniqueVertices: uniqueVertices,
           renderer: renderer)
    {
        Name = "Ellipse";
    }
}

