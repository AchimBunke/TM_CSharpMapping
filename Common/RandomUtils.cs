using System;
using System.Drawing;
using System.Numerics;

namespace TM_GenericMapping.Common;

public static class RandomUtils
{
    static Random _shared;
    public static Random Shared
    {
        get => _shared ?? (_shared = new Random());
        set => _shared = value;
    }

    /// <summary>
    /// Random Vector betwee (0,0,0) - (1,1,1)
    /// </summary>
    /// <returns></returns>
    public static Vector3 RandomVector3()
        => RandomVector3(-1, 1);
    public static Vector3 RandomVector3(float min, float max)
    {
        return new Vector3(
            RandomUtils.Shared.NextSingle() * (max - min) + min,
            RandomUtils.Shared.NextSingle() * (max - min) + min,
            RandomUtils.Shared.NextSingle() * (max - min) + min
        );
    }
    public static Vector3 RandomVector3(float max) => RandomVector3(0, max);
    public static Vector3 UniformRandomVector3()
        => UniformRandomVector3(-1, 1);

    public static Vector3 UniformRandomVector3(float min, float max)
    {
        var rv = RandomUtils.Shared.NextSingle() * (max - min) + min;
        return new Vector3(rv);
    }
    public static Vector3 UniformRandomVector3(float max) => UniformRandomVector3(0, max);


    public static Color RandomColor()
    {
        var rnd = RandomUtils.Shared;
        return Color.FromArgb(rnd.Next(256), rnd.Next(256), rnd.Next(256));
    }
    public static Color RandomDistinctColor(int distinctValuesByChannel = 8)
    {
        var rnd = RandomUtils.Shared;
        return Color.FromArgb(rnd.Next(0, distinctValuesByChannel) * (255 / distinctValuesByChannel), rnd.Next(0, distinctValuesByChannel) * (255 / distinctValuesByChannel), rnd.Next(0, distinctValuesByChannel) * (255 / distinctValuesByChannel));
    }

    public static Color VariateColor(Color baseColor, float variation = 0.1f)
    {
        // Convert RGB to floats 0..1
        float r = baseColor.R / 255f;
        float g = baseColor.G / 255f;
        float b = baseColor.B / 255f;

        // Apply small random offset
        r = float.Clamp(r + (float)(RandomUtils.Shared.NextDouble() * 2 - 1) * variation, 0f, 1f);
        g = float.Clamp(g + (float)(RandomUtils.Shared.NextDouble() * 2 - 1) * variation, 0f, 1f);
        b = float.Clamp(b + (float)(RandomUtils.Shared.NextDouble() * 2 - 1) * variation, 0f, 1f);

        // Convert back to 0..255
        return Color.FromArgb(
            255,
            (int)(r * 255),
            (int)(g * 255),
            (int)(b * 255)
        );
    }

    public static float NextSingle(this Random random, float min, float max)
    {
        return min + random.NextSingle() * (max - min);
    }
    public static Vector3 RandomUnitSphere(this Random random)
    {
        // Returns a random point on unit sphere
        double u = random.NextDouble();
        double v = random.NextDouble();
        double theta = 2 * Math.PI * u;
        double phi = Math.Acos(2 * v - 1);
        float x = (float)(Math.Sin(phi) * Math.Cos(theta));
        float y = (float)(Math.Sin(phi) * Math.Sin(theta));
        float z = (float)Math.Cos(phi);
        return new Vector3(x, z, y); // note: swap to Unity Y-up
    }

    public static Vector3 RandomDirectionAroundAxis(this Random random, Vector3 axis, float minAngle, float maxAngle)
    {
        // Random angle from axis (uniform on spherical cap)
        float cosTheta = float.Lerp(MathF.Cos(maxAngle), MathF.Cos(minAngle), random.NextSingle());
        float sinTheta = MathF.Sqrt(1f - cosTheta * cosTheta);

        // Random azimuth around axis
        float phi = 2f * MathF.PI * random.NextSingle();

        // Local vector along Z
        Vector3 local = new Vector3(
            sinTheta * MathF.Cos(phi),
            sinTheta * MathF.Sin(phi),
            cosTheta
        );

        // Rotate local Z to align with axis
        Quaternion rotation = QuaternionUtils.FromToRotation(Vector3.UnitZ, axis);
        return Vector3.Transform(local, rotation);
    }

    public static Quaternion RandomRotation(this Random random)
    {
        float u1 = random.NextSingle();
        float u2 = random.NextSingle();
        float u3 = random.NextSingle();

        float sqrt1MinusU1 = MathF.Sqrt(1f - u1);
        float sqrtU1 = MathF.Sqrt(u1);

        float theta1 = 2f * MathF.PI * u2;
        float theta2 = 2f * MathF.PI * u3;

        float x = sqrt1MinusU1 * MathF.Sin(theta1);
        float y = sqrt1MinusU1 * MathF.Cos(theta1);
        float z = sqrtU1 * MathF.Sin(theta2);
        float w = sqrtU1 * MathF.Cos(theta2);

        return new Quaternion(x, y, z, w);
    }
    public static Quaternion RandomRotation(this Random random, Vector3 axis, float minAngle = 0, float maxAngle = 360)
    {
        return Quaternion.CreateFromAxisAngle(axis, MathUtils.Deg2Rad * random.NextSingle(minAngle, maxAngle));
    }

    public static Vector2 RandomInCircle(this Random random, float radius = 1)
    {
        float angle = 2f * MathF.PI * random.NextSingle();

        float r = MathF.Sqrt(random.NextSingle()) * radius;

        return new Vector2(
            MathF.Cos(angle) * r,
            MathF.Sin(angle) * r
        );
    }
}
