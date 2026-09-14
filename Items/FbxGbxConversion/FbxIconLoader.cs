using GBX.NET.Engines.GameData;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace TM_GenericMapping.Items.FbxGbxConversion;

internal class FbxIconLoader
{

    public static GBX.NET.Color[,]? LoadIcon(FbxGbxConversionInput config)
    {
        if (config.Icon is null)
            return null;
        using Image<Rgba32> image = Image.Load<Rgba32>(config.Icon);
        image.Mutate(x => x.Resize(64, 64));
        GBX.NET.Color[,] colors = new GBX.NET.Color[image.Width, image.Height];

        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                Rgba32 pixel = image[x, y];

                colors[x, y] = new GBX.NET.Color(
                    pixel.R,
                    pixel.G,
                    pixel.B,
                    pixel.A
                );
            }
        }

        return colors;
    }
    public static Stream ExtractIcon(NormalizedItem item)
    {
        if (item.Icon is null)
            return Stream.Null;

        using var image = new Image<Rgba32>(item.Icon.GetLength(0), item.Icon.GetLength(1));

        for (int x = 0; x < item.Icon.GetLength(0); x++)
        {
            for (int y = 0; y < item.Icon.GetLength(1); y++)
            {
                var color = item.Icon[x, y];

                image[x, y] = new Rgba32(
                    color.R,
                    color.G,
                    color.B,
                    color.A
                );
            }
        }
        using var ms = new MemoryStream();

        image.SaveAsPbm(ms);
        ms.Seek(0, SeekOrigin.Begin);
        return ms;
    }
}
