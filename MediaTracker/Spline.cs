using GBX.NET;
using System.Numerics;
using TM_GenericMapping.Common;
using static TM_GenericMapping.MediaTracker.Spline;

namespace TM_GenericMapping.MediaTracker;

public class Spline
{
    public enum SplineType
    {
        Bezier,
        Linear
    }
    public struct Knot
    {
        public Vector3 Position;
        public Vector3 TangentIn;   // tangent coming into this knot
        public Vector3 TangentOut;  // tangent leaving this knot
        public Quaternion Rotation;

        public Knot(Vector3 position, Vector3 handleIn, Vector3 handleOut, Quaternion rotation)
        {
            Position = position;
            TangentIn = handleIn;
            TangentOut = handleOut;
            Rotation = rotation;
        }
    }

    private readonly Knot[] _knots;
    public ReadOnlySpan<Knot> Knots => _knots;

    public SplineType Type { get; private set;  }

    public Spline(SplineType type, params ReadOnlySpan<Knot> knots)
    {
        if (knots.Length < 2)
            throw new ArgumentException("Spline must have at least 2 knots.");
        _knots = knots.ToArray();
        Type = type;
    }
    public Spline(params ReadOnlySpan<Knot> knots) : this(SplineType.Bezier, knots) { }

    public int SegmentCount => _knots.Length - 1;

    /// <summary>Evaluate spline position for t in [0,1] over entire spline.</summary>
    public Vector3 Evaluate(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        float segmentLength = 1f / SegmentCount;
        int i = Math.Min((int)(t / segmentLength), SegmentCount - 1);
        float localT = (t - i * segmentLength) / segmentLength;

        var k0 = _knots[i];
        var k1 = _knots[i + 1];
        return Type switch
        {
            SplineType.Bezier => SplineUtils.Bezier(k0.Position, k0.Position + k0.TangentOut, k1.Position + k1.TangentIn, k1.Position, localT),
            SplineType.Linear => Vector3.Lerp(k0.Position, k1.Position, t),
            _ => throw new NotImplementedException()
        };
            
    }
    public Quaternion EvaluateRotation(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        float segmentLength = 1f / SegmentCount;
        int i = Math.Min((int)(t / segmentLength), SegmentCount - 1);
        float localT = (t - i * segmentLength) / segmentLength;

        var k0 = _knots[i];
        var k1 = _knots[i + 1];

        // Smoothly interpolate rotation along the segment
        return Type switch
        {
            SplineType.Bezier => Quaternion.Slerp(k0.Rotation, k1.Rotation, localT),
            SplineType.Linear => Quaternion.Slerp(k0.Rotation, k1.Rotation, localT),
            _ => throw new NotImplementedException(),
        };
    }

    /// <summary>
    /// Using Bezier
    /// </summary>
    /// <param name="t"></param>
    /// <returns></returns>
    public Vector3 EvaluateTangent(float t)
    {
        t = Math.Clamp(t, 0f, 1f);

        float segmentLength = 1f / SegmentCount;
        int i = Math.Min((int)(t / segmentLength), SegmentCount - 1);
        float localT = (t - i * segmentLength) / segmentLength;

        var k0 = _knots[i];
        var k1 = _knots[i + 1];


        return Type switch
        {
            SplineType.Bezier => SplineUtils.BezierTangent(k0.Position, k0.Position + k0.TangentOut, k1.Position + k1.TangentIn, k1.Position, localT),
            SplineType.Linear => Vector3.Normalize(k1.Position - k0.Position),
            _ => throw new NotImplementedException(),
        };
    }

    public (Vector3 position, Quaternion rotation, Vector3 tangent)[] Sample(
        int segments,
        Vector3? forcedStartTangent = null,
        Vector3? forcedEndTangent = null)
    {
        int numSlices = Type == SplineType.Linear ? Knots.Length : segments + 1;
        var result = new (Vector3 position, Quaternion rotation, Vector3 tangent)[numSlices];
        for (int i = 0; i < numSlices; ++i)
        {
            Vector3 p, tangent;
            Quaternion q;

            if (Type == SplineType.Linear)
            {
                var knot = Knots[i];
                p = knot.Position;
                q = knot.Rotation;

                if (i == 0 && forcedStartTangent != null)
                {
                    tangent = (Vector3)forcedStartTangent;
                }
                else if (i == numSlices - 1 && forcedEndTangent != null)
                {
                    tangent = (Vector3)forcedEndTangent;
                }
                else if (i == 0) // first knot, no incoming segment
                {
                    tangent = Vector3.Normalize(Knots[i + 1].Position - knot.Position);
                }
                else if (i == numSlices - 1) // last knot, no outgoing segment
                {
                    tangent = Vector3.Normalize(knot.Position - Knots[i - 1].Position);
                }
                else
                {
                    // middle knots: average of incoming and outgoing segment directions
                    Vector3 inDir = Vector3.Normalize(knot.Position - Knots[i - 1].Position);
                    Vector3 outDir = Vector3.Normalize(Knots[i + 1].Position - knot.Position);
                    tangent = Vector3.Normalize(inDir + outDir);
                }
            }
            else
            {
                float t = i / (float)(numSlices - 1);
                p = Evaluate(t);
                q = EvaluateRotation(t);
                tangent = (i == 0 && forcedStartTangent != null) ? (Vector3)forcedStartTangent :
                          (i == numSlices - 1 && forcedEndTangent != null) ? (Vector3)forcedEndTangent :
                          EvaluateTangent(t);
            }
            result[i] = (p, q, tangent);
        }
        return result;
    }

}
