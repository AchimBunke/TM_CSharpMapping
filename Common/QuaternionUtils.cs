
using GBX.NET;
using System.Numerics;

namespace TM_GenericMapping.Common;

public static class QuaternionUtils
{
    //public static Quaternion RotationFromDirection(Vector3 direction, Vector3 forward, Vector3 up)
    //{
    //    direction = Vector3.Normalize(direction);
    //    forward = Vector3.Normalize(forward);

    //    float dot = Vector3.Dot(forward, direction);
    //    dot = Math.Clamp(dot, -1f, 1f); // avoid NaNs

    //    if (dot > 0.9999f) return Quaternion.Identity; // same direction
    //    if (dot < -0.9999f) return Quaternion.CreateFromAxisAngle(up, MathF.PI); // opposite

    //    Vector3 axis = Vector3.Normalize(Vector3.Cross(forward, direction));
    //    float angle = MathF.Acos(dot);

    //    return Quaternion.CreateFromAxisAngle(axis, angle);
    //}

    //public static Quaternion RotationFromDirection2D(Vector3 dir)
    //{

    //    if (dir == Vector3.Zero)
    //        return Quaternion.Identity;
    //    var dirNorm = Vector3.Normalize(dir);
        
    //    float angle = MathF.Atan2(dirNorm.Y, dirNorm.X); // angle in radians
    //    return Quaternion.CreateFromAxisAngle(Vector3.UnitZ, angle);
    //}

    public static float GetAngleBetween(Vector2 a, Vector2 b)
    {
        float sin = a.X * b.Y - b.X * a.Y;
        float cos = a.X * b.X + a.Y * b.Y;

        return MathF.Atan2(sin, cos) * (180 / MathF.PI);
    }

    public static Quaternion FromToRotation(Vector3 a, Vector3 b)
    {
        a = Vector3.Normalize(a);
        b = Vector3.Normalize(b);
        Vector3 v = Vector3.Cross(a, b);
        float dot = Vector3.Dot(a, b);
        if (dot >= 1.0f) return Quaternion.Identity;
        if (dot <= -1.0f)
        {
            // 180 degree rotation around any perpendicular axis
            Vector3 ortho = Math.Abs(a.X) < 0.99f ? Vector3.UnitX : Vector3.UnitY;
            v = Vector3.Normalize(Vector3.Cross(a, ortho));
            return new Quaternion(v, 0f);
        }
        float s = (float)Math.Sqrt((1 + dot) * 2);
        float invs = 1f / s;
        return new Quaternion(v * invs, s * 0.5f);
    }

    extension(Quaternion quaternion)
    {
        public static Quaternion CreateFromPitchYawRoll(Vec3 pitchYawRoll)
        {
            return Quaternion.CreateFromYawPitchRoll(pitchYawRoll.Y, pitchYawRoll.X, pitchYawRoll.Z);
        }

        public static Quaternion CreateFromAxisAngleDegrees(Vector3 axis, float angleDegrees)
            => Quaternion.CreateFromAxisAngle(axis, MathUtils.Deg2Rad * angleDegrees);

        public static Quaternion CreateFromYRotationDegrees(float angleDegrees)
            => CreateFromAxisAngleDegrees(Vector3.UnitY, angleDegrees);

        public static Quaternion CreateFromXRotationDegrees(float angleDegrees)
           => CreateFromAxisAngleDegrees(Vector3.UnitX, angleDegrees);

        public static Quaternion CreateFromZRotationDegrees(float angleDegrees)
           => CreateFromAxisAngleDegrees(Vector3.UnitZ, angleDegrees);
    }
}
