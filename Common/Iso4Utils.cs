using GBX.NET;
using Microsoft.VisualBasic;
using System.Drawing.Drawing2D;
using System.Numerics;
using TM_GenericMapping.Items.MeshCompilation;
using static GBX.NET.Engines.Plug.CPlugCrystal;
using static GBX.NET.Engines.Plug.CPlugPrefab;
using static System.Net.Mime.MediaTypeNames;

namespace TM_GenericMapping.Common;

public static class Iso4Utils
{
    extension(Iso4 iso)
    {
        public Quaternion GetRotationQuaternion()
        {
            float sx = iso.GetScaleX();
            float sy = iso.GetScaleY();
            float sz = iso.GetScaleZ();

            if (sx <= float.Epsilon ||
                sy <= float.Epsilon ||
                sz <= float.Epsilon)
            {
                return Quaternion.Identity;
            }

            var matrix = new Matrix4x4(
                iso.XX / sx, iso.XY / sy, iso.XZ / sz, 0f,
                iso.YX / sx, iso.YY / sy, iso.YZ / sz, 0f,
                iso.ZX / sx, iso.ZY / sy, iso.ZZ / sz, 0f,
                0f, 0f, 0f, 1f
            );

            return Quaternion.Normalize(
                Quaternion.CreateFromRotationMatrix(matrix)
            );
        }

        public Vector3 GetPosition()
        {
            return new Vector3(iso.TX, iso.TY, iso.TZ);
        }

        /// <summary>
        /// Transforms an Iso4 by a parent rotation and position.
        /// </summary>
        public Iso4 TransformBy(
            Vector3 position,
            Quaternion rotation)
        {
            var localRotation = iso.GetRotationQuaternion();

            var newPosition =
                position +
                Vector3.Transform(iso.GetPosition(), rotation);

            var newRotation =
                Quaternion.Normalize(rotation * localRotation);

            return IsoFromTransform(
                newPosition,
                newRotation
            );
        }


        public static Iso4 IsoFromTransform(Vector3 location, Quaternion rotation)
        {
            var m = Matrix4x4.CreateFromQuaternion(rotation);

            return new Iso4(
                m.M11, m.M12, m.M13,
                m.M21, m.M22, m.M23,
                m.M31, m.M32, m.M33,
                location.X, location.Y, location.Z
            );
        }
        public static Iso4 IsoFromPitchYawRoll(Vector3 location, float pitchDeg, float yawDeg, float rollDeg)
        {
            float p = pitchDeg * MathUtils.Deg2Rad;
            float y = yawDeg * MathUtils.Deg2Rad;
            float r = rollDeg * MathUtils.Deg2Rad;

            float cp = MathF.Cos(p), sp = MathF.Sin(p);
            float cy = MathF.Cos(y), sy = MathF.Sin(y);
            float cr = MathF.Cos(r), sr = MathF.Sin(r);

            // M = Rz(roll) * Ry(yaw) * Rx(pitch)
            float XX = cr * cy;
            float XY = sp * sy * cr + sr * cp;
            float XZ = sp * sr - sy * cp * cr;

            float YX = -sr * cy;
            float YY = -sp * sr * sy + cp * cr;
            float YZ = sp * cr + sr * sy * cp;

            float ZX = sy;
            float ZY = -sp * cy;
            float ZZ = cp * cy;

            return new Iso4(
                XX, XY, XZ,
                YX, YY, YZ,
                ZX, ZY, ZZ,
                location.X, location.Y, location.Z
            );
        }

    }

}
