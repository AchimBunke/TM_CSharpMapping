using System.Diagnostics.CodeAnalysis;
using TM_GenericMapping.Common;

namespace TM_GenericMapping.MediaTracker;

public abstract class CameraObject : RenderObject
{
    protected CameraObject([NotNull] IRenderer renderer) : base(renderer)
    {
    }

    protected CameraObject(CameraObject other) : base(other)
    {
    }

}
