namespace TM_GenericMapping.Common;

public static class TrianglesUtils
{
    /// <summary>
    /// Minimum recommended distance between parallel Triangles. Clipping still occurs upon some distance.
    /// </summary>
    public const float LowClippingOffset = 0.0005f;
    /// <summary>
    /// Recommended distance between parallel Triangles to avoid clipping.
    /// </summary>
    public const float SafeClippingOffset = 0.001f;


    /// <summary>
    /// Name of special Geometry layer in custom items. Used to allow invisible blocks by including a microscopic visible layer.
    /// </summary>
    public const string VisibleRootLayerName = "VisibleRoot";
}
