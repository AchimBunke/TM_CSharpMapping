
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

        public Vector3 ToPitchYawRoll(PyrConvention? c = null)
        {
            c ??= new PyrConvention();
            var q = Quaternion.Normalize(quaternion);

            // Basis of the Euler axes: e1 = yaw, e2 = pitch, e3 = roll (right-handed).
            Vector3 e1 = Vector3.Normalize(c.YawAxis), e2 = Vector3.Normalize(c.PitchAxis);
            if (Math.Abs(Vector3.Dot(e1, e2)) > 1e-4f) throw new ArgumentException("Yaw and pitch axes must be perpendicular.");
            Vector3 e3 = Vector3.Cross(e1, e2);
            if (Math.Abs(Vector3.Dot(Vector3.Normalize(c.RestDirection), e3)) < 0.999f)
                throw new ArgumentException("RestDirection must lie along the roll axis (YawAxis x PitchAxis).");

            // Rotation matrix R (column vectors), then R' = B^T R B, so R' = Rx(a) Ry(b) Rz(c)
            double x = q.X, y = q.Y, z = q.Z, w = q.W;
            double[,] R = {
            { 1-2*(y*y+z*z), 2*(x*y-z*w),   2*(x*z+y*w)   },
            { 2*(x*y+z*w),   1-2*(x*x+z*z), 2*(y*z-x*w)   },
            { 2*(x*z-y*w),   2*(y*z+x*w),   1-2*(x*x+y*y) } };
            Vector3[] E = { e1, e2, e3 };
            double[,] M = new double[3, 3];
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                {
                    // M[i,j] = e_i . (R e_j)
                    var ej = E[j];
                    double rx = R[0, 0] * ej.X + R[0, 1] * ej.Y + R[0, 2] * ej.Z;
                    double ry = R[1, 0] * ej.X + R[1, 1] * ej.Y + R[1, 2] * ej.Z;
                    double rz = R[2, 0] * ej.X + R[2, 1] * ej.Y + R[2, 2] * ej.Z;
                    M[i, j] = E[i].X * rx + E[i].Y * ry + E[i].Z * rz;
                }

            // Intrinsic XYZ solve: M = Rx(a) Ry(b) Rz(r)
            double b = Math.Asin(Math.Clamp(M[0, 2], -1.0, 1.0));            // pitch (about e2)
            double a;                                                        // yaw   (about e1)
            if (Math.Abs(Math.Cos(b)) > c.PoleEpsilon)
                a = Math.Atan2(-M[1, 2], M[2, 2]);
            else
            {
                double poleYawDeg = (c.PitchSign * b < 0) ? c.YawAtNegativePoleDeg : c.YawAtPositivePoleDeg;
                a = c.YawSign * poleYawDeg * Math.PI / 180.0;                // back to axis-angle space
            }

            // roll = what's left after removing yaw: N = Rx(-a) * M = Ry(b) Rz(r)
            double ca = Math.Cos(a), sa = Math.Sin(a);
            double n10 = ca * M[1, 0] + sa * M[2, 0];
            double n11 = ca * M[1, 1] + sa * M[2, 1];
            double r = Math.Atan2(n10, n11);

            double toDeg = 180.0 / Math.PI;
            return new Vector3(
                Wrap((float)(c.PitchSign * b * toDeg)),
                Wrap((float)(c.YawSign * a * toDeg)),
                Wrap((float)(c.RollSign * r * toDeg)));
        }
        static float Wrap(float deg)
        {
            deg %= 360f;
            if (deg > 180f) deg -= 360f;
            if (deg <= -180f) deg += 360f;
            return deg;
        }

    }
    extension(Quat quat)
    {
        public Quaternion ToQuaternion()
        {
            return new Quaternion(quat.X, quat.Y, quat.Z, quat.W);
        }
        public Quat Fixed()
        {
            return new Quat(quat.W, quat.X, quat.Y, quat.Z);
        }
        /// Quaternion -> (pitch, yaw, roll) in degrees under the given convention.

        public Vec3 ToPitchYawRoll()
        {
            return quat.ToQuaternion().ToPitchYawRoll();
        }


    }


}

public sealed record PyrConvention
{
    /// Arrow direction at (0,0,0). Must be parallel to the roll axis (YawAxis x PitchAxis).
    public Vector3 RestDirection = -Vector3.UnitY;      // "back"

    /// Axes the angles rotate about. Order is intrinsic: yaw, then pitch, then roll.
    /// The roll axis is YawAxis x PitchAxis (Z x X = Y by default).
    public Vector3 YawAxis = Vector3.UnitZ;
    public Vector3 PitchAxis = Vector3.UnitX;

    /// Sign of each angle. Default: pitch +90 = down, yaw +90 = +X (left).
    public float YawSign = 1, PitchSign = 1, RollSign = 1;   // RollSign is untested, flip if mirrored

    /// Yaw to report when pitch is exactly +/-90 (yaw and roll are coupled there).
    /// Defaults match your table: up = (-90,180,0), down = (90,0,0).
    public float YawAtNegativePoleDeg = 180;   // pitch < 0
    public float YawAtPositivePoleDeg = 0;     // pitch > 0

    /// Pole detection threshold on cos(pitch).
    public double PoleEpsilon = 1e-6;
}
