using DirectXTexNet;
using System.Drawing;
using System.Runtime.InteropServices;
using static GBX.NET.Engines.Plug.CPlugMaterialUserInst;

namespace TM_GenericMapping.Materials;

public class DDSTexture : IDisposable
{
    private ScratchImage image = null!;
    private TexMetadata metadata = null!;
    private byte[] pixelData = [];
    private int width;
    private int height;

    public Color BorderColor { get; set; } = Color.Transparent; // TODO

    public DDSTexture(string path)
    {
        image = TexHelper.Instance.LoadFromDDSFile(path, DDS_FLAGS.NONE);
        Initialize();
    }

    public DDSTexture(Stream stream)
    {
        // Load stream into byte array
        byte[] data;
        if (stream is MemoryStream ms && ms.TryGetBuffer(out var buffer))
        {
            data = buffer.Array;
        }
        else
        {
            using var memStream = new MemoryStream();
            stream.CopyTo(memStream);
            data = memStream.ToArray();
        }

        // Pin the array and get pointer
        GCHandle handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            IntPtr ptr = handle.AddrOfPinnedObject();
            image = TexHelper.Instance.LoadFromDDSMemory(ptr, data.Length, DDS_FLAGS.NONE);
            Initialize();
        }
        finally
        {
            handle.Free();
        }
    }

    private void Initialize()
    {
        metadata = image.GetMetadata();
        width = metadata.Width;
        height = metadata.Height;

        // Decompress if needed
        if (TexHelper.Instance.IsCompressed(metadata.Format))
        {
            image = image.Decompress(DXGI_FORMAT.R8G8B8A8_UNORM);
            metadata = image.GetMetadata();
        }

        // Convert to RGBA if needed
        if (metadata.Format != DXGI_FORMAT.R8G8B8A8_UNORM)
        {
            image = image.Convert(DXGI_FORMAT.R8G8B8A8_UNORM, TEX_FILTER_FLAGS.DEFAULT, 0.5f);
            metadata = image.GetMetadata();
        }

        // Get pixel data
        unsafe
        {
            var img = image.GetImage(0, 0, 0);
            pixelData = new byte[img.SlicePitch];
            Marshal.Copy(img.Pixels, pixelData, 0, pixelData.Length);
        }
    }
    public Color Sample(float u, float v, float textureSizeInMeters,
                   ETexAddress tilingU = ETexAddress.Clamp,
                   ETexAddress tilingV = ETexAddress.Clamp)
    {
        // Convert world coordinates to UV space
        u = u / textureSizeInMeters;
        v = v / textureSizeInMeters;

        return Sample(u, v, tilingU, tilingV);
    }
    public Color Sample(float u, float v, ETexAddress tilingU = ETexAddress.Clamp, ETexAddress tilingV = ETexAddress.Clamp)
    {
        u = ApplyTiling(u, tilingU);
        v = ApplyTiling(v, tilingV);

        // Border mode returns -1 when out of bounds
        if (u < 0 || v < 0)
            return BorderColor;

        int x = Math.Clamp((int)(u * width), 0, width - 1);
        int y = Math.Clamp((int)(v * height), 0, height - 1);

        int index = (y * width + x) * 4;
        return Color.FromArgb(
            pixelData[index + 3],
            pixelData[index + 0],
            pixelData[index + 1],
            pixelData[index + 2]
        );
    }

    public Color SampleBilinear(float u, float v, ETexAddress tilingU = ETexAddress.Wrap, ETexAddress tilingV = ETexAddress.Wrap)
    {
        u = ApplyTiling(u, tilingU);
        v = ApplyTiling(v, tilingV);

        if (u < 0 || v < 0)
            return BorderColor;

        float x = u * (width - 1);
        float y = v * (height - 1);

        int x0 = (int)Math.Floor(x);
        int y0 = (int)Math.Floor(y);
        int x1 = Math.Min(x0 + 1, width - 1);
        int y1 = Math.Min(y0 + 1, height - 1);

        float fx = x - x0;
        float fy = y - y0;

        Color c00 = GetPixel(x0, y0);
        Color c10 = GetPixel(x1, y0);
        Color c01 = GetPixel(x0, y1);
        Color c11 = GetPixel(x1, y1);

        return Lerp(Lerp(c00, c10, fx), Lerp(c01, c11, fx), fy);
    }
    private float ApplyTiling(float coord, ETexAddress mode)
    {
        switch (mode)
        {
            case ETexAddress.Wrap:
                return coord - MathF.Floor(coord); // fract(coord)
            case ETexAddress.Clamp:
                return Math.Clamp(coord, 0f, 1f);
            case ETexAddress.Border:
                return (coord >= 0f && coord <= 1f) ? coord : -1f;
            case ETexAddress.Mirror:
                {
                    float t = coord - MathF.Floor(coord);
                    int tile = (int)MathF.Floor(coord);
                    return (tile % 2 == 0) ? t : 1 - t;
                }
            default: return 0;
        }
    }

    private Color GetPixel(int x, int y)
    {
        int index = (y * width + x) * 4;
        return Color.FromArgb(
            pixelData[index + 3],
            pixelData[index + 0],
            pixelData[index + 1],
            pixelData[index + 2]
        );
    }

    private Color Lerp(Color a, Color b, float t)
    {
        return Color.FromArgb(
            (int)(a.A + (b.A - a.A) * t),
            (int)(a.R + (b.R - a.R) * t),
            (int)(a.G + (b.G - a.G) * t),
            (int)(a.B + (b.B - a.B) * t)
        );
    }

    public void Dispose()
    {
        image?.Dispose();
    }
}
