using System.Drawing;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace TM_GenericMapping.Common;

public static class ColorUtils
{
    extension (Vector4 v)
    {
        public Color ToColor()
        {
            return Color.FromArgb(
                (int)Math.Clamp(v.W * 255f, 0, 255),
                (int)Math.Clamp(v.X * 255f, 0, 255),
                (int)Math.Clamp(v.Y * 255f, 0, 255),
                (int)Math.Clamp(v.Z * 255f, 0, 255)
            );
        }
        public Color HSVToRGB()
        {
            float h = v.X;
            float s = v.Y;
            float v_ = v.Z;
            float a = v.W;

            h = h % 1f; // ensure 0 ≤ h < 1
            float c = v_ * s;
            float x = c * (1 - MathF.Abs((h * 6) % 2 - 1));
            float m = v_ - c;

            float r1 = 0, g1 = 0, b1 = 0;

            if (h < 1f / 6) { r1 = c; g1 = x; b1 = 0; }
            else if (h < 2f / 6) { r1 = x; g1 = c; b1 = 0; }
            else if (h < 3f / 6) { r1 = 0; g1 = c; b1 = x; }
            else if (h < 4f / 6) { r1 = 0; g1 = x; b1 = c; }
            else if (h < 5f / 6) { r1 = x; g1 = 0; b1 = c; }
            else { r1 = c; g1 = 0; b1 = x; }

            int R = (int)((r1 + m) * 255);
            int G = (int)((g1 + m) * 255);
            int B = (int)((b1 + m) * 255);
            int A = (int)(Math.Clamp(a, 0f, 1f) * 255);

            return Color.FromArgb(A, R, G, B);
        }

    }

    extension(Color c)
    {

        public Vector4 ToVector4()
        {
            return new Vector4(c.R / 255f, c.G / 255f, c.B / 255f, c.A / 255f);
        }

        public Vector4 RGBAToHSVA()
        {
            float r = c.R / 255f;
            float g = c.G / 255f;
            float b = c.B / 255f;
            float a = c.A / 255f;

            float max = MathF.Max(r, MathF.Max(g, b));
            float min = MathF.Min(r, MathF.Min(g, b));
            float delta = max - min;

            float h = 0f, s = 0f, v = max;
            s = (max == 0) ? 0 : delta / max;

            if (delta != 0)
            {
                if (max == r) h = ((g - b) / delta) % 6f;
                else if (max == g) h = ((b - r) / delta) + 2f;
                else h = ((r - g) / delta) + 4f;

                h /= 6f;
                if (h < 0) h += 1f;
            }

            return new Vector4(h, s, v, a);
        }


    }
   


  

}
