using System.Drawing;
using System.Numerics;

namespace TM_GenericMapping.MediaTracker;

public enum Easing
{
    Linear,
    SmoothStep,
    Smootherstep,
    SineIn,
    SineOut,
    SineInOut,
    QuadIn,
    QuadOut,
    QuadInOut,
    CubicIn,
    CubicOut,
    CubicInOut,
    QuartIn,
    QuartOut,
    QuartInOut,
    QuintIn,
    QuintOut,
    QuintInOut,
    ExpoIn,
    ExpoOut,
    ExpoInOut,
    CircIn,
    CircOut,
    CircInOut,
    BackIn,
    BackOut,
    BackInOut,
    ElasticIn,
    ElasticOut,
    ElasticInOut,
    BounceIn,
    BounceOut,
    BounceInOut,
}

public static class EasingUtils
{
    public static float LinearEasing(float percent) => percent;
    public static float SmoothStep(float percent) => percent * percent * (3f - 2f * percent);
    public static float SmootherStep(float percent) => percent * percent * percent * (percent * (percent * 6f - 15f) + 10f);
    public static float SineIn(float percent) => 1f - MathF.Cos(percent * MathF.PI / 2f);
    public static float SineOut(float percent) => MathF.Sin(percent * MathF.PI / 2f);
    public static float SineInOut(float percent) => 0.5f * (1f - MathF.Cos(percent * MathF.PI));
    public static float QuadIn(float percent) => percent * percent;
    public static float QuadOut(float percent) => percent * (2f - percent);
    public static float QuadInOut(float percent) => percent < 0.5f ? 2f * percent * percent : -1f + (4f - 2f * percent) * percent;
    public static float CubicIn(float percent) => percent * percent * percent;
    public static float CubicOut(float percent) => 1f - MathF.Pow(1f - percent, 3);
    public static float CubicInOut(float percent) => percent < 0.5f ? 4f * percent * percent * percent : 1f - MathF.Pow(-2f * percent + 2f, 3) / 2f;
    public static float QuartIn(float percent) => percent * percent * percent * percent;
    public static float QuartOut(float percent) => 1f - MathF.Pow(1f - percent, 4);
    public static float QuartInOut(float percent) => percent < 0.5f ? 8f * percent * percent * percent * percent : 1f - MathF.Pow(-2f * percent + 2f, 4) / 2f;
    public static float QuintIn(float percent) => percent * percent * percent * percent * percent;
    public static float QuintOut(float percent) => 1f - MathF.Pow(1f - percent, 5);
    public static float QuintInOut(float percent) => percent < 0.5f ? 16f * percent * percent * percent * percent * percent : 1f - MathF.Pow(-2f * percent + 2f, 5) / 2f;
    public static float ExpoIn(float percent) => MathF.Pow(2f, 10f * (percent - 1f));
    public static float ExpoOut(float percent) => 1f - MathF.Pow(2f, -10f * percent);
    public static float ExpoInOut(float percent) => percent < 0.5f ? 0.5f * MathF.Pow(2f, 20f * (percent - 1f)) : 1f - 0.5f * MathF.Pow(2f, -20f * percent);
    public static float CircIn(float percent) => 1f - MathF.Sqrt(1f - MathF.Pow(percent, 2));
    public static float CircOut(float percent) => MathF.Sqrt(1f - MathF.Pow(percent - 1f, 2));
    public static float CircInOut(float percent) => percent < 0.5f ? (1f - MathF.Sqrt(1f - MathF.Pow(2f * percent, 2))) / 2f : (MathF.Sqrt(1f - MathF.Pow(-2f * percent + 2f, 2)) + 1f) / 2f;
    public static float BackIn(float percent) => 2.70158f * percent * percent * percent - 1.70158f * percent * percent;
    public static float BackOut(float percent) => 1f + 2.70158f * MathF.Pow(percent - 1f, 3) + 1.70158f * MathF.Pow(percent - 1f, 2);
    public static float BackInOut(float percent) => percent < 0.5f ? 0.5f * (2.70158f * percent * percent * percent - 1.70158f * percent * percent) : 0.5f * (1f + 2.70158f * MathF.Pow(percent - 1f, 3) + 1.70158f * MathF.Pow(percent - 1f, 2));
    public static float ElasticIn(float percent) => MathF.Sin(13f * MathF.PI / 2f * percent) * MathF.Pow(2f, 10f * (percent - 1f));
    public static float ElasticOut(float percent) => MathF.Sin(-13f * MathF.PI / 2f * (percent + 1f)) * MathF.Pow(2f, -10f * percent) + 1f;
    public static float ElasticInOut(float percent) => percent < 0.5f ? 0.5f * MathF.Sin(13f * MathF.PI / 2f * (2f * percent)) * MathF.Pow(2f, 10f * (2f * percent - 1f)) : 0.5f * (MathF.Sin(-13f * MathF.PI / 2f * (2f * percent - 1f)) * MathF.Pow(2f, -10f * (2f * percent - 1f)) + 2f);
    public static float BounceIn(float percent) => 1f - BounceOut(1f - percent);
    public static float BounceOut(float percent)
    {
        if (percent < 4f / 11f)
        {
            return (121f * percent * percent) / 16f;
        }
        else if (percent < 8f / 11f)
        {
            return (363f / 40f * percent * percent) - (99f / 10f * percent) + 17f / 5f;
        }
        else if (percent < 9f / 10f)
        {
            return (4356f / 361f * percent * percent) - (35442f / 1805f * percent) + 16061f / 1805f;
        }
        else
        {
            return (54f / 5f * percent * percent) - (513f / 25f * percent) + 268f / 25f;
        }
    }
    public static float BounceInOut(float percent) => percent < 0.5f ? 0.5f * BounceIn(percent * 2f) : 0.5f * BounceOut(percent * 2f - 1f) + 0.5f;

    public static float Ease(float percent, Easing easing) => easing switch
    {
        Easing.Linear => LinearEasing(percent),
        Easing.SmoothStep => SmoothStep(percent),
        Easing.Smootherstep => SmootherStep(percent),
        Easing.SineIn => SineIn(percent),
        Easing.SineOut => SineOut(percent),
        Easing.SineInOut => SineInOut(percent),
        Easing.QuadIn => QuadIn(percent),
        Easing.QuadOut => QuadOut(percent),
        Easing.QuadInOut => QuadInOut(percent),
        Easing.CubicIn => CubicIn(percent),
        Easing.CubicOut => CubicOut(percent),
        Easing.CubicInOut => CubicInOut(percent),
        Easing.QuartIn => QuartIn(percent),
        Easing.QuartOut => QuartOut(percent),
        Easing.QuartInOut => QuartInOut(percent),
        Easing.QuintIn => QuintIn(percent),
        Easing.QuintOut => QuintOut(percent),
        Easing.QuintInOut => QuintInOut(percent),
        Easing.ExpoIn => ExpoIn(percent),
        Easing.ExpoOut => ExpoOut(percent),
        Easing.ExpoInOut => ExpoInOut(percent),
        Easing.CircIn => CircIn(percent),
        Easing.CircOut => CircOut(percent),
        Easing.CircInOut => CircInOut(percent),
        Easing.BackIn => BackIn(percent),
        Easing.BackOut => BackOut(percent),
        Easing.BackInOut => BackInOut(percent),
        Easing.ElasticIn => ElasticIn(percent),
        Easing.ElasticOut => ElasticOut(percent),
        Easing.ElasticInOut => ElasticInOut(percent),
        Easing.BounceIn => BounceIn(percent),
        Easing.BounceOut => BounceOut(percent),
        Easing.BounceInOut => BounceInOut(percent),
        _ => throw new NotImplementedException(),
    };
    public static float Ease(float value1, float value2, float percent, Easing easing)
    {
        return float.Lerp(value1, value2, Ease(percent, easing));
    }
    public static Vector3 Ease(Vector3 value1, Vector3 value2, float percent, Easing easing)
    {
        return Vector3.Lerp(value1, value2, Ease(percent, easing));
    }
    public static Quaternion Ease(Quaternion value1, Quaternion value2, float percent, Easing easing)
    {
        return Quaternion.Slerp(value1, value2, Ease(percent, easing));
    }

    public static Color Ease(Color value1, Color value2, float percent, Easing easing)
    {
        return Color.FromArgb(
            (int)Ease(value1.A, value2.A, percent, easing),
            (int)Ease(value1.R, value2.R, percent, easing),
            (int)Ease(value1.G, value2.G, percent, easing),
            (int)Ease(value1.B, value2.B, percent, easing));

    }
}
