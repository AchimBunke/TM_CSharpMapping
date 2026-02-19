using GBX.NET;
using System.Numerics;
using TM_GenericMapping.Common;
using Color = System.Drawing.Color;

namespace TM_GenericMapping.Common;

public enum Anchor
{
    Left,
    Center, 
    Right
}
public class DotMatrixDisplay : TriangleObject
{


    const int VerticesPerCharacter = 4 * 5 * 7; // 4 vertices per dot, 5 columns, 7 rows
    const int TrianglesPerCharacter = 2 * 5 * 7; // 2 triangles per dot, 5 columns, 7 rows
    const float DefaultSize = 0.5f;
    const float DefaultSpacing = 0.05f;

    public string Text { get; init; } = string.Empty;
    public int CharacterCount => Text.Length;
    public float Size { get; init; }
    public float Spacing { get; init; }
    public Anchor Anchor { get; init; }

    Color color;

    public DotMatrixCharacter[] MatrixCharacters { get; init; }

    public override void SetFilled(bool value)
    {
        if (IsFilled == value)
            return;
        foreach (var mc in MatrixCharacters)
        {
            if(value)
                mc.FillCharacter();
            else
                mc.ClearCharacter();
        }
        IsFilled = value;
    }
    public DotMatrixDisplay(ReadOnlySpan<char> text, float size = DefaultSize, float spacing = DefaultSpacing, Color? color = null, bool filled = true,
        Anchor anchor = Anchor.Left, IRenderer renderer = null!)
        : base(withOutline: false, withFill: true, filled: filled, uniqueVertices: true, renderer: renderer)
    {
        Anchor = anchor;
        Text = text.ToString();
        Size = size;
        Spacing = spacing;
        MatrixCharacters = new DotMatrixCharacter[CharacterCount];
        this.color = color ?? Color.Black;
        GenerateTriangleData();

        if (filled)
        {
            foreach (var matChar in MatrixCharacters)
            {
                matChar.FillCharacter();
            }
        }
    }
    protected DotMatrixDisplay(DotMatrixDisplay other) : base(other)
    {
        Text = other.Text;
        Size = other.Size;
        Spacing = other.Spacing;
        color = other.color;
        Anchor = other.Anchor;
        MatrixCharacters = new DotMatrixCharacter[other.CharacterCount];

        GenerateTriangleData();

        if (other.IsFilled)
        {
            foreach (var matChar in MatrixCharacters)
            {
                matChar.FillCharacter();
            }
        }
    }
    void GenerateTriangleData()
    {
        Vertices = new Vector3[CharacterCount * VerticesPerCharacter];
        Triangles = new Int3[CharacterCount * TrianglesPerCharacter];
        Colors = Enumerable.Repeat(color.ToVector4(), CharacterCount * VerticesPerCharacter).ToArray();
        FillVertexCount = Vertices.Length;
        FillTrianglesCount = Triangles.Length;

        GenerateMatrixCharacters();
    }
    void GenerateMatrixCharacters()
    {
        Vector3 anchorOffset = Vector3.Zero;
        Vector3 displayBounds = new Vector3(Text.Length * (Size + Spacing), Size, 0);
        switch (Anchor)
        {
            case Anchor.Left:
                anchorOffset = new Vector3(0, 0, 0);
                break;
            case Anchor.Center:
                anchorOffset = new Vector3(-displayBounds.X / 2f, 0, 0);
                break;
            case Anchor.Right:
                anchorOffset = new Vector3(-displayBounds.X, 0, 0);
                break;
        }
        for (int i = 0; i < Text.Length; ++i)
        {
            var c = Text[i];
            var characterPosition = anchorOffset + new Vector3(i * Size + i * Spacing, 0, 0);
            var matrixCharacter = new DotMatrixCharacter(c, i, characterPosition, Size,
                Vertices.AsMemory(i * VerticesPerCharacter, VerticesPerCharacter),
                Triangles.AsMemory(i * TrianglesPerCharacter, TrianglesPerCharacter),
                Colors.AsMemory(i * VerticesPerCharacter, VerticesPerCharacter));
            MatrixCharacters[i] = matrixCharacter;
        }
    }
    public override DotMatrixDisplay Clone()
    {
        return new DotMatrixDisplay(this);
    }

    public class DotMatrixCharacter : IMorphable
    {
        Vector3 position;
        float size;
        char character;
        public char Character
        {
            get => character;
            set
            {
                if (character == value)
                    return;
                character = value;
                matrix = GetDotMatrix(character);
                FillCharacter();
            }
        }

        public Memory<Vector3> Vertices => vertices;

        public Memory<Int3> Triangles => triangles;

        public int FillVertexCount => vertices.Length;

        public int FillTrianglesCount => triangles.Length;

        public int VertexIdxOffset => vertexOffset;

        int[,] matrix;
        Memory<Vector3> vertices;
        Memory<Int3> triangles;
        int vertexOffset;
        int characterIdx;
        Memory<Vector4> colors;
        public DotMatrixDot[,] Dots { get; init; }

        public IEnumerable<IMorphable> SubObjects => Enumerable.Empty<IMorphable>();

        public DotMatrixCharacter(char character,
            int characterIdx,
            Vector3 characterPosition, 
            float characterSize,
            Memory<Vector3> vertices,
            Memory<Int3> triangles,
            Memory<Vector4> colors)
        {
            matrix = GetDotMatrix(character);
            this.size = characterSize;
            this.position = characterPosition;
            this.character = character;
            this.vertices = vertices;
            this.triangles = triangles;
            this.vertexOffset = characterIdx * VerticesPerCharacter;
            this.characterIdx = characterIdx;
            this.colors = colors;
            Dots = new DotMatrixDot[5,7];

            int i = 0;
            for (int x = 0; x < 5; ++x)
            {
                for (int y = 0; y < 7; ++y)
                {
                    var dotPosition = new Vector3(x * size / 5f, y * size / 7f, 0) + position;
                    var dot = new DotMatrixDot(dotPosition,
                        new Vector3(size / 5f, size / 7f, 0),
                        vertexOffset + i * 4,
                        vertices.Slice(i * 4, 4),
                        triangles.Slice(i * 2, 2),
                        colors.Slice(i * 4, 4));
                    Dots[x , y] = dot;
                    ++i;
                }
            }
        }

        public void FillCharacter()
        {
            for (int x = 0; x < 5; ++x)
            {
                for (int y = 0; y < 7; ++y)
                {
                    if (matrix[x, y] == 1)
                    {
                        Dots[x, y].FillDot();
                    }
                    else
                    {
                        Dots[x, y].ClearDot();
                    }
                }
            }
        }
        public void ClearCharacter()
        {
            for (int x = 0; x < 5; ++x)
            {
                for (int y = 0; y < 7; ++y)
                {
                    Dots[x, y].ClearDot();
                }
            }
        }

        public bool CanMorph()
        {
            return true;
        }

        public DotMatrixCharacter CreateCharacter(char character)
        {
            return new DotMatrixCharacter(character, 0, position, size, new Vector3[VerticesPerCharacter], new Int3[TrianglesPerCharacter], new Vector4[VerticesPerCharacter]);
        }

    }
    public class DotMatrixDot : IMorphable
    {
        Vector3 position;
        Vector3 dotExtends;
        int vertexOffset;
        Memory<Vector3> vertices;
        Memory<Int3> triangles;
        Memory<Vector4> colors;

        public Memory<Vector3> Vertices => vertices;

        public Memory<Int3> Triangles => triangles;

        public int FillVertexCount => vertices.Length;

        public int FillTrianglesCount => triangles.Length;

        public int VertexIdxOffset => vertexOffset;

        public IEnumerable<IMorphable> SubObjects => Enumerable.Empty<IMorphable>();

        public void FillDot()
        {
            var span = vertices.Span;
            span[0] = position + new Vector3(0, 0, 0);
            span[1] = position + new Vector3(0, -dotExtends.Y, 0);
            span[2] = position + new Vector3(dotExtends.X, 0, 0);
            span[3] = position + new Vector3(dotExtends.X, -dotExtends.Y, 0);
        }
        public void ClearDot()
        {
            var span = vertices.Span;
            span[0] = position;
            span[1] = position;
            span[2] = position;
            span[3] = position;
        }

        public bool CanMorph()
        {
            return true;
        }

        public DotMatrixDot(Vector3 position, Vector3 dotExtends, int vertexOffset, Memory<Vector3> vertices, Memory<Int3> triangles, Memory<Vector4> colors)
        {
            this.position = position;
            this.dotExtends = dotExtends;
            this.vertices = vertices;
            this.triangles = triangles;
            this.colors = colors;
            this.vertexOffset = vertexOffset;

            var trianglesSpan = triangles.Span;
            trianglesSpan[0] = new Int3(vertexOffset + 0, vertexOffset + 1, vertexOffset + 2);
            trianglesSpan[1] = new Int3(vertexOffset + 2, vertexOffset + 1, vertexOffset + 3);
            ClearDot();
        }
    }

    public new DotMatrixDisplayAnimator<DotMatrixDisplay> Animate(bool continuosKeyFrames = false, ulong keyframeGenerationRateMillis = 0)
     => new DotMatrixDisplayAnimator<DotMatrixDisplay>(this) { ContinuosKeyFrames = continuosKeyFrames, KeyframeGenerationRateMillis = keyframeGenerationRateMillis };



    public static Dictionary<char, byte[]> font5x7 = new Dictionary<char, byte[]>
    {
  { '@', new byte[] { 0x0e, 0x11, 0x17, 0x15, 0x17, 0x10, 0x0f } },   // 0x40, @
    { 'A', new byte[] { 0x04, 0x0a, 0x11, 0x11, 0x1f, 0x11, 0x11 } },   // 0x41, A
    { 'B', new byte[] { 0x1e, 0x11, 0x11, 0x1e, 0x11, 0x11, 0x1e } },   // 0x42, B
    { 'C', new byte[] { 0x0e, 0x11, 0x10, 0x10, 0x10, 0x11, 0x0e } },   // 0x43, C
    { 'D', new byte[] { 0x1e, 0x09, 0x09, 0x09, 0x09, 0x09, 0x1e } },   // 0x44, D
    { 'E', new byte[] { 0x1f, 0x10, 0x10, 0x1c, 0x10, 0x10, 0x1f } },   // 0x45, E
    { 'F', new byte[] { 0x1f, 0x10, 0x10, 0x1f, 0x10, 0x10, 0x10 } },   // 0x46, F
    { 'G', new byte[] { 0x0e, 0x11, 0x10, 0x10, 0x13, 0x11, 0x0f } },   // 0x37, G
    { 'H', new byte[] { 0x11, 0x11, 0x11, 0x1f, 0x11, 0x11, 0x11 } },   // 0x48, H
    { 'I', new byte[] { 0x0e, 0x04, 0x04, 0x04, 0x04, 0x04, 0x0e } },   // 0x49, I
    { 'J', new byte[] { 0x1f, 0x02, 0x02, 0x02, 0x02, 0x12, 0x0c } },   // 0x4a, J
    { 'K', new byte[] { 0x11, 0x12, 0x14, 0x18, 0x14, 0x12, 0x11 } },   // 0x4b, K
    { 'L', new byte[] { 0x10, 0x10, 0x10, 0x10, 0x10, 0x10, 0x1f } },   // 0x4c, L
    { 'M', new byte[] { 0x11, 0x1b, 0x15, 0x11, 0x11, 0x11, 0x11 } },   // 0x4d, M
    { 'N', new byte[] { 0x11, 0x11, 0x19, 0x15, 0x13, 0x11, 0x11 } },   // 0x4e, N
    { 'O', new byte[] { 0x0e, 0x11, 0x11, 0x11, 0x11, 0x11, 0x0e } },   // 0x4f, O
    { 'P', new byte[] { 0x1e, 0x11, 0x11, 0x1e, 0x10, 0x10, 0x10 } },   // 0x50, P
    { 'Q', new byte[] { 0x0e, 0x11, 0x11, 0x11, 0x15, 0x12, 0x0d } },   // 0x51, Q
    { 'R', new byte[] { 0x1e, 0x11, 0x11, 0x1e, 0x14, 0x12, 0x11 } },   // 0x52, R
    { 'S', new byte[] { 0x0e, 0x11, 0x10, 0x0e, 0x01, 0x11, 0x0e } },   // 0x53, S
    { 'T', new byte[] { 0x1f, 0x04, 0x04, 0x04, 0x04, 0x04, 0x04 } },   // 0x54, T
    { 'U', new byte[] { 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x0e } },   // 0x55, U
    { 'V', new byte[] { 0x11, 0x11, 0x11, 0x11, 0x11, 0x0a, 0x04 } },   // 0x56, V
    { 'W', new byte[] { 0x11, 0x11, 0x11, 0x15, 0x15, 0x1b, 0x11 } },   // 0x57, W
    { 'X', new byte[] { 0x11, 0x11, 0x0a, 0x04, 0x0a, 0x11, 0x11 } },   // 0x58, X
    { 'Y', new byte[] { 0x11, 0x11, 0x0a, 0x04, 0x04, 0x04, 0x04 } },   // 0x59, Y
    { 'Z', new byte[] { 0x1f, 0x01, 0x02, 0x04, 0x08, 0x10, 0x1f } },   // 0x5a, Z
    { '[', new byte[] { 0x0e, 0x08, 0x08, 0x08, 0x08, 0x08, 0x0e } },   // 0x5b, [
    { '\\', new byte[] { 0x10, 0x10, 0x08, 0x04, 0x02, 0x01, 0x01 } },   // 0x5c, \
    { ']', new byte[] { 0x0e, 0x02, 0x02, 0x02, 0x02, 0x02, 0x0e } },   // 0x5d, ]
    { '^', new byte[] { 0x04, 0x0a, 0x11, 0x00, 0x00, 0x00, 0x00 } },   // 0x5e, ^
    { '_', new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x1f } },    // 0x5f, _

          { '`', new byte[] { 0x04, 0x04, 0x02, 0x00, 0x00, 0x00, 0x00 } },   // 0x60, `
    { 'a', new byte[] { 0x00, 0x0e, 0x01, 0x0d, 0x13, 0x13, 0x0d } },   // 0x61, a
    { 'b', new byte[] { 0x10, 0x10, 0x10, 0x1c, 0x12, 0x12, 0x1c } },   // 0x62, b
    { 'c', new byte[] { 0x00, 0x00, 0x00, 0x0e, 0x10, 0x10, 0x0e } },   // 0x63, c
    { 'd', new byte[] { 0x01, 0x01, 0x01, 0x07, 0x09, 0x09, 0x07 } },   // 0x64, d
    { 'e', new byte[] { 0x00, 0x00, 0x0e, 0x11, 0x1f, 0x10, 0x0f } },   // 0x65, e
    { 'f', new byte[] { 0x06, 0x09, 0x08, 0x1c, 0x08, 0x08, 0x08 } },   // 0x66, f
    { 'g', new byte[] { 0x0e, 0x11, 0x13, 0x0d, 0x01, 0x01, 0x0e } },   // 0x67, g
    { 'h', new byte[] { 0x10, 0x10, 0x10, 0x16, 0x19, 0x11, 0x11 } },   // 0x68, h
    { 'i', new byte[] { 0x00, 0x04, 0x00, 0x0c, 0x04, 0x04, 0x0e } },   // 0x69, i
    { 'j', new byte[] { 0x02, 0x00, 0x06, 0x02, 0x02, 0x12, 0x0c } },   // 0x6a, j
    { 'k', new byte[] { 0x10, 0x10, 0x12, 0x14, 0x18, 0x14, 0x12 } },   // 0x6b, k
    { 'l', new byte[] { 0x0c, 0x04, 0x04, 0x04, 0x04, 0x04, 0x04 } },   // 0x6c, l
    { 'm', new byte[] { 0x00, 0x00, 0x0a, 0x15, 0x15, 0x11, 0x11 } },   // 0x6d, m
    { 'n', new byte[] { 0x00, 0x00, 0x16, 0x19, 0x11, 0x11, 0x11 } },   // 0x6e, n
    { 'o', new byte[] { 0x00, 0x00, 0x0e, 0x11, 0x11, 0x11, 0x0e } },   // 0x6f, o
    { 'p', new byte[] { 0x00, 0x1c, 0x12, 0x12, 0x1c, 0x10, 0x10 } },   // 0x70, p
    { 'q', new byte[] { 0x00, 0x07, 0x09, 0x09, 0x07, 0x01, 0x01 } },   // 0x71, q
    { 'r', new byte[] { 0x00, 0x00, 0x16, 0x19, 0x10, 0x10, 0x10 } },   // 0x72, r
    { 's', new byte[] { 0x00, 0x00, 0x0f, 0x10, 0x0e, 0x01, 0x1e } },   // 0x73, s
    { 't', new byte[] { 0x08, 0x08, 0x1c, 0x08, 0x08, 0x09, 0x06 } },   // 0x74, t
    { 'u', new byte[] { 0x00, 0x00, 0x11, 0x11, 0x11, 0x13, 0x0d } },   // 0x75, u
    { 'v', new byte[] { 0x00, 0x00, 0x11, 0x11, 0x11, 0x0a, 0x04 } },   // 0x76, v
    { 'w', new byte[] { 0x00, 0x00, 0x11, 0x11, 0x15, 0x15, 0x0a } },   // 0x77, w
    { 'x', new byte[] { 0x00, 0x00, 0x11, 0x0a, 0x04, 0x0a, 0x11 } },   // 0x78, x
    { 'y', new byte[] { 0x00, 0x11, 0x11, 0x0f, 0x01, 0x11, 0x0e } },   // 0x79, y
    { 'z', new byte[] { 0x00, 0x00, 0x1f, 0x02, 0x04, 0x08, 0x1f } },   // 0x7a, z
    { '{', new byte[] { 0x06, 0x08, 0x08, 0x10, 0x08, 0x08, 0x06 } },   // 0x7b, {
    { '|', new byte[] { 0x04, 0x04, 0x04, 0x00, 0x04, 0x04, 0x04 } },   // 0x7c, |
    { '}', new byte[] { 0x0c, 0x02, 0x02, 0x01, 0x02, 0x02, 0x0c } },   // 0x7d, }
    { '~', new byte[] { 0x08, 0x15, 0x02, 0x00, 0x00, 0x00, 0x00 } },    // 0x7e, ~

        { ' ', new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 } },   // 0x20, Space
    { '!', new byte[] { 0x04, 0x04, 0x04, 0x04, 0x04, 0x00, 0x04 } },       // 0x21, !
    { '\"', new byte[] { 0x09, 0x09, 0x12, 0x00, 0x00, 0x00, 0x00 } },       // 0x22, "
    { '#', new byte[] { 0x0a, 0x0a, 0x1f, 0x0a, 0x1f, 0x0a, 0x0a } },       // 0x23, #
    { '$', new byte[] { 0x04, 0x0f, 0x14, 0x0e, 0x05, 0x1e, 0x04 } },       // 0x24, $
    { '%', new byte[] { 0x19, 0x19, 0x02, 0x04, 0x08, 0x13, 0x13 } },       // 0x25, %
    { '&', new byte[] { 0x04, 0x0a, 0x0a, 0x0a, 0x15, 0x12, 0x0d } },       // 0x26, &
    { '\'', new byte[] { 0x04, 0x04, 0x08, 0x00, 0x00, 0x00, 0x00 } },       // 0x27, '
    { '(', new byte[] { 0x02, 0x04, 0x08, 0x08, 0x08, 0x04, 0x02 } },       // 0x28, (
    { ')', new byte[] { 0x08, 0x04, 0x02, 0x02, 0x02, 0x04, 0x08 } },       // 0x29, )
    { '*', new byte[] { 0x04, 0x15, 0x0e, 0x1f, 0x0e, 0x15, 0x04 } },       // 0x2a, *
    { '+', new byte[] { 0x00, 0x04, 0x04, 0x1f, 0x04, 0x04, 0x00 } },       // 0x2b, +
    { ',', new byte[] { 0x00, 0x00, 0x00, 0x00, 0x04, 0x04, 0x08 } },       // 0x2c, ,
    { '-', new byte[] { 0x00, 0x00, 0x00, 0x1f, 0x00, 0x00, 0x00 } },       // 0x2d, -
    { '.', new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x0c, 0x0c } },       // 0x2e, .
    { '/', new byte[] { 0x01, 0x01, 0x02, 0x04, 0x08, 0x10, 0x10 } },       // 0x2f, /
    { '0', new byte[] { 0x0e, 0x11, 0x13, 0x15, 0x19, 0x11, 0x0e } },       // 0x30, 0
    { '1', new byte[] { 0x04, 0x0c, 0x04, 0x04, 0x04, 0x04, 0x0e } },       // 0x31, 1
    { '2', new byte[] { 0x0e, 0x11, 0x01, 0x02, 0x04, 0x08, 0x1f } },       // 0x32, 2
    { '3', new byte[] { 0x0e, 0x11, 0x01, 0x06, 0x01, 0x11, 0x0e } },       // 0x33, 3
    { '4', new byte[] { 0x02, 0x06, 0x0a, 0x12, 0x1f, 0x02, 0x02 } },       // 0x34, 4
    { '5', new byte[] { 0x1f, 0x10, 0x1e, 0x01, 0x01, 0x11, 0x0e } },       // 0x35, 5
    { '6', new byte[] { 0x06, 0x08, 0x10, 0x1e, 0x11, 0x11, 0x0e } },       // 0x36, 6
    { '7', new byte[] { 0x1f, 0x01, 0x02, 0x04, 0x08, 0x08, 0x08 } },       // 0x37, 7
    { '8', new byte[] { 0x0e, 0x11, 0x11, 0x0e, 0x11, 0x11, 0x0e } },       // 0x38, 8
    { '9', new byte[] { 0x0e, 0x11, 0x11, 0x0f, 0x01, 0x02, 0x0c } },       // 0x39, 9
    { ':', new byte[] { 0x00, 0x0c, 0x0c, 0x00, 0x0c, 0x0c, 0x00 } },       // 0x3a, :
    { ';', new byte[] { 0x00, 0x0c, 0x0c, 0x00, 0x0c, 0x04, 0x08 } },       // 0x3b, ;
    { '<', new byte[] { 0x02, 0x04, 0x08, 0x10, 0x08, 0x04, 0x02 } },       // 0x3c, <
    { '=', new byte[] { 0x00, 0x00, 0x1f, 0x00, 0x1f, 0x00, 0x00 } },       // 0x3d, =
    { '>', new byte[] { 0x08, 0x04, 0x02, 0x01, 0x02, 0x04, 0x08 } },       // 0x3e, >
    { '?', new byte[] { 0x0e, 0x11, 0x01, 0x02, 0x04, 0x00, 0x04 } }        // 0x3f, ?

    };

    static int[,] GetDotMatrix(char c)
    {
        if (!font5x7.ContainsKey(c))
            throw new ArgumentException("Unsupported character");

        byte[] data = font5x7[c];
        int[,] matrix = new int[5, 7]; // [col, row]

        for (int row = 0; row < 7; row++)
        {
            byte b = data[row];
            for (int col = 0; col < 5; col++)
            {
                matrix[4 - col, 6 - row] = (b >> col) & 1;
            }
        }


        return matrix;
    }

    public void Orientate3D()
    {
        LocalRotation = Quaternion.CreateFromAxisAngle(new Vector3(0, 1, 0), MathF.PI);
    }

}
