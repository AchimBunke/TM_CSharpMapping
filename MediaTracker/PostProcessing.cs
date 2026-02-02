using System.Collections.Generic;
using System.Numerics;

namespace TM_GenericMapping.Common;

public record struct PostProcessingEffectData(Memory<PostProcessingEffect> WorldSpaceEffects, Memory<PostProcessingEffect> NdcSpaceEffects);

public abstract class PostProcessingEffect
{
    public virtual void Update(float deltaTime) { }
    public virtual bool RequiresKeyFrameUpdate(RenderObject o) { return false; }
    public virtual Vector3 Transform(Vector3 v)
    {
        return v;
    }

    public static PostProcessingEffect CreateCustomEffect(Func<Vector3, Vector3> transformFunc)
    {
        return new CustomPostProcessingEffect(transformFunc);
    }
    public static PostProcessingEffect CreateCustomEffect(Func<Vector3, Vector3> transformFunc,
        Action<float> updateFunc,
        Func<RenderObject, bool> requiresKeyFrameCondition)
    {
        return new CustomPostProcessingEffect(transformFunc, updateFunc, requiresKeyFrameCondition);
    }
}
internal class CustomPostProcessingEffect : PostProcessingEffect
{
    private readonly Func<Vector3, Vector3> transformFunc;
    private readonly Action<float> updateFunc;
    private readonly Func<RenderObject, bool> requiresKeyFrameCondition;

    public CustomPostProcessingEffect(Func<Vector3, Vector3> transformFunc) : this(transformFunc, (_) => { }, (_=> false))
    {
    }
    public CustomPostProcessingEffect(Func<Vector3, Vector3> transformFunc,
        Action<float> updateFunc,
        Func<RenderObject, bool> requiresKeyFrameCondition)
    {
        this.transformFunc = transformFunc;
        this.updateFunc = updateFunc;
        this.requiresKeyFrameCondition = requiresKeyFrameCondition;
    }
    public override void Update(float deltaTime)
    {
        updateFunc(deltaTime);
    }
    public override bool RequiresKeyFrameUpdate(RenderObject o)
    {
        return requiresKeyFrameCondition(o);
    }
    public override Vector3 Transform(Vector3 v)
    {
        return transformFunc(v);
    }
}

public class FisheyeDistortionEffect : PostProcessingEffect
{
    private float strength;
    public FisheyeDistortionEffect(float strength)
    {
        this.strength = strength;
    }
    public override bool RequiresKeyFrameUpdate(RenderObject o)
    {
        return true;
    }
    public override Vector3 Transform(Vector3 v)
    {
        float r2 = v.X * v.X + v.Y * v.Y;
        float factor = 1.0f + strength * r2;
        return new Vector3(v.X * factor, v.Y * factor, v.Z);
    }
}

public class BarrelDistortionEffect : PostProcessingEffect
{

    private float strength;
    public BarrelDistortionEffect(float strength)
    {
        this.strength = strength;
    }
    public override Vector3 Transform(Vector3 v)
    {
        float r2 = v.X * v.X + v.Y * v.Y;
        float factor = 1f / (1.0f + strength * r2);
        return new Vector3(v.X * factor, v.Y * factor, v.Z);
    }
    public override bool RequiresKeyFrameUpdate(RenderObject o)
    {
        return true;
    }
}

public class BlackHolePostProcessing : PostProcessingEffect
{
    private readonly MediaObject blackHole;
    private readonly float strength;
    private readonly float radius;
    private Vector3 lastPosition;
    public BlackHolePostProcessing(MediaObject blackHole, float strength, float radius)
    {
        this.blackHole = blackHole;
        this.strength = strength;
        this.radius = radius;
        lastPosition = blackHole.Position;
    }

    public override void Update(float deltaTime)
    {
        bool moved = lastPosition != blackHole.Position;
        lastPosition = blackHole.Position;
    }
    public override bool RequiresKeyFrameUpdate(RenderObject o)
    {
        Vector3 dir = o.Position - blackHole.Position;
        float dist = dir.Length();
        return IsInRange(dist);
    }
    bool IsInRange(float dist) => dist < radius && dist > 0.0001f;
    public override Vector3 Transform(Vector3 v)
    {
        Vector3 dir = v - blackHole.Position;
        float dist = dir.Length();

        if (IsInRange(dist))
        {
            float pull = strength * (1.0f - dist / radius); // smooth falloff
            dir = Vector3.Normalize(dir);
            v -= dir * pull * dist;
        }

        return v;
    }
}

