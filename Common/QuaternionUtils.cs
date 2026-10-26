
using GBX.NET;
using System.Numerics;

namespace TM_GenericMapping.Common;

public static class QuaternionUtils
{
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
        public static Quaternion CreateFromItemPitchYawRoll(Vec3 pitchYawRoll)
        {
            return Quaternion.CreateFromYawPitchRoll(pitchYawRoll.X, pitchYawRoll.Y, pitchYawRoll.Z);
        }
        public static Quaternion CreateFromItemYawPitchRoll(Vec3 yawPitchRoll)
        {
            return Quaternion.CreateFromYawPitchRoll(yawPitchRoll.X, yawPitchRoll.Y, yawPitchRoll.Z);
        }
        public static Quaternion CreateFromYawPitchRollDegrees(float yaw, float pitch, float roll)
            => Quaternion.CreateFromYawPitchRoll(MathUtils.Deg2Rad * yaw, MathUtils.Deg2Rad * pitch, MathUtils.Deg2Rad * roll);

        public static Quaternion CreateFromAxisAngleDegrees(Vector3 axis, float angleDegrees)
            => Quaternion.CreateFromAxisAngle(axis, MathUtils.Deg2Rad * angleDegrees);

        public static Quaternion CreateFromYRotationDegrees(float angleDegrees)
            => CreateFromAxisAngleDegrees(Vector3.UnitY, angleDegrees);

        public static Quaternion CreateFromXRotationDegrees(float angleDegrees)
           => CreateFromAxisAngleDegrees(Vector3.UnitX, angleDegrees);

        public static Quaternion CreateFromZRotationDegrees(float angleDegrees)
           => CreateFromAxisAngleDegrees(Vector3.UnitZ, angleDegrees);

        public Vector3 ToYawPitchRoll()
        {
            Quaternion q = Quaternion.Normalize(quaternion);

            float sinp = 2f * (q.W * q.X - q.Y * q.Z);
            float pitch, yaw, roll;

            if (MathF.Abs(sinp) > 0.99999f)   // gimbal lock: pin yaw, put all remaining rotation into roll
            {
                pitch = MathF.CopySign(MathF.PI / 2f, sinp);
                yaw = 0f;
                roll = MathF.Atan2(-2f * (q.X * q.Y - q.Z * q.W), 1f - 2f * (q.Y * q.Y + q.Z * q.Z));
            }
            else
            {
                pitch = MathF.Asin(sinp);
                yaw = MathF.Atan2(2f * (q.W * q.Y + q.Z * q.X), 1f - 2f * (q.X * q.X + q.Y * q.Y));
                roll = MathF.Atan2(2f * (q.W * q.Z + q.X * q.Y), 1f - 2f * (q.Z * q.Z + q.X * q.X));
            }

            return new Vector3(yaw, pitch, roll); // (yaw, pitch, roll)
        }
        public Vector3 ToPitchYawRoll()
        {
            var ypr = quaternion.ToYawPitchRoll();
            return new Vector3(ypr.Y, ypr.X, ypr.Z); // (pitch, yaw, roll)
        }

        public static Quaternion CreateLookAt(Vector3 forward)
            => CreateLookAt(forward, Vector3.UnitY);
        public static Quaternion CreateLookAt(Vector3 forward, Vector3 up)
        {
            return Quaternion.CreateFromRotationMatrix(
                Matrix4x4.CreateLookAt(Vector3.Zero, forward, Vector3.UnitY)
            );
        }

        public Quat ToQuat()
        {
            return new Quat(quaternion.X, quaternion.Y, quaternion.Z, quaternion.W);
        }
      
    }
    extension(Quat quat)
    {
        public Quaternion ToQuaternion()
        {
            return new Quaternion(quat.X, quat.Y, quat.Z, quat.W);
        }
        public Vec3 ToYawPitchRoll()
        {
            var pyr = quat.ToPitchYawRoll();
            return new Vec3(pyr.Y, pyr.X, pyr.Z); // (yaw, pitch, roll)
        }
        public Quat Fixed()
        {
            return new Quat(quat.W, quat.X, quat.Y, quat.Z);
        }
    }


}
