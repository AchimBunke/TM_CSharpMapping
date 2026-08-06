using System.Drawing;
using System.Numerics;

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
    extension(GBX.NET.Vec4 v)
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

        public static Color FromRGBHex(string value)
        {
            value = value.TrimStart('#');

            if (value.Length == 3)
            {
                value = string.Concat(
                    value[0], value[0],
                    value[1], value[1],
                    value[2], value[2]);
            }

            if (value.Length != 6)
                throw new FormatException("Expected RGB hex color.");

            return Color.FromArgb(
                Convert.ToInt32(value.Substring(0, 2), 16),
                Convert.ToInt32(value.Substring(2, 2), 16),
                Convert.ToInt32(value.Substring(4, 2), 16));
        }
        public string ToRGBHex()
        {
            return $"{c.R:X2}{c.G:X2}{c.B:X2}";
        }
    }
    // -----------------------------
    // Basic lerp
    // -----------------------------

    public static Color Lerp(Color a, Color b, float t)
    {
        t = Clamp01(t);

        byte r = (byte)(a.R + (b.R - a.R) * t);
        byte g = (byte)(a.G + (b.G - a.G) * t);
        byte bl = (byte)(a.B + (b.B - a.B) * t);
        byte al = (byte)(a.A + (b.A - a.A) * t);

        return Color.FromArgb(al, r, g, bl);
    }

    public static Color Blend(Color a, Color b, float t)
        => Lerp(a, b, t);

    // -----------------------------
    // Luminance / brightness
    // -----------------------------

    public static float Luminance(Color c)
    {
        return 0.2126f * c.R +
               0.7152f * c.G +
               0.0722f * c.B;
    }

    public static float Brightness(Color c)
        => (c.R + c.G + c.B) / 3f;

    // -----------------------------
    // Desaturation
    // -----------------------------

    public static Color Desaturate(Color c, float amount)
    {
        amount = Clamp01(amount);

        byte gray = (byte)(Brightness(c));

        byte r = (byte)(c.R + (gray - c.R) * amount);
        byte g = (byte)(c.G + (gray - c.G) * amount);
        byte b = (byte)(c.B + (gray - c.B) * amount);

        return Color.FromArgb(c.A, r, g, b);
    }

    // -----------------------------
    // Background matching
    // -----------------------------

    public static Color MatchBackground(Color foreground, Color background, float strength)
    {
        strength = Clamp01(strength);

        var blended = Lerp(foreground, background, strength);
        blended = Desaturate(blended, strength * 0.5f);

        return blended;
    }

    public static Color ReduceContrast(Color c, Color reference, float amount)
    {
        amount = Clamp01(amount);

        float avg = Brightness(c);
        float refAvg = Brightness(reference);

        float target = avg + (refAvg - avg) * amount;

        byte r = (byte)(c.R + (target - c.R) * amount);
        byte g = (byte)(c.G + (target - c.G) * amount);
        byte b = (byte)(c.B + (target - c.B) * amount);

        return Color.FromArgb(c.A, r, g, b);
    }

    // -----------------------------
    // Helpers
    // -----------------------------

    private static float Clamp01(float v)
        => v < 0 ? 0 : (v > 1 ? 1 : v);

    public static Color LerpOklab(Color a, Color b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);

        var oa = RgbToOklab(a);
        var ob = RgbToOklab(b);

        double L = oa.L + (ob.L - oa.L) * t;
        double A = oa.A + (ob.A - oa.A) * t;
        double B = oa.B + (ob.B - oa.B) * t;

        int alpha = (int)Math.Round(a.A + (b.A - a.A) * t);

        return OklabToRgb(L, A, B, alpha);
    }

    private static (double L, double A, double B) RgbToOklab(Color c)
    {
        double r = SrgbToLinear(c.R / 255.0);
        double g = SrgbToLinear(c.G / 255.0);
        double b = SrgbToLinear(c.B / 255.0);

        double l = Math.Cbrt(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b);
        double m = Math.Cbrt(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b);
        double s = Math.Cbrt(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b);

        return (
            0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s,
            1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s,
            0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s
        );
    }

    private static Color OklabToRgb(double L, double A, double B, int alpha)
    {
        double l = L + 0.3963377774 * A + 0.2158037573 * B;
        double m = L - 0.1055613458 * A - 0.0638541728 * B;
        double s = L - 0.0894841775 * A - 1.2914855480 * B;

        l = l * l * l;
        m = m * m * m;
        s = s * s * s;

        double r = +4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s;
        double g = -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s;
        double b = -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s;

        return Color.FromArgb(
            alpha,
            Clamp255(LinearToSrgb(r) * 255.0),
            Clamp255(LinearToSrgb(g) * 255.0),
            Clamp255(LinearToSrgb(b) * 255.0)
        );
    }

    private static double SrgbToLinear(double c) =>
        c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);

    private static double LinearToSrgb(double c) =>
        c <= 0.0031308 ? c * 12.92 : 1.055 * Math.Pow(c, 1.0 / 2.4) - 0.055;

    private static int Clamp255(double v) =>
        (int)Math.Round(Math.Clamp(v, 0, 255));




}
