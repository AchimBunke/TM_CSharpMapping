using System.Numerics;

namespace TM_GenericMapping.Common;

public static class Conversion
{
    public static Vector3 V3(float x, float y, float z) => new Vector3(x, y, z);
    public static Vector3 V3(float v) => new Vector3(v);
    public static Vector3 V3(Vector2 v) => v.ToVector3();
    public static Vector3 V3(Vector2 v, float z) => V3(v.X, v.Y, z);
    public static Vector3 V3(float x, float y) => new Vector3(x, y, 0);

    /// <summary>
    /// Parses copied positions from Editor++
    /// </summary>
    /// <param name="v"></param>
    /// <returns></returns>
    public static Vector3 V3(string v) => VectorUtils.FromString(v);

    public static Vector2 V2(float x, float y)=> new Vector2(x, y);
    public static Vector2 V2(float v) => new Vector2(v);
    public static Vector2 V2(Vector3 v) => v.ToVector2();

}
