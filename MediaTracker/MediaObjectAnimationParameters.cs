using System.Numerics;
using TM_GenericMapping.MediaTracker;

namespace TM_GenericMapping.Common;


public interface IAnimationParameter
{
    public Easing Easing { get; }
    public ulong TimeMillis { get; }
    public float Time => TimeMillis / 1000f;
    public bool continuousKeyFrames { get; }
}
public record AnimationParameter(
    Easing Easing = Easing.Linear,
    ulong TimeMillis = 1000,
    bool continuousKeyFrames = false) : IAnimationParameter
{
    public AnimationParameter(
        Easing easing = Easing.Linear,
        float time = 1f,
        bool continuousKeyFrames = false) : this(easing, (ulong)(time * 1000), continuousKeyFrames)
    {

    }
    public AnimationParameter() : this(Easing.Linear, 1f, false) { }
    public float Time => TimeMillis / 1000f;
}

public record Vector3AnimationParameter(Vector3 Vector, Space Space = Space.Local) : AnimationParameter();
public record TranslationAnimationParameters(Vector3 Vector, float Amount = 1f) : Vector3AnimationParameter(Vector)
{

}
public record BooleanAnimationParameter(bool Value) : AnimationParameter;

public record RotationAnimationParameters(Quaternion? Rotation = null, Vector3? Axis = null, float Angle = 1, Space Space = Space.Local) : AnimationParameter;
public record FloatAnimationParameters(float Value) : AnimationParameter;

public record GenericMorphAnimationParameter(IMorphable Target) : AnimationParameter;

public record DotMatrixDisplayMorphAnimationParameter(DotMatrixDisplay Target) : AnimationParameter;

public record AnimationCurveAnimationParameter<T, TValue>(IAnimationCurve<TValue> Curve, Action<T, TValue> PropertySetter, float PlaybackSpeed) : AnimationParameter
    where T : MediaObject;

public enum FollowMode
{
    ToEnd,
    VisitAll,
    Loop,
}
public record TupleAnimationParameter<T1> (Tuple<T1> Tuple) : AnimationParameter;
public record TupleAnimationParameter<T1, T2>(Tuple<T1, T2> Tuple) : AnimationParameter;
public record TupleAnimationParameter<T1, T2, T3>(Tuple<T1, T2, T3> Tuple) : AnimationParameter;
public record TupleAnimationParameter<T1, T2, T3, T4>(Tuple<T1, T2, T3, T4> Tuple) : AnimationParameter;
public record ArrayAnimationParameter<T>(params T[] Array) : AnimationParameter;

public record FollowPathAnimationParameter(IPointPath Path, float Speed, Vector3 Offset, bool SmoothVertexSkipping, bool StartWithClosestPoint, FollowMode FollowMode) : AnimationParameter;
public record FollowSplineAnimationParameter(Spline Spline) : AnimationParameter;
public class FollowShapeAnimationData() : AnimationData()
{
    public int CurrentTargetVertexIdx { get; set; }
    public int StartIndex { get; set; }
    public int Iteration { get; set; }

    public override void Reset()
    {
        base.Reset();
        Iteration = 0;
        StartIndex = 0;
        CurrentTargetVertexIdx = 0;
    }
}
public class ArrayAnimationData<T> : AnimationData
{
    public T[] Data { get; set; } = [];
    public override void Reset()
    {
        base.Reset();
        Data = [];
    }
}

public record StoredVertexAnimationParameter(StoredVertexAnimation StoredAnimation) : AnimationParameter;
public class StoredVertexAnimationData : AnimationData { public int CurrentAnimationFrame { get; set; } }