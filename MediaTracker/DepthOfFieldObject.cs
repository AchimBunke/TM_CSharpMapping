using System.Numerics;
using TM_GenericMapping.Common;

namespace TM_GenericMapping.MediaTracker;

public class DepthOfFieldObject : RenderObject
{
    public float FocusDistance { get; set; }
    public float LensSize { get; set; }
    public int Target { get; set; }
    public Vector3 TargetPosition { get; set; }
    public DepthOfFieldObject(float focusDistance = 10, float lensSize = 0.05f, int target = -1, Vector3? targetPosition = null) : base(new DepthOfFieldRenderer())
    {
        FocusDistance = focusDistance;
        LensSize = lensSize;
        Target = target;
        TargetPosition = targetPosition ?? Vector3.Zero;
    }

    public DepthOfFieldObject(DepthOfFieldObject other) : base(other)
    {
        FocusDistance = other.FocusDistance;
        LensSize = other.LensSize;
        Target = other.Target;
        TargetPosition = other.TargetPosition;
    }

    public override MediaObject Clone()
    {
        return new DepthOfFieldObject(this);
    }
}
