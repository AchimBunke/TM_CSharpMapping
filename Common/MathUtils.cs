namespace TM_GenericMapping.Common
{
    public static class MathUtils
    {
        extension (float value)
        {
            public static bool NearlyEqual(float a, float b, float epsilon = float.Epsilon)
            {
                const double MinNormal = 2.2250738585072014E-308d;
                double absA = Math.Abs(a);
                double absB = Math.Abs(b);
                double diff = Math.Abs(a - b);

                if (a.Equals(b))
                { // shortcut, handles infinities
                    return true;
                }
                else if (a == 0 || b == 0 || absA + absB < MinNormal)
                {
                    // a or b is zero or both are extremely close to it
                    // relative error is less meaningful here
                    return diff < (epsilon * MinNormal);
                }
                else
                { // use relative error
                    return diff / (absA + absB) < epsilon;
                }
            }
        }

        public static float ToRadians(float angleDegrees) => angleDegrees * (MathF.PI / 180);
        public static float Deg2Rad => MathF.PI / 180f;
        public static float Rad2Deg => 180f / MathF.PI;
    }
}
