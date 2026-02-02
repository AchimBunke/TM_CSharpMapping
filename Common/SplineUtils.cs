using System.Numerics;
using System.Runtime.Serialization;
using System.Security.Cryptography.X509Certificates;
using TM_GenericMapping.Common;
using static TM_GenericMapping.Common.Spline;

namespace TM_GenericMapping.Common;

public static class SplineUtils
{
    public static Vector3[] SampleCatmullRom(ReadOnlySpan<Vector3> points, float step)
    {
        if (points.Length < 2) return points.ToArray();

        List<Vector3> result = new();

        for (int i = 0; i < points.Length - 1; i++)
        {
            // Get p0, p1, p2, p3 with edge handling
            Vector3 p0 = i > 0 ? points[i - 1] : points[i];
            Vector3 p1 = points[i];
            Vector3 p2 = points[i + 1];
            Vector3 p3 = (i + 2 < points.Length) ? points[i + 2] : p2;

            for (float t = 0; t < 1f; t += step)
            {
                result.Add(CatmullRom(p0, p1, p2, p3, t));
            }
        }

        // Ensure the last point is added
        result.Add(points[^1]);

        return result.ToArray();
    }
    private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;

        return 0.5f * (
            2f * p1 +
            (-p0 + p2) * t +
            (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
            (-p0 + 3f * p1 - 3f * p2 + p3) * t3
        );
    }

    public static Vector3 Bezier(Vector3 p0, Vector3 p1, Vector3 p2, float t)
    {
        float u = 1f - t;
        return u * u * p0 + 2f * u * t * p1 + t * t * p2;
    }

    public static Vector3 Bezier(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float u = 1f - t;
        float tt = t * t;
        float uu = u * u;
        return uu * u * p0 + 3f * uu * t * p1 + 3f * u * tt * p2 + tt * t * p3;
    }


    public static Spline CreateQuadraticBezier(Vector3 start, Vector3 end, Vector3 controlPoint)
    {
        // Compute equivalent cubic control points from a quadratic curve
        Vector3 p1 = start + (controlPoint - start) * (2f / 3f);
        Vector3 p2 = end + (controlPoint - end) * (2f / 3f);

        var k0 = new Spline.Knot(start, Vector3.Zero, p1 - start, Quaternion.Identity);
        var k1 = new Spline.Knot(end, p2 - end, Vector3.Zero, Quaternion.Identity);

        return new Spline(k0, k1);
    }

    public static Spline CreateCubicBezier(
        Vector3 start,
        Vector3 startControl,
        Vector3 endControl,
        Vector3 end)
    {
        // p0 = start, p3 = end
        Vector3 p0 = start;
        Vector3 p1 = startControl;
        Vector3 p2 = endControl;
        Vector3 p3 = end;

        // Create knots
        var k0 = new Spline.Knot(
            p0,
            Vector3.Zero,       // first knot has no incoming tangent
            p1 - p0,           // outgoing tangent
            Quaternion.Identity
        );

        var k1 = new Spline.Knot(
            p3,
            p2 - p3,            // incoming tangent
            Vector3.Zero,      // last knot has no outgoing tangent
            Quaternion.Identity
        );

        return new Spline(k0, k1);
    }

    public static Vector3 BezierTangent(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float u = 1f - t;
        Vector3 tangent =
            3f * u * u * (p1 - p0) +
            6f * u * t * (p2 - p1) +
            3f * t * t * (p3 - p2);
        return Vector3.Normalize(tangent);
    }

    public static Spline CreateLinear(params ReadOnlySpan<Vector3> points)
    {
        ExceptionUtils.Ensure(points.Length >= 2, () => new ArgumentException("Need at least 2 points to create a linear spline."));

        var knots = new Spline.Knot[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            knots[i] = new Spline.Knot(
                points[i],              // Position
                points[i],              // TangentIn (ignored for linear)
                points[i],              // TangentOut (ignored for linear)
                Quaternion.Identity     // Rotation
            );
        }

        return new Spline(SplineType.Linear, knots);
    }
}
