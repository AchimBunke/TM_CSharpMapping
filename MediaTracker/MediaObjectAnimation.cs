using System.Linq.Expressions;
using System.Numerics;
using System.Security.Cryptography;
using TM_GenericMapping.Common;
using TM_GenericMapping.MediaTracker;

namespace TM_GenericMapping.Common;

public interface IMediaObjectAnimator
{
    bool Update(ulong deltaTimeMillis);
    bool Completed { get; }
    bool ContinuosKeyFrames { get; set; }
    ulong KeyframeGenerationRateMillis { get; set; }
    MediaObject Target { get; }
    void AddAnimation(IMediaObjectAnimation<MediaObject> animation);
    void Stop();
    void Pause();
    bool Init();
    bool Initialized { get; }

    IMediaObjectAnimator Repeat(int count = 1);

}

/// <summary>
/// Executes animations of a MediaObject
/// Acts as a decorator to itself - Calling animations will add them in parallel to itself.
/// Note: Prefer AnimateProperty<> to MoveTo/ScaleBy/... because behavior is weird.
/// </summary>
/// <typeparam name="T"></typeparam>
public class MediaObjectAnimator<T> : IMediaObjectAnimator where T : MediaObject
{
    protected List<IMediaObjectAnimation<T>> animations = [];
    protected List<IMediaObjectAnimation<T>> completedAnimations = [];
    public T Target { get; init; }
    public bool Completed => animations.Count == 0;
    public bool ContinuosKeyFrames { get; set; } = false;
    public bool Initialized { get; private set; } = false;
    public ulong KeyframeGenerationRateMillis { get; set; }
    ulong MillisSinceLastKeyFrameGeneration = 0;
    bool stopped = false;
    bool paused = false;

    bool requiresKeyFrame = false;
    MediaObject IMediaObjectAnimator.Target => Target;

    public MediaObjectAnimator(T obj)
    {
        Target = obj;
    }
    public bool Update(ulong deltaTimeMillis)
    {
        if (paused || stopped)
            return false;
        MillisSinceLastKeyFrameGeneration += deltaTimeMillis;
        requiresKeyFrame |= ContinuosKeyFrames;
        foreach (var anim in animations.ToArray()) // ToArray because animations might be modified from within like stopping
        {
            requiresKeyFrame |= anim.Update(Target, deltaTimeMillis);
        }
        // remove completed, if removed keyframe!
        completedAnimations.AddRange(animations.Where(anim => anim.Completed));
        requiresKeyFrame |= animations.RemoveAll(anim => anim.Completed) > 0;

        //repeats
        if(repeats > 0 && animations.Count == 0 && completedAnimations.Count > 0)
        {
            animations.AddRange(completedAnimations);
            completedAnimations.Clear();
            foreach (var anim in animations)
            {
                anim.Reset();
            }
            repeats--;
        }
        if(MillisSinceLastKeyFrameGeneration >= KeyframeGenerationRateMillis)
        {
            MillisSinceLastKeyFrameGeneration = 0;
            bool newKeyFrame = requiresKeyFrame;
            requiresKeyFrame = false;
            return newKeyFrame;
        }
        else
        {
            return false;
        }
        
    }
    public bool Init()
    {
        var b = Update(0);
        Initialized = true;
        return b;
    }

    int repeats = 0;
    public IMediaObjectAnimator Repeat(int count = 1)
    {
        repeats = count;
        return this;
    }

    public void Stop()
    {
        animations.Clear();
        stopped = true;
    }

    public void Pause()
    {
        paused = true;
    }

    public void AddAnimation(IMediaObjectAnimation<MediaObject> animation)
    {
        animations.Add((IMediaObjectAnimation<T>)animation);
    }

    // Animations

    // Wait
    bool WaitAnimation(T obj, float deltaTime, BooleanAnimationParameter animParams, AnimationData animData)
    {
        if (animData.LastFrame)
            return animParams.Value;
        return false;
    }
    public MediaObjectAnimator<T> Wait(BooleanAnimationParameter animParameter)
    {
        var animation = new MediaObjectAnimation<T, BooleanAnimationParameter, AnimationData>()
        {
            Func = WaitAnimation,
            AnimationParameter = animParameter,
            AnimationData = new AnimationData(),
        };
        animations.Add(animation);
        return this;
    }
    public MediaObjectAnimator<T> Wait(float time = 1f, bool endWithKeyFrame = false, bool continuosKeyFrames = false)
        => WaitMillis(Time.Millis(time), endWithKeyFrame, continuosKeyFrames);
    public MediaObjectAnimator<T> WaitMillis(ulong timeMillis = 1000, bool endWithKeyFrame = false, bool continuosKeyFrames = false)
        => Wait(new(endWithKeyFrame) { TimeMillis = timeMillis, ContinuosKeyFrames = continuosKeyFrames });



    // MoveTo

    bool MoveToAnimation(T obj, float deltaTime, Vector3AnimationParameter animParams, AnimationData animData)
    {
        var targetPos = animParams.Vector;
        var timeRemaining = animParams.Time - animData.ElapsedTime;
        var curPos = animParams.Space == Space.Local ? obj.LocalPosition : obj.Position;
        obj.SetPosition(EasingUtils.Ease(curPos, targetPos, Math.Clamp(deltaTime / timeRemaining, 0, 1), animParams.Easing), animParams.Space);

        return animParams.Easing != Easing.Linear;
    }
    public MediaObjectAnimator<T> MoveTo(Vector3AnimationParameter animParameter)
    {
        var animation = new MediaObjectAnimation<T, Vector3AnimationParameter, AnimationData>()
        {
            Func = MoveToAnimation,
            AnimationParameter = animParameter,
            AnimationData = new AnimationData(),
        };
        animations.Add(animation);
        return this;
    }
    public MediaObjectAnimator<T> MoveTo(
        Vector3 targetPostion,
        float time = 1f, 
        Easing easing = Easing.Linear,
        Space space = Space.Local,
        bool continuosKeyFrames = false)
        => MoveTo(new(targetPostion, space) { Easing = easing, TimeMillis = Time.Millis(time), ContinuosKeyFrames = continuosKeyFrames });
    public MediaObjectAnimator<T> MoveTo(
      ScreenPosition screenPos,
      float time = 1f,
      Easing easing = Easing.Linear,
      Space space = Space.Local,
      bool continuosKeyFrames = false)
      => MoveTo(screenPos.ToVector3(), time, easing, space, continuosKeyFrames);

    //MoveTowards
    bool MoveTowardsAnimation(T obj, float deltaTime, TranslationAnimationParameters animParams, AnimationData animData)
    {
        var targetPos = animParams.Vector;
        var curPos = animParams.Space == Space.Local ? obj.LocalPosition : obj.Position;
        obj.SetPosition(EasingUtils.Ease(curPos, targetPos, Math.Clamp(deltaTime * animParams.Amount, 0, 1), animParams.Easing), animParams.Space);

        return animParams.Easing != Easing.Linear;
    }
    public MediaObjectAnimator<T> MoveTowards(TranslationAnimationParameters animParameter)
    {
        var animation = new MediaObjectAnimation<T, TranslationAnimationParameters, AnimationData>()
        {
            Func = MoveTowardsAnimation,
            AnimationParameter = animParameter,
            AnimationData = new AnimationData(),
        };
        animations.Add(animation);
        return this;
    }
    public MediaObjectAnimator<T> MoveTowards(
        Vector3 targetPostion, 
        float time = 1f, 
        float speed = 1f,
        Easing easing = Easing.Linear,
        Space space = Space.Local,
        bool continuosKeyFrames = false)
        => MoveTowards(new(targetPostion, speed) { Easing = easing, Space = space, TimeMillis = Time.Millis(time), ContinuosKeyFrames = continuosKeyFrames });
    public MediaObjectAnimator<T> MoveTowards(
       ScreenPosition screenPos,
      float time = 1f,
      float speed = 1f,
      Easing easing = Easing.Linear,
      Space space = Space.Local,
      bool continuosKeyFrames = false)
        => MoveTowards(screenPos.ToVector3(), time, speed, easing, space, continuosKeyFrames);

    // MoveBy
    bool TranslateByAnimation(T obj, float deltaTime, Vector3AnimationParameter animParams, AnimationData animData)
    {
        obj.Translate(animParams.Vector * deltaTime, animParams.Space);

        return false;
    }
    public MediaObjectAnimator<T> TranslateBy(Vector3AnimationParameter animParameter)
    {
        var animation = new MediaObjectAnimation<T, Vector3AnimationParameter, AnimationData>()
        {
            Func = TranslateByAnimation,
            AnimationParameter = animParameter,
            AnimationData = new AnimationData(),
        };
        animations.Add(animation);
        return this;
    }
    public MediaObjectAnimator<T> TranslateBy(
        Vector3 translation,
        float time = 1f,
        Space space = Space.Local,
        bool continuosKeyFrames = false)
        => TranslateBy(new(translation) { Easing = Easing.Linear, Space = space, TimeMillis = Time.Millis(time), ContinuosKeyFrames = continuosKeyFrames });

    // ScaleTo
    bool ScaleToAnimation(T obj, float deltaTime, Vector3AnimationParameter animParams, AnimationData animData)
    {
        var curScale = obj.LocalScale;
        var targetScale = animParams.Vector;
        var timeRemaining = animParams.Time - animData.ElapsedTime;
        obj.LocalScale = EasingUtils.Ease(curScale, targetScale, Math.Clamp(deltaTime / timeRemaining, 0, 1), animParams.Easing);

        return animParams.Easing != Easing.Linear;
    }
    public MediaObjectAnimator<T> ScaleTo(Vector3AnimationParameter animParameter)
    {
        var animation = new MediaObjectAnimation<T, Vector3AnimationParameter, AnimationData>()
        {
            Func = ScaleToAnimation,
            AnimationParameter = animParameter,
            AnimationData = new AnimationData(),
        };
        animations.Add(animation);
        return this;
    }
    public MediaObjectAnimator<T> ScaleTo(
       Vector3 targetScale,
       float time = 1f,
       Easing easing = Easing.Linear,
       bool continuosKeyFrames = false)
       => ScaleTo(new(targetScale) { Easing = easing, TimeMillis = Time.Millis(time), ContinuosKeyFrames = continuosKeyFrames });



    // Scale By
    /*
    bool ScaleByAnimation(T obj, float deltaTime, Vector3AnimationParameter animParams, AnimationData animData)
    {
        obj.Scale(animParams.Vector * deltaTime, animParams.Space);

        return animParams.Interpolation != InterpolationType.Linear;
    }
    public MediaObjectAnimator<T> ScaleBy(Vector3AnimationParameter animParameter)
    {
        var animation = new MediaObjectAnimation<T, Vector3AnimationParameter, AnimationData>()
        {
            Func = ScaleByAnimation,
            AnimationParameter = animParameter,
            AnimationData = new AnimationData(),
        };
        animations.Add(animation);
        return this;
    }
    public MediaObjectAnimator<T> ScaleBy(
       Vector3 scale,
       float time = 1f,
       InterpolationType easing = InterpolationType.Linear,
       Space space = Space.Local,
       bool continuosKeyFrames = false)
       => ScaleBy(new(scale, space) { Interpolation = easing, TimeMillis = Time.Millis(time), ContinuosKeyFrames = continuosKeyFrames });

    */

    //Rotate To
    bool RotateToAnimation(T obj, float deltaTime, RotationAnimationParameters animParams, AnimationData animData)
    {
        var curRotation = animParams.Space == Space.Local ? obj.LocalRotation : obj.Rotation;
        var targetRotation = animParams.Rotation ?? Quaternion.Identity;
        var timeRemaining = animParams.Time - animData.ElapsedTime;
        obj.SetRotation(EasingUtils.Ease(curRotation, targetRotation, Math.Clamp(deltaTime / timeRemaining, 0, 1), animParams.Easing), animParams.Space);

        return animParams.Easing != Easing.Linear;
    }
    public MediaObjectAnimator<T> RotateTo(
   Quaternion rotation,
   float time = 1f,
   Easing easing = Easing.Linear,
   Space space = Space.Local,
   bool continuosKeyFrames = false)
   => RotateTo(new(Rotation: rotation, Space: space) { Easing = easing, TimeMillis = Time.Millis(time), ContinuosKeyFrames = continuosKeyFrames });
    public MediaObjectAnimator<T> RotateTo(RotationAnimationParameters animParameter)
    {
        var animation = new MediaObjectAnimation<T, RotationAnimationParameters, AnimationData>()
        {
            Func = RotateToAnimation,
            AnimationParameter = animParameter,
            AnimationData = new AnimationData(),
        };
        animations.Add(animation);
        return this;
    }


    //RotateBy
    
  
    bool RotateByAnimation(T obj, float deltaTime, RotationAnimationParameters animParams, AnimationData animData)
    {
        var rotation = Quaternion.CreateFromAxisAngle(animParams.Axis ?? Vector3.UnitZ, animParams.Angle * deltaTime);
        obj.Rotate(rotation, animParams.Space);
        return animParams.Easing != Easing.Linear;
    }
    public MediaObjectAnimator<T> RotateBy(RotationAnimationParameters animParameter)
    {
        var animation = new MediaObjectAnimation<T, RotationAnimationParameters, AnimationData>()
        {
            Func = RotateByAnimation,
            AnimationParameter = animParameter,
            AnimationData = new AnimationData(),
        };
        animations.Add(animation);
        return this;
    }
    public MediaObjectAnimator<T> RotateBy(
       Vector3? axis = null,
       float angle = MathF.PI,
       float time = 1f,
       Space space = Space.Local,
       bool continuosKeyFrames = false)
       => RotateBy(new(Axis: axis, Angle: angle, Space: space) { Easing = Easing.Linear, TimeMillis = Time.Millis(time), ContinuosKeyFrames = continuosKeyFrames });


    bool RotationAnimation(T obj, float deltaTime, RotationAnimationParameters animParams, AnimationData animData)
    {
        float percent = EasingUtils.Ease(animData.ElapsedTime / animParams.Time, animParams.Easing);
        float angle = float.Lerp(0, animParams.Angle, percent);
        var rotation = Quaternion.CreateFromAxisAngle(animParams.Axis ?? Vector3.UnitZ, angle);
        obj.SetRotation(rotation, animParams.Space);
        return true;
    }
    public MediaObjectAnimator<T> Rotation(RotationAnimationParameters animParameter)
    {
        var animation = new MediaObjectAnimation<T, RotationAnimationParameters, AnimationData>()
        {
            Func = RotationAnimation,
            AnimationParameter = animParameter,
            AnimationData = new AnimationData(),
        };
        animations.Add(animation);
        return this;
    }
    public MediaObjectAnimator<T> Rotation(
       Vector3? axis = null,
       float angleRad = MathF.PI,
       float time = 1f,
       Space space = Space.Local,
       bool continuosKeyFrames = false)
       => Rotation(new(Axis: axis, Angle: angleRad, Space: space) { Easing = Easing.Linear, TimeMillis = Time.Millis(time), ContinuosKeyFrames = continuosKeyFrames });

    // Custom Anims

    //public static MediaObjectAnimation<T, P, D> CreateCustomAnimation<P,D>(Func<T, float, P, D, bool> update, P animationParameter, D animationData)
    //    where P : IAnimationParameter
    //    where D : AnimationData
    //{
    //    return new MediaObjectAnimation<T, P, D>()
    //    {
    //        Func = update,
    //        AnimationParameter = animationParameter,
    //        AnimationData = animationData,
    //    };
    //}
    //public static MediaObjectAnimation<T, P, AnimationData> CreateCustomAnimation<P>(Func<T, float, P, AnimationData, bool> update, P animationParameter)
    //   where P : IAnimationParameter
    //    => CreateCustomAnimation(update, animationParameter, new AnimationData());
    //public static MediaObjectAnimation<T, AnimationParameter, D> CreateCustomAnimation<D>(Func<T, float, AnimationParameter, D, bool> update, D animData)
    //    where D : AnimationData
    //    => CreateCustomAnimation(update, new AnimationParameter(), animData);

    public static MediaObjectAnimation<T, P, D> CreateCustomAnimation<P, D>(
        Func<T, float, P, D, bool> update,
        P animationParameter = default,
        D animationData = default)
        where P : IAnimationParameter, new()
        where D : AnimationData, new()
    {
        return new MediaObjectAnimation<T, P, D>
        {
            Func = update,
            AnimationParameter = animationParameter ?? new P(),
            AnimationData = animationData ?? new D(),
        };
    }
    public static MediaObjectAnimation<T, AnimationParameter, D> CreateCustomAnimation<D>(
        Func<T, float, AnimationParameter, D, bool> update,
        float time = 50f,
        bool continuosKeyFrames = false,
        D animationData = default)
        where D : AnimationData, new()
        => CreateCustomAnimation(update, new AnimationParameter() { TimeMillis = Time.Millis(time), ContinuosKeyFrames = continuosKeyFrames }, animationData);

    public static MediaObjectAnimation<T, AnimationParameter, AnimationData> CreateCustomAnimation(
        Func<T, float, AnimationParameter, AnimationData, bool> update,
        float time = 50f,
        bool continuosKeyFrames = false)
        => CreateCustomAnimation(update, new AnimationParameter() { TimeMillis = Time.Millis(time), ContinuosKeyFrames = continuosKeyFrames }, new AnimationData());


    /// <summary>
    /// Custom animation function
    /// </summary>
    /// <typeparam name="P"></typeparam>
    /// <param name="update"></param>
    /// <param name="animationParameter"></param>
    /// <returns></returns>
    public MediaObjectAnimator<T> CustomAnimation<P, D>(Func<T, float, P, D, bool> update, P animationParameter, D animationData)
        where P : IAnimationParameter
        where D : AnimationData
    {
        var animation = new MediaObjectAnimation<T, P, D>()
        {
            Func = update,
            AnimationParameter = animationParameter,
            AnimationData = animationData,
        };
        animations.Add(animation);
        return this;
    }

    /// <summary>
    /// Custom animation function
    /// </summary>
    /// <typeparam name="P"></typeparam>
    /// <param name="update"></param>
    /// <param name="animationParameter"></param>
    /// <returns></returns>
    public MediaObjectAnimator<T> CustomAnimation<P>(Func<T, float, P, AnimationData, bool> update, P animationParameter)
        where P : IAnimationParameter
        => CustomAnimation((t, deltaTime, param, animData) => update(t, deltaTime, param, animData), animationParameter, new AnimationData());

    /// <summary>
    /// Custom animation function
    /// </summary>
    /// <typeparam name="P"></typeparam>
    /// <param name="update"></param>
    /// <param name="animationParameter"></param>
    /// <returns></returns>
    public MediaObjectAnimator<T> CustomAnimation<D>(Func<T, float, AnimationParameter, D, bool> update, D animationData,
        float time = 1f,
        Easing easing = Easing.Linear,
        bool continuosKeyFrames = false)
        where D : AnimationData
        => CustomAnimation((t, deltaTime, param, animData) => update(t, deltaTime, param, animationData), new AnimationParameter() { ContinuosKeyFrames = continuosKeyFrames, TimeMillis = Time.Millis(time), Easing = easing }, animationData);
    public MediaObjectAnimator<T> CustomAnimation<D>(Func<T, float, D, bool> update, D animationData,
      float time = 1f,
       Easing easing = Easing.Linear,
      bool continuosKeyFrames = false)
      where D : AnimationData
      => CustomAnimation((t, deltaTime, param, animData) => update(t, deltaTime, animationData), new AnimationParameter() { ContinuosKeyFrames = continuosKeyFrames, TimeMillis = Time.Millis(time), Easing = easing }, animationData);

    public MediaObjectAnimator<T> CustomAnimation(Func<T, float, AnimationParameter, AnimationData, bool> update, 
        float time = 1f,
        Easing easing = Easing.Linear,
        bool continuosKeyFrames = false)
        => CustomAnimation((t, deltaTime, param, animData) => update(t, deltaTime, param, animData), new AnimationParameter() { ContinuosKeyFrames = continuosKeyFrames, TimeMillis = Time.Millis(time) , Easing = easing }, new AnimationData());

    /// <summary>
    ///  Custom animation function without parameters
    /// </summary>
    /// <typeparam name="P"></typeparam>
    /// <param name="update"></param>
    /// <returns></returns>
    public MediaObjectAnimator<T> CustomAnimation(Func<T, float, AnimationData, bool> update,
        float time = 1f,
        bool continuosKeyFrames = false)
        => CustomAnimation((t, deltaTime, param, animData) => update(t, deltaTime, animData), new AnimationParameter() { ContinuosKeyFrames = continuosKeyFrames, TimeMillis = Time.Millis(time)});
    public MediaObjectAnimator<T> CustomAnimation(Func<T, float, bool> update,
        float time = 1f,
        bool continuosKeyFrames = false)
        => CustomAnimation((t, deltaTime, animData) => update(t, deltaTime), time, continuosKeyFrames);




    // Follow Path

    bool FollowPathAnimation(T obj, float deltaTime, FollowPathAnimationParameter animParams, FollowShapeAnimationData animData)
    {
        var curPos = obj.Position;
        ReadOnlySpan<Vector3> shapePoints = animParams.Path.GetPoints();

        // setup first idx
        if (animData.ElapsedTime == 0 && animParams.StartWithClosestPoint)
        {
            float min = float.MaxValue;
            int idx = 0;
            for (int i = 0; i < shapePoints.Length; ++i)
            {
                var dist = Vector3.Distance(curPos, shapePoints[i]);
                if (dist < min)
                {
                    min = dist;
                    idx = i;
                }
            }
            animData.CurrentTargetVertexIdx = idx;
            animData.StartIndex = idx;
        }

        Vector3 targetPos = curPos;
        var remainingTravelDistance = deltaTime * animParams.Speed;

        //if (animParams.SmoothVertexSkipping)
        //{

        //}
        //else 
        //{
        //    var distance = Vector3.Distance(curPos, shapePoints[animData.CurrentTargetVertexIdx]);
        //    if(distance )
        //}

        bool generateKeyframe = false;
        while (true)
        {
            bool exit = false;
            var targetVertex = shapePoints[animData.CurrentTargetVertexIdx];
            var distanceToTarget = Vector3.Distance(targetPos, targetVertex + animParams.Offset);
            if (distanceToTarget - remainingTravelDistance < 0.001f)
            {
                targetPos = targetVertex;
                remainingTravelDistance -= distanceToTarget;
                switch (animParams.FollowMode)
                {
                    case FollowMode.ToEnd:
                        if (animData.CurrentTargetVertexIdx + 1 == shapePoints.Length)
                            exit = true;
                        else
                            animData.CurrentTargetVertexIdx++;
                            break;
                    case FollowMode.VisitAll:
                        if (animData.CurrentTargetVertexIdx == animData.StartIndex && animData.Iteration > 0)
                            exit = true;
                        else
                        {
                            animData.CurrentTargetVertexIdx++;
                            if(animData.CurrentTargetVertexIdx >= shapePoints.Length)
                            {
                                animData.CurrentTargetVertexIdx = 0;
                                animData.Iteration++;
                            }
                        }
                        break;
                    case FollowMode.Loop:
                        animData.CurrentTargetVertexIdx++;
                        if (animData.CurrentTargetVertexIdx >= shapePoints.Length)
                        {
                            animData.CurrentTargetVertexIdx = 0;
                            animData.Iteration++;
                        }
                        break;
                }
                if (!animParams.SmoothVertexSkipping)
                    exit = true;
                generateKeyframe |= true;
            }
            else
            {
                targetPos = Vector3.Lerp(curPos, targetVertex + animParams.Offset, deltaTime * animParams.Speed / distanceToTarget);
                exit = true;
            }
            if (exit)
                break;
        }
        obj.Position = targetPos ;
        return generateKeyframe;
    }
    public MediaObjectAnimator<T> FollowPath(FollowPathAnimationParameter animParams)
    {
        var animation = new MediaObjectAnimation<T, FollowPathAnimationParameter, FollowShapeAnimationData>()
        {
            Func = FollowPathAnimation,
            AnimationParameter = animParams,
            AnimationData = new FollowShapeAnimationData(),
        };
        animations.Add(animation);
        return this;
    }
    public MediaObjectAnimator<T> FollowPath(
        IPointPath followTarget,
        FollowMode mode = FollowMode.ToEnd,
        bool startWithClosestPoint = false,
        float time = 1f,
        float speed = 1f,
        Vector3 offset = default,
        bool smoothVertexSkipping = false,
        Easing easing = Easing.Linear,
        bool continuosKeyFrames = false)
        => FollowPath(new(followTarget, speed, offset, smoothVertexSkipping, startWithClosestPoint, mode) { ContinuosKeyFrames = continuosKeyFrames, Easing = easing, TimeMillis = Time.Millis(time) });


    // AnimationCurve
    bool PropertyAnimation<TValue>(T obj, float deltaTime, AnimationCurveAnimationParameter<T, TValue> animParams, AnimationData animData)
    {
        float curveDuration = animParams.Curve.Duration;

        // compute time along the curve, scaled by playback speed
        float curveTime = animData.ElapsedTime * animParams.PlaybackSpeed;

        // clamp to curve duration
        curveTime = MathF.Min(curveTime, curveDuration);

        TValue value = animParams.Curve.Evaluate(animParams.Curve.StartTime + curveTime, out bool isLinear);
        animParams.PropertySetter(obj, value);
        return !isLinear;
    }
    public MediaObjectAnimator<T> AnimateProperty<TValue>(AnimationCurveAnimationParameter<T, TValue> animParams)
    {
        ExceptionUtils.Ensure(animParams.Curve.Length >= 1, () => new ArgumentException("Curve must have at least 1 keyframes"));
        var animation = new MediaObjectAnimation<T, AnimationCurveAnimationParameter<T, TValue>, AnimationData>()
        {
            Func = PropertyAnimation,
            AnimationParameter = animParams,
            AnimationData = new AnimationData(),
        };
        animations.Add(animation);
        return this;
    }
    public MediaObjectAnimator<T> AnimateProperty<TValue>(
        IAnimationCurve<TValue> animCurve,
        Action<T, TValue> propertySetter,
        float time,
        float playbackSpeed = 1f,
        bool continuosKeyFrames = false)
   => AnimateProperty(new AnimationCurveAnimationParameter<T, TValue>(animCurve, propertySetter, playbackSpeed) { ContinuosKeyFrames = continuosKeyFrames, TimeMillis = Time.Millis(time) });
    public MediaObjectAnimator<T> AnimateProperty<TValue>(
        IAnimationCurve<TValue> animCurve,
        Action<T, TValue> propertySetter,
        float playbackSpeed = 1f,
        bool continuosKeyFrames = false)
        => AnimateProperty(animCurve, propertySetter, time: animCurve.Duration / playbackSpeed, playbackSpeed);
    public MediaObjectAnimator<T> AnimatePropertyExp<TValue>(
        IAnimationCurve<TValue> animCurve,
        Expression<Func<T, TValue>> propertySetter,
        float playbackSpeed = 1f,
        bool continuosKeyFrames = false)
    {
        // Build a value-only setter bound to Target
        var valueSetter = BuildSetter(propertySetter);

        // Wrap it to match Action<T, TValue> signature
        Action<T, TValue> wrappedSetter = (_, value) => valueSetter(value);

        return AnimateProperty(
            animCurve,
            wrappedSetter,
            time: animCurve.Duration / playbackSpeed,
            playbackSpeed,
            continuosKeyFrames);
    }
    public MediaObjectAnimator<T> AnimatePropertyExp(
     IAnimationCurve<float> animCurve,
     Expression<Func<T, Vector3>> propertySetter,
     float playbackSpeed = 1f,
     bool continuosKeyFrames = false)
    {
        var valueSetter = BuildSetter(propertySetter);
        Action<T, float> wrappedSetter = (_, v) => valueSetter(new Vector3(v));

        return AnimateProperty(
            animCurve,
            wrappedSetter,
            time: animCurve.Duration / playbackSpeed,
            playbackSpeed,
            continuosKeyFrames);
    }
    Action<TValue> BuildSetter<TValue>(Expression<Func<T, TValue>> expr)
    {
        var valueParam = Expression.Parameter(typeof(TValue), "v");

        var body = new ReplaceParameterVisitor(
            expr.Parameters[0],
            Expression.Constant(Target)
        ).Visit(expr.Body);

        Expression assignValue = valueParam;

        var assign = Expression.Assign(body, assignValue);

        return Expression.Lambda<Action<TValue>>(assign, valueParam).Compile();
    }
    class ReplaceParameterVisitor : ExpressionVisitor
    {
        private readonly ParameterExpression _from;
        private readonly ConstantExpression _to;

        public ReplaceParameterVisitor(ParameterExpression from, ConstantExpression to)
        {
            _from = from;
            _to = to;
        }

        protected override Expression VisitParameter(ParameterExpression node)
            => node == _from ? _to : node;
    }


    // Spline
    bool FollowSpline(T obj, float deltaTime, FollowSplineAnimationParameter animParams, AnimationData animData)
    {
        float t = animData.ElapsedTime / animParams.Time;
        t = Math.Clamp(t, 0f, 1f);
        t = EasingUtils.Ease(t, animParams.Easing);
        Vector3 position = animParams.Spline.Evaluate(t);
        Quaternion rotation = animParams.Spline.EvaluateRotation(t);

        obj.Position = position;
        obj.Rotation = rotation;

        return animParams.Easing != Easing.Linear;
    }
    public MediaObjectAnimator<T> FollowSpline(FollowSplineAnimationParameter animParams)
    {
        var animation = new MediaObjectAnimation<T, FollowSplineAnimationParameter, AnimationData>()
        {
            Func = FollowSpline,
            AnimationParameter = animParams,
            AnimationData = new AnimationData(),
        };
        animations.Add(animation);
        return this;
    }
    public MediaObjectAnimator<T> FollowSpline(
        Spline spline,
        float time = 1f,
        Easing easing = Easing.Linear,
        bool continuosKeyFrames = false)
        => FollowSpline(new(spline) { TimeMillis = Time.Millis(time), Easing = easing, ContinuosKeyFrames = continuosKeyFrames });


    // SetData
    public MediaObjectAnimator<T> SetData(Action<T> setter)
    {
        Func<T, float, AnimationParameter, AnimationData, bool> func = (obj, dt, ap, ad) =>
        {
            setter(obj);
            return true;
        };
        var animation = new MediaObjectAnimation<T, AnimationParameter, AnimationData>()
        {
            Func = func,
            AnimationParameter = new AnimationParameter(TimeMillis: 0),
            AnimationData = new AnimationData(),
        };
        animations.Add(animation);
        return this;
    }

}
public class TriangleObjectAnimator<T> : MediaObjectAnimator<T> where T : TriangleObject
{
    public TriangleObjectAnimator(T obj) : base(obj)
    {}

    // OutlineWidth
    bool OutlineWidthAnimation(T obj, float deltaTime, FloatAnimationParameters animParams, AnimationData animData)
    {
        var timeRemaining = animParams.Time - animData.ElapsedTime;
        var currentWidth = obj.OutlineWidth;
        obj.SetOutlineWidth(EasingUtils.Ease(currentWidth, animParams.Value, Math.Clamp(deltaTime / timeRemaining, 0, 1), animParams.Easing));

        return animParams.Easing != Easing.Linear;
    }
    public MediaObjectAnimator<T> OutlineWidth(FloatAnimationParameters animParameter)
    {
        var animation = new MediaObjectAnimation<T, FloatAnimationParameters, AnimationData>()
        {
            Func = OutlineWidthAnimation,
            AnimationParameter = animParameter,
            AnimationData = new AnimationData(),
        };
        animations.Add(animation);
        return this;
    }
    public MediaObjectAnimator<T> OutlineWidth(
       float width,
       float time = 1f,
       Easing easing = Easing.Linear,
       bool continuosKeyFrames = false)
       => OutlineWidth(new(width) { Easing = easing, TimeMillis = Time.Millis(time), ContinuosKeyFrames = continuosKeyFrames });

    // Morphs dynamically between two objects
    bool MorphAnimation(T obj, float deltaTime, GenericMorphAnimationParameter animParams, AnimationData animData)
    {
        var timeRemaining = animParams.Time - animData.ElapsedTime;
        var morphPercent = Math.Clamp(deltaTime / timeRemaining, 0, 1);
        GenericTriangle2DObjectMorpher.Morph(obj, animParams.Target, animParams.Easing, morphPercent);
        //animParams.morpher.Morph(morphPercent);
        return animParams.Easing != Easing.Linear;
    }
    public MediaObjectAnimator<T> Morph(GenericMorphAnimationParameter animParameter)
    {
        var animation = new MediaObjectAnimation<T, GenericMorphAnimationParameter, AnimationData>()
        {
            Func = MorphAnimation,
            AnimationParameter = animParameter,
            AnimationData = new AnimationData(),
        };
        animations.Add(animation);
        return this;
    }
    public MediaObjectAnimator<T> Morph(
       IMorphable target,
       float time = 1f,
       Easing easing = Easing.Linear,
       bool continuosKeyFrames = false)
       => Morph(new(target) { Easing = easing, TimeMillis = Time.Millis(time), ContinuosKeyFrames = continuosKeyFrames });

    // Morphs dynamically between two objects
    bool HierarchicMorphAnimation(T obj, float deltaTime, GenericMorphAnimationParameter animParams, AnimationData animData)
    {
        var timeRemaining = animParams.Time - animData.ElapsedTime;
        var morphPercent = Math.Clamp(deltaTime / timeRemaining, 0, 1);
        GenericTriangle2DObjectMorpher.HierarchicMorph(obj, animParams.Target, animParams.Easing, morphPercent);
        //animParams.morpher.Morph(morphPercent);
        return animParams.Easing != Easing.Linear;
    }
    public MediaObjectAnimator<T> HierarchicMorph(GenericMorphAnimationParameter animParameter)
    {
        var animation = new MediaObjectAnimation<T, GenericMorphAnimationParameter, AnimationData>()
        {
            Func = HierarchicMorphAnimation,
            AnimationParameter = animParameter,
            AnimationData = new AnimationData(),
        };
        animations.Add(animation);
        return this;
    }
    public MediaObjectAnimator<T> HierarchicMorph(
       IMorphable target,
       float time = 1f,
       Easing easing = Easing.Linear,
       bool continuosKeyFrames = false)
       => HierarchicMorph(new(target) { Easing = easing, TimeMillis = Time.Millis(time), ContinuosKeyFrames = continuosKeyFrames });


    // Vertex Animation
    void SetStoredVertexAnimationFrameRecursive(TriangleObject obj, StoredVertexAnimationFrame animFrame)
    {
        for (int i = 0; i < obj.Vertices.Length; ++i)
        {
            obj.Vertices[i] = animFrame.Vertices[i];
        }
        foreach(var sub in obj.SubTriangleObjects.Zip(animFrame.SubAnimations))
        {
            SetStoredVertexAnimationFrameRecursive(sub.First, sub.Second);
        }
    }
    bool StoredVertexAnimation(T obj, float deltaTime, StoredVertexAnimationParameter animParams, StoredVertexAnimationData animData)
    {
        var animationFrameCount = animParams.StoredAnimation.VertexAnimationFrames.Length;
        var desiredKeyFrameIdx = Math.Min((int)((animationFrameCount - 1) * animData.ElapsedTime / animParams.Time), animationFrameCount - 1);
        if (!animData.FirstFrame && desiredKeyFrameIdx == animData.CurrentAnimationFrame)
            return false;
        // only here if new keyframe
        SetStoredVertexAnimationFrameRecursive(obj, animParams.StoredAnimation.VertexAnimationFrames[desiredKeyFrameIdx]);

        animData.CurrentAnimationFrame = desiredKeyFrameIdx;
        return true;
    }
    public MediaObjectAnimator<T> StoredVertexAnimation(StoredVertexAnimationParameter animParameter)
    {
        var animation = new MediaObjectAnimation<T, StoredVertexAnimationParameter, StoredVertexAnimationData>()
        {
            Func = StoredVertexAnimation,
            AnimationParameter = animParameter,
            AnimationData = new StoredVertexAnimationData(),
        };
        animations.Add(animation);
        return this;
    }
    public MediaObjectAnimator<T> StoredVertexAnimation(
        StoredVertexAnimation storedVertexAnimation,
        float time = 1f,
        bool continuosKeyFrames = false)
        => StoredVertexAnimation(new StoredVertexAnimationParameter(storedVertexAnimation) { TimeMillis = Time.Millis(time), ContinuosKeyFrames = continuosKeyFrames });

}
public class DotMatrixDisplayAnimator<T> : TriangleObjectAnimator<T> where T : DotMatrixDisplay
{
    public DotMatrixDisplayAnimator(T obj) : base(obj)
    {
    }

    // MorphText
    bool MorphTextAnimation(T obj, float deltaTime, DotMatrixDisplayMorphAnimationParameter animParams, AnimationData animData)
    {
        var timeRemaining = animParams.Time - animData.ElapsedTime;
        var morphPercent = Math.Clamp(deltaTime / timeRemaining, 0, 1);
        for (int i = 0; i < animParams.Target.MatrixCharacters.Length; ++i)
        {
            GenericTriangle2DObjectMorpher.MorphCharacter(obj.MatrixCharacters[i], animParams.Target.MatrixCharacters[i], animParams.Easing, morphPercent);
        }
        foreach(var c in obj.MatrixCharacters.Skip(animParams.Target.MatrixCharacters.Length))
        {
            GenericTriangle2DObjectMorpher.MorphCharacter(c, c.CreateCharacter(' '), animParams.Easing, morphPercent);
        }
        return animParams.Easing != Easing.Linear;
    }
    public MediaObjectAnimator<T> MorphText(DotMatrixDisplayMorphAnimationParameter animParam)
    {
        if(animParam.Target.FillVertexCount > Target.FillVertexCount)
            throw new ArgumentException("Cannot morph into more characters");
        var animation = new MediaObjectAnimation<T, DotMatrixDisplayMorphAnimationParameter, AnimationData>()
        {
            Func = MorphTextAnimation,
            AnimationParameter = animParam,
            AnimationData = new AnimationData(),
        };
        animations.Add(animation);
        return this;
    }
    public MediaObjectAnimator<T> MorphText(
        T target, 
        float time = 1f,
        Easing easing = Easing.Linear,
        bool continuosKeyFrames = false)
    => MorphText(new(target) { Easing = easing, TimeMillis = Time.Millis(time), ContinuosKeyFrames = continuosKeyFrames });

    public MediaObjectAnimator<T> MorphText(
      string targetText,
      float time = 1f,
      Easing easing = Easing.Linear,
      bool continuosKeyFrames = false)
         => MorphText(new DotMatrixDisplayMorphAnimationParameter(new DotMatrixDisplay(targetText, anchor: Target.Anchor)) { Easing = easing, TimeMillis = Time.Millis(time), ContinuosKeyFrames = continuosKeyFrames });


    // FillText
    bool FillTextAnimation(T obj, float deltaTime, BooleanAnimationParameter animParams, AnimationData animData)
    {
        if (animData.LastFrame)
        {
            if (!animParams.Value)
            {
                for (int i = 0; i < Target.CharacterCount; i++)
                {
                    Target.MatrixCharacters[i].FillCharacter();
                }
            }
            else
            {
                for (int i = 0; i < Target.CharacterCount; i++)
                {
                    Target.MatrixCharacters[i].ClearCharacter();
                }
            }
        }
        return false;
    }
    public MediaObjectAnimator<T> FillText(BooleanAnimationParameter animParam)
    {
        var animation = new MediaObjectAnimation<T, BooleanAnimationParameter, AnimationData>()
        {
            Func = FillTextAnimation,
            AnimationParameter = animParam,
            AnimationData = new AnimationData(),
        };
        animations.Add(animation);
        return this;
    }
    public MediaObjectAnimator<T> FillText(
        bool reverse = false,
        float time = 1f,
        bool continuosKeyFrames = false)
    => FillText(new(reverse) { TimeMillis = Time.Millis(time), ContinuosKeyFrames = continuosKeyFrames });

    //// RandomizeCharacters
    //bool RandomizeCharactersAnimation(T obj, float deltaTime, FloatAnimationParameters animParams, AnimationData animData)
    //{
    //    var timeRemaining = animParams.Time - animData.ElapsedTime;
    //    var morphPercent = Math.Clamp(deltaTime / timeRemaining, 0, 1);
    //    for (int i = 0; i < animParams.Target.MatrixCharacters.Length; ++i)
    //    {
    //        GenericTriangle2DObjectMorpher.MorphCharacter(obj.MatrixCharacters[i], animParams.Target.MatrixCharacters[i], MorphType.Lerp, morphPercent);
    //    }
    //    foreach (var c in obj.MatrixCharacters.Skip(animParams.Target.MatrixCharacters.Length))
    //    {
    //        GenericTriangle2DObjectMorpher.MorphCharacter(c, c.CreateCharacter(' '), MorphType.Lerp, morphPercent);
    //    }
    //    return false;
    //}
    //public MediaObjectAnimator<T> RandomizeCharacters(FloatAnimationParameters animParam)
    //{
    //    var animation = new MediaObjectAnimation<T, FloatAnimationParameters, AnimationData>()
    //    {
    //        Func = RandomizeCharactersAnimation,
    //        AnimationParameter = animParam,
    //        AnimationData = new AnimationData(),
    //    };
    //    animations.Add(animation);
    //    return this;
    //}
    //public MediaObjectAnimator<T> RandomizeCharacters(
    //    float speed = 1f,
    //    float time = 1f,
    //    bool continuosKeyFrames = false)
    //=> RandomizeCharacters(new(speed) { TimeMillis = Time.Millis(time), ContinuosKeyFrames = continuosKeyFrames });
}
public interface IMediaObjectAnimation<T> where T : MediaObject
{
    void Reset();
    bool Completed { get; }
    bool Update(T target, ulong deltaTimeMillis);
}
public class MediaObjectAnimation<T, AP, AD> : IMediaObjectAnimation<T>
    where T : MediaObject
    where AP : IAnimationParameter
    where AD : AnimationData
{
    public required Func<T, float, AP, AD, bool> Func { get; init; }
    public required AP AnimationParameter { get; init; }
    public required AD AnimationData { get; init; }
    public bool Completed => AnimationData.Completed;

    public void Reset()
    {
        AnimationData.Reset();
    }

    public bool Update(T target, ulong deltaTimeMillis)
    {
        bool requiresKeyChange = AnimationParameter.ContinuosKeyFrames;
        AnimationData.ElapsedTimeMillis += deltaTimeMillis;
        AnimationData.LastFrame = AnimationData.ElapsedTime >= AnimationParameter.Time;
        requiresKeyChange |= Func(target, deltaTimeMillis / 1000f, AnimationParameter, AnimationData);
        AnimationData.FirstFrame = false;
        if (AnimationData.ElapsedTime >= AnimationParameter.Time)
            AnimationData.Completed = true;
        return requiresKeyChange;
    }

}

public class AnimationData
{
    public ulong ElapsedTimeMillis { get; set; }
    public float ElapsedTime => ElapsedTimeMillis / 1000f;
    public bool Completed { get; set; }
    public bool FirstFrame { get; set; } = true;
    public bool LastFrame { get; set; } = false;
    public virtual void Reset()
    {
        ElapsedTimeMillis = 0;
        Completed = false;
        FirstFrame = true;
        LastFrame = false;
    }
}


// Animations

public class AnimationCurveAnimation<T, AP, AD> : MediaObjectAnimation<T, AP, AD>
    where T : MediaObject
    where AP : IAnimationParameter
    where AD : AnimationData
{

}