using GBX.NET;
using System.Numerics;

namespace TM_GenericMapping.Common
{
    public static class VectorUtils
    {
        public static Vector3 PerpendicularXY(this Vector3 dir)
        {
            return Vector3.Normalize(Vector3.Cross(dir, new Vector3(0, 0, 1))); // Perpendicular in XY plane
        }
        public static Vector3 As2D(this Vector3 v)
        {
            return new Vector3(v.X, v.Y, 0);
        }
        public static Vector2 ToVector2(this Vector3 v)
        {
            return new Vector2(v.X, v.Y);
        }

        /// <summary>
        /// Parse from Editor++ copied position.
        /// </summary>
        /// <param name="s"></param>
        /// <returns></returns>
        public static Vector3 FromString(string s)
        {
            var splits = s.Trim().TrimStart('<').TrimEnd('>').Split(",");
            return new Vector3(float.Parse(splits[0]), float.Parse(splits[1]), float.Parse(splits[2]));
        }

        public static Vector3 ToVector3(this Vector2 v)
        {
            return new Vector3(v.X, v.Y, 0);
        }

        public static Vec3 ToVec3(this Vector3 v) => new Vec3(v.X, v.Y, v.Z);
        public static Vector3 ToVector3(this Vec3 v) => new Vector3(v.X, v.Y, v.Z);

        public static Vector3 Normalized(this Vector3 v)
            => Vector3.Normalize(v);

    }
}
