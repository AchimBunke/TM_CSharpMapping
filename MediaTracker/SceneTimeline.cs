using GBX.NET;
using GBX.NET.Engines.Game;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using TM_GenericMapping.Common;
using TM_GenericMapping.IO;
using TM_GenericMapping.MediaTracker;
using TmEssentials;
using static GBX.NET.Engines.Game.CGameCtnChallenge.TMUnlimiter;
using static GBX.NET.Engines.Game.CGameCtnMediaBlock;

namespace TM_GenericMapping.Common;

/// <summary>
/// 
/// </summary>
/// <param name="AnimationTickRateMillis"></param>
/// <param name="MinKeyFrameTickRateMillis"></param>
/// <param name="FallbackAnimationTimeSeconds">If animation doe snot contain enough keyframes, an additional keyframe will be placed at this time after beginning</param>
public record struct SceneAnimationSettings(
    ulong AnimationTickRateMillis,
    ulong MinKeyFrameTickRateMillis,
    float FallbackAnimationTimeSeconds,
    bool UpdateTrackOrder,
    long AnimationOffsetMillis = 0)
{
    public static SceneAnimationSettings Default => new SceneAnimationSettings
    {
        AnimationTickRateMillis = 20,
        MinKeyFrameTickRateMillis = 100,
        FallbackAnimationTimeSeconds = 1f,
        UpdateTrackOrder = true,
        AnimationOffsetMillis = 0,
    };
}
public record class KeyFrameData()
{
    public required ulong KeyFrameTickRateMillis { get; set; } = 100;
    public ulong NextKeyFrameTargetMillis { get; set; } = 0;
}

public record BlockTemplates(
    CGameCtnMediaClip Clip,
    CGameCtnMediaTrack Track,
    CGameCtnMediaBlockTriangles2D Triangles2D,
    CGameCtnMediaBlockTriangles3D Triangles3D,
    CGameCtnMediaBlockText Text,
    CGameCtnMediaBlockImage Image,
    CGameCtnMediaBlockCameraGame PlayerCamera,
    CGameCtnMediaBlockDOF DepthOfField,
    CGameCtnMediaBlockCameraCustom CustomCamera,
    CGameCtnMediaBlockCameraPath PathCamera,
    CGameCtnMediaBlockCameraOrbital OrbitalCamera)
{
    public CGameCtnMediaClip GetEmptyClip() => MediaTrackerUtils.DeepCopyClip(Clip);
    public CGameCtnMediaTrack GetEmptyTrack() => MediaTrackerUtils.DeepCopyTrack(Track);
    public CGameCtnMediaBlockTriangles2D GetEmptyTriangles2DBlock() => MediaTrackerUtils.DeepCopyBlockTriangles2D(Triangles2D);
    public CGameCtnMediaBlockTriangles3D GetEmptyTriangles3DBlock() => MediaTrackerUtils.DeepCopyBlockTriangles3D(Triangles3D);
    public CGameCtnMediaBlockText GetEmptyTextBlock() => MediaTrackerUtils.DeepCopyBlockText(Text);
    public CGameCtnMediaBlockImage GetEmptyImageBlock() => MediaTrackerUtils.DeepCopyBlockImage(Image);
    public CGameCtnMediaBlockCameraGame GetEmptyPlayerCameraBlock() => MediaTrackerUtils.DeepCopyBlockPlayerCamera(PlayerCamera);
    public CGameCtnMediaBlockDOF GetEmptyDepthOfFieldBlock() => MediaTrackerUtils.DeepCopyBlockDepthOfField(DepthOfField);
    public CGameCtnMediaBlockCameraCustom GetEmptyCustomCameraBlock() => MediaTrackerUtils.DeepCopyBlockCustomCamera(CustomCamera);
    public CGameCtnMediaBlockCameraPath GetEmptyPathCameraBlock() => ObjectCloner.DeepCloneObject(PathCamera);
    public CGameCtnMediaBlockCameraOrbital GetEmptyOrbitalCameraBlock() => ObjectCloner.DeepCloneObject(OrbitalCamera);

}


public class DuplicateKeyComparer<TKey>
                :
             IComparer<TKey> where TKey : IComparable
{
    #region IComparer<TKey> Members

    public int Compare(TKey x, TKey y)
    {
        int result = x.CompareTo(y);

        if (result == 0)
            return 1; // Handle equality as being greater. Note: this will break Remove(key) or
        else          // IndexOfKey(key) since the comparer never returns 0 to signal key equality
            return result;
    }

    #endregion
}

public static class WorldPositionExtensions
{
    public static Vector3 StadiumSurfaceOffset = new Vector3(0, 8, 0);
    public static Bounds StadiumBounds = new Bounds() { Center = new Vector3(768, 128, 768), Size = new Vector3(1536, 256, 1536) };
    public static Bounds StadiumSurfaceBounds = new Bounds() { Center = new Vector3(768, 132, 768), Size = new Vector3(1536, 248, 1536) };
    public static Vector3 StadiumSurfaceCenter => StadiumSurfaceBounds.Center with { Y = StadiumSurfaceOffset.Y };
}

public class SceneTimeline
{
    #region Publics
    public BlockTemplates BlockTemplates { get; init; }

    #endregion

    #region Animation Fields
    /// <summary>
    /// Represents scene level objects
    /// </summary>
    HashSet<MediaObject> objects = [];

    /// <summary>
    /// Contains all object in the entire scene hierarhcy
    /// </summary>
    Dictionary<MediaObject, (CGameCtnMediaTrack track, CGameCtnMediaBlock block, int blockIdx)> objectsInScene = [];
    Dictionary<CGameCtnMediaBlock, HashSet<RenderObject>> blockToRenderObjects = [];
    HashSet<CGameCtnMediaTrack> tracks = [];

    // share ids
    Dictionary<int, RenderObject> blockShareIdToRenderObject = [];

    //HashSet<IMediaObjectAnimator> animators = [];
    Dictionary<MediaObject, Queue<IMediaObjectAnimator>> animators = [];
    HashSet<IMediaObjectAnimator> concurrentAnimators = [];
    HashSet<IMediaObjectAnimator> stopAnimators = [];



    // postProcessing
    Dictionary<uint, HashSet<PostProcessingEffect>> postProcessingEffectsNDC = [];
    Dictionary<uint, HashSet<PostProcessingEffect>> postProcessingEffectsWorld = [];

    SortedList<ulong, Action> delayedActions = new(new DuplicateKeyComparer<ulong>());

    SceneAnimationSettings animationSettings;

    Dictionary<CGameCtnMediaBlock, KeyFrameData> blockToKeyFrameData = [];
    HashSet<CGameCtnMediaBlock> requiresKeyFrame = [];
    //HashSet<MediaObject> addedObjects = [];
    //HashSet<MediaObject> addedSubObjects = [];
    //HashSet<IMediaObjectAnimator> addedAnimators = [];
    HashSet<IMediaObjectAnimator> completedAnimators = [];
    CGameCtnMediaClip clip = null!;
    HashSet<CGameCtnMediaBlock> hiddenInEditorTimeline = [];
    #endregion

    #region Scene API
    public SceneCameraManager CameraManager { get; private set; }
    public IEnumerable<MediaObject> Objects => objects;
    public void Add(params ReadOnlySpan<MediaObject> newObjects)
    {
        foreach (var obj in newObjects)
        {
            EnsureNotAdded(obj);
            objects.Add(obj);
            RegisterObject(obj);
            foreach (var subObject in obj.SubObjects)
            {
                RegisterSubObject(subObject);
            }
        }
    }
    public void AddSubObjects(MediaObject obj, params ReadOnlySpan<MediaObject> subObjects)
    {
        foreach (var subObject in subObjects)
        {
            obj.AddSubObjects(subObject);
            if (IsInScene(obj))
            {
                EnsureNotAdded(subObject);
                RegisterSubObject(subObject);
            }
        }
    }
    public void Wait(float timeSeconds)
    {
        ulong targetAnimationTimeMillis = AnimationTimeMillis + (ulong)(timeSeconds * 1000f);
        while (AnimationTimeMillis < targetAnimationTimeMillis)
        {
            AnimationUpdate();
        }
    }
    public void RequireKeyFrame(params ReadOnlySpan<MediaObject> objects)
    {
        foreach (var obj in objects)
        {
            EnsureAdded(obj);
            SetHierarchyRequiresKeyFrame(obj);
        }
    }
    public void AnimationStep()
    {
        AnimationUpdate();
    }

    public void Delayed(float delaySeconds, Action action)
        => DelayedMillis((ulong)(delaySeconds * 1000), action);
    public void DelayedMillis(ulong delayMillis, Action action)
    {
        if(delayMillis == 0)
        {
            action();
            return;
        }
        ulong targetTimeMillis = AnimationTimeMillis + delayMillis;
        delayedActions.Add(targetTimeMillis, action);
    }

    public void StepToNextKeyFrameUpdate()
    {
        while(AnimationTimeMillis < nextKeyFrameTimeMillis)
        {
            AnimationUpdate();
        }
    }
    /// <summary>
    /// Step to next update of keyframe of specific object
    /// </summary>
    /// <param name="obj"></param>
    public void StepToNextKeyFrameUpdate(MediaObject obj)
    {
        ulong targetMillis = blockToKeyFrameData[objectsInScene[obj].block].NextKeyFrameTargetMillis;
        while (AnimationTimeMillis < targetMillis)
        {
            AnimationUpdate();
        }
    }
    /// <summary>
    /// Runs animation until every animation ended and all objects are created
    /// </summary>
    public void WaitAnimationEnd()
    {
        while (RequiresKeyFrameUpdates || HasActiveAnimations || HasDelayedActions)
        {
            AnimationUpdate();
        }
       
    }


    public void ForceStopAllAnimations()
    {
        foreach (var animator in animators.Values.SelectMany(v=>v))
        {
            animator.Stop();
        }
        foreach(var concurrentAnimator in concurrentAnimators)
        {
            concurrentAnimator.Stop();
        }
    }
    public void StopAnimation(params ReadOnlySpan<MediaObject> targets)
    {
        foreach (var obj in targets)
        {
            if (animators.TryGetValue(obj, out var animatorQueue))
            {
                foreach (var animator in animatorQueue)
                {
                    animator.Stop();
                }
            }

            foreach (var concurrentAnimator in concurrentAnimators)
            {
                if (concurrentAnimator.Target == obj)
                    concurrentAnimator.Stop();
            }
        }
    }

    public void SetPosition(MediaObject obj, Vector3 position, Space space = Space.Local)
    {
        obj.SetPosition(position, space);
        if (IsInScene(obj))
            SetHierarchyRequiresKeyFrame(obj);
    }
    public void SetPosition(MediaObject obj, ScreenPosition moveLocation, Space space = Space.Local) => SetPosition(obj, ScreenPositionExtensions.DefaultScreenToVector3Function(moveLocation), space);
    public void Translate(MediaObject obj, Vector3 position, Space space = Space.Local)
    {
        obj.Translate(position, space);
        if (IsInScene(obj))
            SetHierarchyRequiresKeyFrame(obj);
    }
    public void SetScale(MediaObject obj, Vector3 scale)
    {
        obj.LocalScale = scale;
        if (IsInScene(obj))
            SetHierarchyRequiresKeyFrame(obj);
    }
    public void SetScale(MediaObject obj, float scale) => SetScale(obj, Vector3.Create(scale));
    public void SetRotation(MediaObject obj, Quaternion rotation, Space space = Space.Local)
    {
        obj.SetRotation(rotation, space);
        if (IsInScene(obj))
            SetHierarchyRequiresKeyFrame(obj);
    }
    public void Rotate(MediaObject obj, Quaternion rotation, Space space = Space.Local)
    {
        obj.Rotate(rotation, space);
        if (IsInScene(obj))
            SetHierarchyRequiresKeyFrame(obj);
    }

    public void AddLocalNDCPostProcessingEffects(RenderObject obj, params ReadOnlySpan<PostProcessingEffect> effects)
    {
        foreach (var effect in effects)
        {
            obj.AddLocalNDCPostProcessingEffect(effect);
            if (IsInScene(obj))
                SetHierarchyRequiresKeyFrame(obj);
        }
    }
    public void AddLocalWorldSpacePostProcessingEffects(RenderObject obj, params ReadOnlySpan<PostProcessingEffect> effects)
    {
        foreach (var effect in effects)
        {
            obj.AddLocalWorldSpacePostProcessingEffect(effect);
            if (IsInScene(obj))
                SetHierarchyRequiresKeyFrame(obj);
        }
    }

    public void AddNDCSpacePostProcessingEffect(PostProcessingEffect effect, params ReadOnlySpan<string> layers)
    {
        foreach(var layer in layers)
        {
            var layerMask = LayerManager.GetLayerMask(layer);
            if (!postProcessingEffectsNDC.TryGetValue(layerMask, out var effectSet))
            {
                effectSet = new HashSet<PostProcessingEffect>();
                postProcessingEffectsNDC[layerMask] = effectSet;
            }
            effectSet.Add(effect);
        }
    }
    public void AddWorldSpacePostProcessingEffect(PostProcessingEffect effect, params ReadOnlySpan<string> layers)
    {
        foreach (var layer in layers)
        {
            var layerMask = LayerManager.GetLayerMask(layer);
            if (!postProcessingEffectsWorld.TryGetValue(layerMask, out var effectSet))
            {
                effectSet = new HashSet<PostProcessingEffect>();
                postProcessingEffectsWorld[layerMask] = effectSet;
            }
            effectSet.Add(effect);
        }
    }


    public void Morph<T, T2>(T source, T2 target, Easing type = Easing.Linear, float percent = 1f)
        where T : MediaObject, IMorphable
        where T2 : IMorphable
    {
        GenericTriangle2DObjectMorpher.Morph(source, target, type, percent);
        if (IsInScene(source))
            SetHierarchyRequiresKeyFrame(source);
    }

    public void Play(bool stopAfter = false, params ReadOnlySpan<IMediaObjectAnimator> newAnimators)
    {
        foreach (var newAnimator in newAnimators)
        {
            if (animators.TryGetValue(newAnimator.Target, out var animatorQueue))
            {
                animatorQueue.Enqueue(newAnimator);
            }
            else
            {
                animators[newAnimator.Target] = new Queue<IMediaObjectAnimator>([newAnimator]);
            }
        }
        if(stopAfter)
            StopObjectAnimationsAfter(newAnimators);
    }
    public void Play(ulong tickRateMillis, ulong keyFrameGenerationTickRateMillis = 0, bool stopAfter = false, params ReadOnlySpan<IMediaObjectAnimator> animators)
    {
        foreach (var animator in animators)
        {
            SetKeyFrameTickRate(tickRateMillis, animator.Target);
            animator.KeyframeGenerationRateMillis = keyFrameGenerationTickRateMillis;
        }
        Play(stopAfter, animators);
    }
    public void Play(ulong tickRateMillis, params ReadOnlySpan<IMediaObjectAnimator> animators)
        => Play(tickRateMillis, 0, false, animators);
    public void Play(params ReadOnlySpan<IMediaObjectAnimator> newAnimators)
        => Play(false, newAnimators);

    public void PlayConcurrent(params ReadOnlySpan<IMediaObjectAnimator> newAnimators)
    {

        foreach (var newAnimator in newAnimators)
        {
            concurrentAnimators.Add(newAnimator);
        }
    }
    public void PlayConcurrent(ulong tickRateMillis, params ReadOnlySpan<IMediaObjectAnimator> newAnimators)
    {
        foreach (var animator in newAnimators)
        {
            SetKeyFrameTickRate(tickRateMillis, animator.Target);
        }
        PlayConcurrent(newAnimators);
    }


    public void StopObjectAnimationsAfter(params ReadOnlySpan<IMediaObjectAnimator> animators)
    {
        foreach(var animator in animators)
        {
            stopAnimators.Add(animator);
        }
    }


    public void SetKeyFrameTickRate(ulong tickRateMillis, params ReadOnlySpan<MediaObject> objects)
    {

        foreach(var obj in objects)
        {
            EnsureAdded(obj);
            var block = objectsInScene[obj].block;
            if (block is not null)
            {
                blockToKeyFrameData[block].KeyFrameTickRateMillis = Math.Max(animationSettings.MinKeyFrameTickRateMillis, tickRateMillis);
            }
        }
    }

    /// <summary>
    /// Use for invalid operations that should throw
    /// </summary>
    /// <param name="obj"></param>
    /// <exception cref="ArgumentException"></exception>
    private void EnsureAdded(MediaObject obj)
    {
        if (!IsInScene(obj))
            throw new ArgumentException("object is not added to scene!");
    }
    private void EnsureNotAdded(MediaObject obj)
    {
        if (IsInScene(obj))
            throw new ArgumentException("object is not added to scene!");
    }

    /// <summary>
    /// Sets the ScreenPosition to Vector3 function for this scene's viewbox
    /// </summary>
    public void ResetScreenPositionFunction()
    {
        ScreenPositionExtensions.ScreenToVector3Function = ScreenPositionExtensions.DefaultScreenToVector3Function;
    }

    public void SetTrackCycling(bool cycleTrack = true, params ReadOnlySpan<MediaObject> objects)
    {
        foreach (var obj in objects)
        {
            EnsureAdded(obj);
            var track = objectsInScene[obj].track;
            if (track is not null)
            {
                track.IsCycling = cycleTrack;
            }
        }
    }
    public MediaObject[] GetObjectsFromHierarchy(params ReadOnlySpan<MediaObject> objects)
    {
        List<MediaObject> objs = [];
        foreach(var o in objects)
        {
            objs.Add(o);
            objs.AddRange(GetObjectsFromHierarchy(o.SubObjects.ToArray()));
        }
        return objs.ToArray();
    }

    public void SetKeepTrackActive(bool keepActive = true, params ReadOnlySpan<MediaObject> objects)
    {
        //throw new NotImplementedException("Feature not enabled!");
        foreach (var obj in objects)
        {
            EnsureAdded(obj);
            var track = objectsInScene[obj].track;
            if (track is not null)
            {
                track.IsKeepPlaying = keepActive;
            }
        }
    }

    public void TrySetHiddenInIngameEditorTimeline(bool hidden, params ReadOnlySpan<MediaObject> objects)
    {
        foreach(var obj in objects)
        {
            EnsureAdded(obj);

            var block = objectsInScene[obj].block;
            if (block is not null)
            {
                if(hidden)
                    hiddenInEditorTimeline.Add(block);
                else
                    hiddenInEditorTimeline.Remove(block);
            }   
        }
    }

    #endregion

    #region Internal
    public SceneTimeline()
    {
        BlockTemplates = MediaTrackerUtils.CreateBlockTemplates();
        CameraManager = new SceneCameraManager(this);
    }

    public float AnimationTickRateMillis => animationSettings.AnimationTickRateMillis;
    public float AnimationTime => AnimationTimeMillis / 1000f;
    float NextKeyFrameTime => nextKeyFrameTimeMillis / 1000f;

    public ulong AnimationTimeMillis { get; private set; }
    ulong nextKeyFrameTimeMillis = 0;
    protected bool RequiresKeyFrameUpdates =>
        requiresKeyFrame.Count > 0;
    protected bool HasActiveAnimations => animators.Count > 0 || concurrentAnimators.Count > 0;
    protected bool HasDelayedActions => delayedActions.Count > 0;
    void AnimationUpdate()
    {
        PreAnimationUpdateTick?.Invoke(this);
        UpdateDelayedAction();
        UpdateAnimators();
        UpdatePostProcessingEffects();
        if (AnimationTimeMillis >= nextKeyFrameTimeMillis)
        {
            GenerateKeyFrames();
            nextKeyFrameTimeMillis += animationSettings.MinKeyFrameTickRateMillis;
        }
        PostAnimationUpdateTick?.Invoke(this);
        AnimationTimeMillis += animationSettings.AnimationTickRateMillis;
    }
    void UpdateAnimators()
    {
        List<MediaObject> justCompleted = [];

        foreach (var stopAnimator in stopAnimators)
        {
            if (!stopAnimator.Completed)
                continue;
            if (animators.TryGetValue(stopAnimator.Target, out var animatorQueue))
            {
                foreach (var anim in animatorQueue)
                {
                    anim.Stop();
                }
            }
            foreach (var concurrentAnimator in concurrentAnimators)
            {
                if (concurrentAnimator.Target == stopAnimator.Target)
                    concurrentAnimator.Stop();
            }
        }


        foreach (var animatorQueue in animators.Values)
        {
            var animator = animatorQueue.Peek();
            if (animator.Initialized)
            {
                if (animator.Update(animationSettings.AnimationTickRateMillis))
                    SetHierarchyRequiresKeyFrame(animator.Target);
                if (animator.Completed)
                    justCompleted.Add(animator.Target);
            }
            else
            {
                if (animator.Init())
                    SetHierarchyRequiresKeyFrame(animator.Target);
            }
        }
        foreach (var concurrentAnimator in concurrentAnimators)
        {
            if (concurrentAnimator.Initialized)
            {
                if (concurrentAnimator.Update(animationSettings.AnimationTickRateMillis))
                    SetHierarchyRequiresKeyFrame(concurrentAnimator.Target);
            }
            else
            {
                if (concurrentAnimator.Init())
                    SetHierarchyRequiresKeyFrame(concurrentAnimator.Target);
            }
        }
        completedAnimators.UnionWith(justCompleted.Select(a => animators[a].Dequeue()));

        foreach (var key in animators.Keys.ToArray())
        {
            if (animators[key].Count == 0)
            {
                animators.Remove(key);
            }
        }
        completedAnimators.UnionWith(concurrentAnimators.Where(a => a.Completed));
        concurrentAnimators.RemoveWhere(a => a.Completed);


        // init queued animation with elapsedTime = 0
        foreach (var newAnimatorKV in animators.Where(kv => justCompleted.Contains(kv.Key)))
        {
            var animatorQueue = newAnimatorKV.Value;
            var newAnimator = animatorQueue.Peek();
            if (newAnimator.Init())
                SetHierarchyRequiresKeyFrame(newAnimator.Target);
        }
    }
    void UpdateDelayedAction()
    {
        while (HasDelayedActions && delayedActions.Keys[0] <= AnimationTimeMillis)
        {
            var action = delayedActions.Values[0];
            delayedActions.RemoveAt(0);
            action();
        }
    }
    void UpdatePostProcessingEffects()
    {
        foreach(var (layer, effectSet) in postProcessingEffectsNDC.Concat(postProcessingEffectsWorld))
        {
            var objectsInLayer = 
                objectsInScene
                .Where(kv=>kv.Key is RenderObject)
                .Where(kv => LayerManager.IsInLayer(kv.Key.LayerMask, layer))
                .Select(kv => kv.Key)
                .Cast<RenderObject>();
            foreach (var effect in effectSet)
            {
                effect.Update(animationSettings.AnimationTickRateMillis / 1000f);
                RequireKeyFrame(objectsInLayer.Where(effect.RequiresKeyFrameUpdate).ToArray());
            }
        }
    }
    void GenerateKeyFrames()
    {
        foreach (var block in requiresKeyFrame)
        {
            var keyFrameData = blockToKeyFrameData[block];

            if (keyFrameData.NextKeyFrameTargetMillis <= nextKeyFrameTimeMillis)
            {
                GenerateKeyFrame(block);
                keyFrameData.NextKeyFrameTargetMillis = nextKeyFrameTimeMillis + keyFrameData.KeyFrameTickRateMillis;
            }
        }
        requiresKeyFrame.Clear();

    }
    void RegisterObject(MediaObject obj)
    {
        CGameCtnMediaBlock block = null;
        CGameCtnMediaTrack track = null;
        int idx = -1;
        if (obj is RenderObject renderObj)
        {
            // if use shared id then sharing blocks across hiearchies is possible
            if (renderObj.BlockShareId is int blockShareId)
            {
                if(!blockShareIdToRenderObject.TryGetValue(blockShareId, out var blockOwner))
                {
                    blockOwner = renderObj;
                    blockShareIdToRenderObject[blockShareId] = blockOwner;
                    (block, track) = CreateAndRegisterBlock(renderObj);
                }
                else
                {
                    var existingGroupData = objectsInScene[blockOwner];
                    block = existingGroupData.block;
                    track = existingGroupData.track;
                    blockToRenderObjects[block].Add(renderObj);
                    requiresKeyFrame.Add(block);
                }
            }
            else
            {
                (block, track) = CreateAndRegisterBlock(renderObj);
            }
            if (renderObj.Renderer is IKeysRenderer keysRenderer)
                idx = keysRenderer.AddRenderDataToBlock(renderObj, block);
            else if (renderObj.Renderer is ITwoKeyRenderer twoKeyRenderer)
            {
                twoKeyRenderer.SetDataToStart(renderObj, block);
                (block as CGameCtnMediaBlock.IHasTwoKeys).Start = TimeSingle.FromMilliseconds((long)AnimationTimeMillis + animationSettings.AnimationOffsetMillis);
            }

        }
        if(obj is CameraObject cameraObj)
        {
            CameraManager.AddCamera(cameraObj);
        }
        objectsInScene[obj] = (track, block, idx);
    }
    (CGameCtnMediaBlock newBlock, CGameCtnMediaTrack newTrack) CreateAndRegisterBlock(RenderObject renderObj)
    {
        var newBlock = renderObj.Renderer.CreateEmptyBlock(BlockTemplates);
        var newTrack = MediaTrackerUtils.DeepCopyTrack(BlockTemplates.Track);
        newTrack.Name = renderObj.Name;
        newTrack.Blocks.Add(newBlock);
        clip.Tracks.Add(newTrack);
        tracks.Add(newTrack);

        blockToRenderObjects[newBlock] = [renderObj];
        blockToKeyFrameData[newBlock] = new KeyFrameData() { KeyFrameTickRateMillis = animationSettings.MinKeyFrameTickRateMillis };
        requiresKeyFrame.Add(newBlock);
        return (newBlock, newTrack);
    }

    void RegisterSubObject(MediaObject obj)
    {
        if (objectsInScene.ContainsKey(obj))
            return;
        if (!objectsInScene.ContainsKey(obj.Parent))
        {
            // always register downwards
            RegisterSubObject(obj.Parent);
            return;
        }
        // here i know parent is registered
        if(obj is RenderObject renderObj)
        {
            // uses block sharing
            if (TryFindSharedBlockOwner(renderObj, out var sharedBlockOwner))
            {
                var (commonTrack, commonBlock, _) = objectsInScene[sharedBlockOwner];
                int idx = -1;
                if (renderObj.Renderer is IKeysRenderer keysRenderer)
                    idx = keysRenderer.AddRenderDataToBlock(renderObj, commonBlock);
                else if (renderObj.Renderer is ITwoKeyRenderer twoKeyRenderer)
                {
                    twoKeyRenderer.SetDataToStart(renderObj, commonBlock);
                    (sharedBlockOwner as CGameCtnMediaBlock.IHasTwoKeys).Start = TimeSingle.FromMilliseconds((long)AnimationTimeMillis + animationSettings.AnimationOffsetMillis);
                }
                objectsInScene[renderObj] = (commonTrack, commonBlock, idx);
                blockToRenderObjects[commonBlock].Add(renderObj);

                requiresKeyFrame.Add(commonBlock);
            }
            else
            {
                RegisterObject(renderObj);
            }
        }

        // register hierarchy
        foreach (var subObject in obj.SubObjects)
        {
            RegisterSubObject(subObject);
        }

    }
    bool TryFindSharedBlockOwner(RenderObject obj, out RenderObject sharedBlockOwner)
    {
        // 1) Explicit group (strongest rule)
        if (obj.BlockShareId is int groupId)
            return blockShareIdToRenderObject.TryGetValue(groupId, out sharedBlockOwner!);

        // 2) Fallback: blockShareMode
        switch (obj.BlockShareMode)
        {
            case BlockShareMode.ToParent:
            case BlockShareMode.Hierarchy:
                {
                    var parent = obj.Parent;
                    while (parent != null)
                    {
                        if (parent is RenderObject renderParent 
                            && (renderParent.BlockShareMode == BlockShareMode.FromChildren || renderParent.BlockShareMode == BlockShareMode.Hierarchy)
                            && renderParent.Renderer.CanShareBlockWith(renderParent, obj))
                        {
                            sharedBlockOwner = renderParent;
                            return true;
                        }
                        parent = parent.Parent;
                    }
                    sharedBlockOwner = null!;
                    return false;
                }
            default:
                sharedBlockOwner = null!;
                return false;
        }
    }
   
    void GenerateKeyFrame(CGameCtnMediaBlock block)
    {
        GenerateKeyFrame(block, nextKeyFrameTimeMillis);
    }
    void GenerateKeyFrame(CGameCtnMediaBlock block, ulong keyFrameTime)
    {
        IKey key = null;
        foreach (var obj in blockToRenderObjects[block])
        {
            var (_, _, idx) = objectsInScene[obj];
            if (obj.Renderer is IKeysRenderer keysRenderer)
            {
                // generate Key in first obj
                if (key == null)
                {
                    key = keysRenderer.CreateAndAddEmptyKey(block);
                    key.Time = TimeSingle.FromMilliseconds((long)keyFrameTime + animationSettings.AnimationOffsetMillis);
                }
                // generate keyframe data for obj
                keysRenderer.SetKeyFrameData(obj, block, key, idx, GetPPEffectData(obj));
            }
            else if (obj.Renderer is ITwoKeyRenderer twoKeyRenderer)
            {
                twoKeyRenderer.SetDataToEnd(obj, block);
                (block as CGameCtnMediaBlock.IHasTwoKeys).End = TimeSingle.FromMilliseconds((long)keyFrameTime + animationSettings.AnimationOffsetMillis);
            }
        }


    }
    PostProcessingEffectData GetPPEffectData(RenderObject obj)
    {
        var ndcEffects = postProcessingEffectsNDC.Where(kv => LayerManager.IsInLayer(obj.LayerMask, kv.Key))
            .SelectMany(kv => kv.Value)
            .Distinct()
            .ToArray();
        var worldEffects = postProcessingEffectsWorld.Where(kv => LayerManager.IsInLayer(obj.LayerMask, kv.Key))
            .SelectMany(kv => kv.Value)
            .Distinct()
            .ToArray();
        return new PostProcessingEffectData(worldEffects, ndcEffects);
    }

    bool TryGetKeyFrameCount(CGameCtnMediaBlock block, out int keyFrameCount)
    {
        if (block is IHasKeys hasKeys)
        {
            keyFrameCount = hasKeys.Keys.Count();
            return true;
        }else if(block is IHasTwoKeys twoKeys)
        {
            keyFrameCount = twoKeys.Start.TotalMilliseconds == twoKeys.End.TotalMilliseconds ? 1 : 2;
            return true;
        }
        keyFrameCount = -1;
        return false;
    }

    bool IsInScene(MediaObject obj) => objectsInScene.ContainsKey(obj);

    void SetHierarchyRequiresKeyFrame(MediaObject obj)
    {
        if (obj is RenderObject renderObj)
        {
            requiresKeyFrame.Add(objectsInScene[renderObj].block);
        }
        foreach (var subObject in obj.SubObjects)
        {
            SetHierarchyRequiresKeyFrame(subObject);
        }
    }

    void UpdateTrackOrder()
    {
        var blockOrder = blockToRenderObjects.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Min(o => o.Order));

        var trackOrder = tracks.ToDictionary(track => track, track => track.Blocks.Min(block => blockOrder[block]));

        clip.Tracks.Sort((t1, t2) => {
            if (tracks.Contains(t1) && tracks.Contains(t2))
                return trackOrder[t1].CompareTo(trackOrder[t2]);
            else return 0;
        });
    }

    void UpdateTrackCycling()
    {
        const uint cycleChunkId = 50823173;

        foreach (var track in clip.Tracks.Where(t => t.IsCycling))
        {
            if (track.Blocks.FirstOrDefault() is CGameCtnMediaBlock.IHasKeys firstBlock && track.Blocks.LastOrDefault() is CGameCtnMediaBlock.IHasKeys lastBlock)
            {
                var chunkBase = track.Chunks.Get(cycleChunkId) as CGameCtnMediaTrack.Chunk03078005;
                track.Chunks.Remove(cycleChunkId);
                var cycleChunk = track.CreateChunk<CGameCtnMediaTrack.Chunk03078005>();
                cycleChunk.Version = chunkBase.Version;
                track.IsCycling = true;
                track.RepeatingSegmentStart = firstBlock.Keys.FirstOrDefault()?.Time ?? default;
                track.RepeatingSegmentEnd = lastBlock.Keys.LastOrDefault()?.Time ?? default;
                if (track.RepeatingSegmentStart.Value.TotalMilliseconds < 0)
                    Logger.Warn($"Cycling will not work on track {track.Name} because it starts before time=0");
            }
        }
    }

    private void CompleteAndHideKeyframesForUnfinishedBlocks()
    {
        // Create keyframes for objects which only have 1
        foreach (var block in blockToRenderObjects.Keys)
        {
            if (TryGetKeyFrameCount(block, out var keyCount))
            {
                if (keyCount == 0)
                {
                    GenerateKeyFrame(block, 0);
                    if (hiddenInEditorTimeline.Contains(block))
                        GenerateKeyFrame(block, 0);
                    else
                        GenerateKeyFrame(block, (ulong)animationSettings.FallbackAnimationTimeSeconds * 1000);
                }
                else if (keyCount == 1)
                {
                    if (block is IHasKeys hasKeys)
                    {
                        ulong lastKeyframeTime = (ulong)MediaTrackerUtils.GetLastKeyInBlock(block as IHasKeys).Time.TotalMilliseconds;
                        if (hiddenInEditorTimeline.Contains(block))
                            GenerateKeyFrame(block, lastKeyframeTime);
                        else
                            GenerateKeyFrame(block, lastKeyframeTime + (ulong)animationSettings.FallbackAnimationTimeSeconds * 1000);
                    }
                    else if (block is IHasTwoKeys twoKeys)
                    {
                        ulong lastKeyframeTime = (ulong)twoKeys.Start.TotalMilliseconds;
                        if (hiddenInEditorTimeline.Contains(block))
                            GenerateKeyFrame(block, lastKeyframeTime);
                        else
                            GenerateKeyFrame(block, lastKeyframeTime + (ulong)animationSettings.FallbackAnimationTimeSeconds * 1000);
                    }
                   
                }
                // This will not make it invisible in editor timeline!
                //else if(keyCount > 1)
                //{
                //    if (hiddenInEditorTimeline.Contains(block))
                //    {
                //        ulong lastKeyframeTime = (ulong)MediaTrackerUtils.GetLastKeyInBlock(block as IHasKeys).Time.TotalMilliseconds;
                //        GenerateKeyFrame(block, lastKeyframeTime);
                //    }
                //}
            }
        }
    }

 

    #endregion

    public void Animate(CGameCtnMediaClip clip, SceneAnimationSettings settings, ISceneScript sceneBuilder)
    {
        objects.Clear();
        this.clip = clip;
        animationSettings = settings;
        ResetScreenPositionFunction();

        sceneBuilder.Build(this);

        CompleteAndHideKeyframesForUnfinishedBlocks();

        if (animationSettings.UpdateTrackOrder)
            UpdateTrackOrder();
        
        UpdateTrackCycling();
    }


    public event Action<SceneTimeline> PreAnimationUpdateTick;
    public event Action<SceneTimeline> PostAnimationUpdateTick;
}

public interface ISceneScript
{
    void Build(SceneTimeline scene);

    static Clip CreateClip<T>(string clipOutPath) where T : ISceneScript, new()
        => CreateClip<T>(clipOutPath, SceneAnimationSettings.Default);

    static Clip CreateClip<T>(string clipOutPath, SceneAnimationSettings sceneAnimSettings) where T : ISceneScript, new()
    {
        var blockTemplates = MediaTrackerUtils.CreateBlockTemplates();

        var clip = new Clip() { SavePath = clipOutPath };
        clip.Create(MediaTrackerUtils.DeepCopyClip(blockTemplates.Clip));

        CreateClip<T>(clip, sceneAnimSettings);

        return clip;

    }
    static void CreateClip<T>(Clip clip) where T : ISceneScript, new()
        => CreateClip<T>(clip, SceneAnimationSettings.Default);
    static void CreateClip<T>(Clip clip, SceneAnimationSettings sceneAnimSettings) where T : ISceneScript, new()
    {
        var sceneBuilder = new T();
        CreateClip(sceneBuilder, clip, sceneAnimSettings);
    }

    static void CreateClip<T>(T sceneScript, Clip clip) where T : ISceneScript
         => CreateClip<T>(sceneScript, clip, SceneAnimationSettings.Default);
    static void CreateClip<T>(T sceneScript, Clip clip, SceneAnimationSettings sceneAnimSettings) where T : ISceneScript
        => CreateClip<T>(sceneScript, clip.MediaClip, sceneAnimSettings);
    static void CreateClip<T>(T sceneScript, CGameCtnMediaClip clip) where T : ISceneScript
        => CreateClip<T>(sceneScript, clip, SceneAnimationSettings.Default);
    static void CreateClip<T>(T sceneScript, CGameCtnMediaClip clip, SceneAnimationSettings sceneAnimSettings) where T : ISceneScript
    {
        var blockTemplates = MediaTrackerUtils.CreateBlockTemplates();
        var scene = new SceneTimeline()
        {
            BlockTemplates = blockTemplates,
        };
        var sceneBuilder = sceneScript;
        scene.Animate(clip, sceneAnimSettings, sceneBuilder);
    }
}
public abstract class SceneBuilder : ISceneScript
{
    protected SceneTimeline scene { get; private set; } = null!;
    public void Build(SceneTimeline scene)
    {
        this.scene = scene;
        Build();
    }
    protected abstract void Build();
}
public class GenericSceneBuilder : SceneBuilder
{
    private readonly Action<SceneTimeline> buildAction;

    public GenericSceneBuilder(Action<SceneTimeline> buildAction)
    {
        this.buildAction = buildAction;
    }

    protected override void Build()
    {
        buildAction(scene);
    }
}