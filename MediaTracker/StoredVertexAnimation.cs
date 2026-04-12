using System.Numerics;

namespace TM_GenericMapping.MediaTracker;

public record class StoredVertexAnimation()
{
    public StoredVertexAnimationFrame[] VertexAnimationFrames { get; set; } = [];
}
public record class StoredVertexAnimationFrame()
{
    public Vector3[] Vertices { get; set; } = [];
    public StoredVertexAnimationFrame[] SubAnimations { get; set; } = [];
}
