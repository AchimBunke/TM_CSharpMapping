using System.Numerics;
using TM_GenericMapping.Common;

namespace TM_GenericMapping.MediaTracker;

public interface IAnimationCurve<T>
{
    T Evaluate(float time) => Evaluate(time, out _);
    T Evaluate(float time, out bool isLinear);
    bool IsSegmentLinear(float time);
    float Duration { get; }
    int Length { get; }
    float StartTime { get; }
    void SmoothTangents();
}
public record class AnimationCurveFloat : IAnimationCurve<float>
{
    public AnimationCurveFloat(params ReadOnlySpan<AnimationCurveKeyFrame> keys)
    {
        Keys = keys.ToArray();
    }
    public AnimationCurveKeyFrame[] Keys { get; set; } = [];
    public AnimationCurveKeyFrame this[int i]
    {
        get => Keys[i];
        set => Keys[i] = value;
    }
    private float Hermite(float p0, float m0, float p1, float m1, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;
        float h00 = 2 * t3 - 3 * t2 + 1;
        float h10 = t3 - 2 * t2 + t;
        float h01 = -2 * t3 + 3 * t2;
        float h11 = t3 - t2;
        return h00 * p0 + h10 * m0 + h01 * p1 + h11 * m1;
    }
    public float Evaluate(float time, out bool isLinear)
    {
        isLinear = false;
        if (Keys.Length == 0) return 0f;
        if (time <= Keys[0].Time) return Keys[0].Value;
        if (time >= Keys[^1].Time) return Keys[^1].Value;

        for (int i = 0; i < Keys.Length - 1; i++)
        {
            var kf0 = Keys[i];
            var kf1 = Keys[i + 1];

            if (time >= kf0.Time && time <= kf1.Time)
            {
                float dt = kf1.Time - kf0.Time;
                if (dt == 0f) return kf0.Value;

                float t = (time - kf0.Time) / dt;

                // Compute slope
                float slope = (kf1.Value - kf0.Value) / dt;

                // Calculate tangent magnitudes scaled by segment length
                float m0 = kf0.GetOutTangent();
                float m1 = kf1.GetInTangent();

                // Check for linearity
                if (MathF.Abs(m0 - slope) < 1e-5f && MathF.Abs(m1 - slope) < 1e-5f)
                {
                    isLinear = true;
                    return float.Lerp(kf0.Value, kf1.Value, t);
                }

                // Cubic Hermite interpolation
                m0 *= dt;
                m1 *= dt;
                // Cubic Hermite interpolation
                return Hermite(kf0.Value, m0, kf1.Value, m1, t);
            }
        }

        return 0f; // fallback (should not reach)
    }

    public bool IsSegmentLinear(float time)
    {
        for (int i = 0; i < Keys.Length - 1; i++)
        {
            var kf0 = Keys[i];
            var kf1 = Keys[i + 1];

            if (time >= kf0.Time && time <= kf1.Time)
            {
                float slope = (kf1.Value - kf0.Value) / (kf1.Time - kf0.Time);
                return MathF.Abs(kf0.GetOutTangent() - slope) < 1e-5f &&
                  MathF.Abs(kf1.GetInTangent() - slope) < 1e-5f;
            }
        }

        // Time is out of range; treat as linear by default (or false)
        return true;
    }
    public float Duration
    {
        get
        {
            if (Keys == null || Keys.Length == 0)
                return 0f;

            return Keys[^1].Time - Keys[0].Time;
        }
    }

    public int Length => Keys.Length;

    public float StartTime => (Keys.Length > 0) ? Keys[0].Time : 0f;

    public void SmoothTangents()
    {
        if (Keys.Length < 2)
            return;

        for (int i = 0; i < Keys.Length; i++)
        {
            float tangent;

            if (i == 0)
            {
                // Forward difference for the first keyframe
                var next = Keys[i + 1];
                tangent = (next.Value - Keys[i].Value) / (next.Time - Keys[i].Time);
            }
            else if (i == Keys.Length - 1)
            {
                // Backward difference for the last keyframe
                var prev = Keys[i - 1];
                tangent = (Keys[i].Value - prev.Value) / (Keys[i].Time - prev.Time);
            }
            else
            {
                // Central difference for middle keyframes
                var prev = Keys[i - 1];
                var next = Keys[i + 1];
                tangent = (next.Value - prev.Value) / (next.Time - prev.Time);
            }

            Keys[i] = Keys[i] with
            {
                InTangent = tangent,
                OutTangent = tangent,
                WeightedMode = WeightedMode.None
            };
        }
    }

    public static AnimationCurveFloat FromLinear(float t0, float v0, float t1, float v1)
    {
        float slope = (v1 - v0) / (t1 - t0);

        return new AnimationCurveFloat(
        [
            new AnimationCurveKeyFrame(t0, v0,
                0, 1,
                slope, 1,
                WeightedMode.None),

            new AnimationCurveKeyFrame(t1, v1,
                slope, 1,
                0, 1,
                WeightedMode.None)
        ]);
    }
    public static AnimationCurveFloat FromData(params ReadOnlySpan<(float value, float time)> data)
    {
        var keyFrames = new AnimationCurveKeyFrame[data.Length];

        for (int i = 0; i < data.Length; i++)
        {
            var (v, t) = data[i];
            keyFrames[i] = new(t, v);
        }

        return new(keyFrames);
    }

}
public enum WeightedMode { None, In, Out, Both }
public record struct AnimationCurveKeyFrame(
    float Time,
    float Value,
    float InTangent = 0,
    float InWeight = 1,
    float OutTangent = 0,
    float OutWeight = 1,
    WeightedMode WeightedMode = WeightedMode.None
    )
{
    public float GetInTangent() => (WeightedMode == WeightedMode.In || WeightedMode == WeightedMode.Both) ? InTangent * InWeight : InTangent;
    public float GetOutTangent() => (WeightedMode == WeightedMode.Out || WeightedMode == WeightedMode.Both) ? OutTangent * OutWeight : OutTangent;
}

public record class AnimationCurveVector3 : IAnimationCurve<Vector3>, IPointPath
{
    private AnimationCurveFloat _curveX;
    private AnimationCurveFloat _curveY;
    private AnimationCurveFloat _curveZ;
    public AnimationCurveVector3(AnimationCurveFloat curveX, AnimationCurveFloat curveY, AnimationCurveFloat curveZ)
    {
        _curveX = curveX ?? throw new ArgumentNullException(nameof(curveX));
        _curveY = curveY ?? throw new ArgumentNullException(nameof(curveY));
        _curveZ = curveZ ?? throw new ArgumentNullException(nameof(curveZ));
    }
    public Vector3 Evaluate(float time, out bool isLinear)
    {
        var v = new Vector3(
            _curveX.Evaluate(time, out var isLinearX),
            _curveY.Evaluate(time, out var isLinearY),
            _curveZ.Evaluate(time, out var isLinearZ)
        );
        isLinear = isLinearX && isLinearY && isLinearZ;
        return v;
    }

    public float Duration
        => MathF.Max(_curveX.Duration,
            MathF.Max(
                _curveY.Duration,
                _curveZ.Duration)
            );
    
    public int Length 
        => Math.Max(_curveX.Length,
            Math.Max(
                _curveY.Length,
                _curveZ.Length)
            );

    public float StartTime => 
        MathF.Min(_curveX.StartTime,
            MathF.Min(
                _curveY.StartTime,
                _curveZ.StartTime)
            );
    
    public bool IsSegmentLinear(float time)
    {
        return _curveX.IsSegmentLinear(time) &&
               _curveY.IsSegmentLinear(time) &&
               _curveZ.IsSegmentLinear(time);
    }

    public ReadOnlySpan<Vector3> GetPoints()
    {
        ExceptionUtils.Ensure(_curveX.Length == _curveY.Length && _curveX.Length == _curveZ.Length, () => throw new InvalidOperationException("Curves are of different Length"));
        return _curveX.Keys.Zip(_curveY.Keys, (x,y) => (x,y)).Zip(_curveZ.Keys, (xy, z) => new Vector3(xy.x.Value, xy.y.Value, z.Value)).ToArray().AsSpan();
    }

    public void SmoothTangents()
    {
        _curveX.SmoothTangents();
        _curveY.SmoothTangents();
        _curveZ.SmoothTangents();
    }


    // creation
    public static AnimationCurveVector3 FromCurves(AnimationCurveFloat x, AnimationCurveFloat y, AnimationCurveFloat z) 
        => new(x, y, z);
    public static AnimationCurveVector3 FromData(params ReadOnlySpan<(Vector3 value, float time)> data)
    {
        var x = new AnimationCurveKeyFrame[data.Length];
        var y = new AnimationCurveKeyFrame[data.Length];
        var z = new AnimationCurveKeyFrame[data.Length];

        for (int i = 0; i < data.Length; i++)
        {
            var (v, t) = data[i];
            x[i] = new(t, v.X);
            y[i] = new(t, v.Y);
            z[i] = new(t, v.Z);
        }

        return new(new(x), new(y), new(z));
    }
    public static AnimationCurveVector3 FromValues(float startTime = 0f, float step = 1f, params ReadOnlySpan<Vector3> values)
    {
        var x = new AnimationCurveKeyFrame[values.Length];
        var y = new AnimationCurveKeyFrame[values.Length];
        var z = new AnimationCurveKeyFrame[values.Length];

        for (int i = 0; i < values.Length; i++)
        {
            float t = startTime + i * step;
            x[i] = new(t, values[i].X);
            y[i] = new(t, values[i].Y);
            z[i] = new(t, values[i].Z);
        }

        return new(new(x), new(y), new(z));
    }
    public static AnimationCurveVector3 FromPath(IPointPath path, float startTime = 0f, float step = 1f)
        => FromValues(startTime, step, path.GetPoints());
    public static AnimationCurveVector3 FromFunction(Func<float, Vector3> func, float duration, float sampleRate)
    {
        int count = (int)(duration * sampleRate) + 1;
        var x = new AnimationCurveKeyFrame[count];
        var y = new AnimationCurveKeyFrame[count];
        var z = new AnimationCurveKeyFrame[count];

        for (int i = 0; i < count; i++)
        {
            float t = i / sampleRate;
            Vector3 v = func(t);
            x[i] = new(t, v.X);
            y[i] = new(t, v.Y);
            z[i] = new(t, v.Z);
        }

        return new(new(x), new(y), new(z));
    }

    public static AnimationCurveVector3 FromControlPoints_CatmullRom(ReadOnlySpan<Vector3> controlPoints, float step = 0.1f)
    {
        var samples = SplineUtils.SampleCatmullRom(controlPoints, step);
        return FromValues(0f, step, samples);
    }


}

