using GBX.NET;
using GBX.NET.Engines.Game;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using TM_GenericMapping.Common;
using TM_GenericMapping.IO;
using TmEssentials;
using static GBX.NET.Engines.Game.CGameCtnMediaBlock;

namespace TM_GenericMapping.MediaTracker;

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
    bool UpdateTrackOrder)
{
    public static SceneAnimationSettings Default => new SceneAnimationSettings
    {
        AnimationTickRateMillis = 20,
        MinKeyFrameTickRateMillis = 100,
        FallbackAnimationTimeSeconds = 1f,
        UpdateTrackOrder = true,
    };
}
public record class KeyFrameData()
{
    public required ulong KeyFrameTickRateMillis { get; set; } = 100;
    public ulong NextKeyFrameTargetMillis { get; set; } = 0;
}
public enum CameraMode
{
    Orthographic,
    Perspective
}
public record struct RenderData(Vector3 ViewBox, CameraMode Mode, Vector3 CameraPosition, Vector3 CameraLookAt, float FOV)
{
    public static RenderData Default
        => new RenderData(new Vector3(10, 10, 10), CameraMode.Orthographic, Vector3.Zero, Vector3.UnitZ, MathF.PI / 2f);
}
public record BlockTemplates(
    CGameCtnMediaClip Clip,
    CGameCtnMediaTrack Track,
    CGameCtnMediaBlockTriangles2D Triangles2D,
    CGameCtnMediaBlockTriangles3D Triangles3D,
    CGameCtnMediaBlockText Text,
    CGameCtnMediaBlockImage Image);

public enum ScreenPosition
{
    TOP,
    BOTTOM,
    LEFT,
    RIGHT,
    CENTER,
    TOP_LEFT,
    TOP_RIGHT,
    BOTTOM_LEFT,
    BOTTOM_RIGHT,
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
public static class ScreenPositionExtensions
{
    public static Func<ScreenPosition, Vector3> ScreenToVector3Function = (_) => throw new NotImplementedException("Function not set");
    public static Vector3 ToVector3(this ScreenPosition screenPos) => ScreenToVector3Function(screenPos);
}
public static class WorldPositionExtensions
{
    public static Vector3 StadiumSurfaceOffset = new Vector3(0, 8, 0);
    public static Bounds StadiumBounds = new Bounds() { Center = new Vector3(768, 128, 768), Size = new Vector3(1536, 256, 1536) };
    public static Bounds StadiumSurfaceBounds = new Bounds() { Center = new Vector3(768, 132, 768), Size = new Vector3(1536, 248, 1536) };
    public static Vector3 StadiumSurfaceCenter => StadiumSurfaceBounds.Center with { Y = StadiumSurfaceOffset.Y };
}

public abstract class Scene
{
    #region Publics
    public BlockTemplates BlockTemplates { get; init; }
    public RenderData RenderData { get; init; }
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
    #endregion

    #region Scene API
    public IEnumerable<MediaObject> Objects => objects;
    protected void Add(params ReadOnlySpan<MediaObject> newObjects)
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
    protected void AddSubObjects(MediaObject obj, params ReadOnlySpan<MediaObject> subObjects)
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
    protected void Wait(float timeSeconds)
    {
        ulong targetAnimationTimeMillis = AnimationTimeMillis + (ulong)(timeSeconds * 1000f);
        while (AnimationTimeMillis < targetAnimationTimeMillis)
        {
            AnimationUpdate();
        }
    }
    protected void RequireKeyFrame(params ReadOnlySpan<MediaObject> objects)
    {
        foreach (var obj in objects)
        {
            EnsureAdded(obj);
            SetHierarchyRequiresKeyFrame(obj);
        }
    }
    protected void AnimationStep()
    {
        AnimationUpdate();
    }

    protected void Delayed(float delaySeconds, Action action)
    {
        ulong targetTimeMillis = AnimationTimeMillis + (ulong)(delaySeconds * 1000f);
        delayedActions.Add(targetTimeMillis, action);
    }

    protected void StepToNextKeyFrameUpdate()
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
    protected void StepToNextKeyFrameUpdate(MediaObject obj)
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
    protected void WaitAnimationEnd()
    {
        while (RequiresKeyFrameUpdates || HasActiveAnimations || HasDelayedActions)
        {
            AnimationUpdate();
        }
       
    }
    void CompleteKeyframesForUnfinishedBlocks()
    {
        // Create keyframes for objects which only have 1
        foreach (var block in blockToRenderObjects.Keys)
        {
            if (TryGetKeyFrameCount(block, out var keyCount))
            {
                if (keyCount == 0)
                {
                    GenerateKeyFrame(block, 0);
                    GenerateKeyFrame(block, (ulong)animationSettings.FallbackAnimationTimeSeconds * 1000);
                }
                else if (keyCount == 1)
                {
                    ulong lastKeyframeTime = (ulong)MediaTrackerUtils.GetLastKeyInBlock(block as IHasKeys).Time.Milliseconds;
                    GenerateKeyFrame(block, lastKeyframeTime + (ulong)animationSettings.FallbackAnimationTimeSeconds * 1000);
                }
            }
        }
    }

    protected void ForceStopAllAnimations()
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
    protected void StopAnimation(params ReadOnlySpan<MediaObject> targets)
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

    protected void SetPosition(MediaObject obj, Vector3 position, Space space = Space.Local)
    {
        obj.SetPosition(position, space);
        if (IsInScene(obj))
            SetHierarchyRequiresKeyFrame(obj);
    }
    protected void SetPosition(MediaObject obj, ScreenPosition moveLocation, Space space = Space.Local) => SetPosition(obj, GetPosition(moveLocation), space);
    protected void Translate(MediaObject obj, Vector3 position, Space space = Space.Local)
    {
        obj.Translate(position, space);
        if (IsInScene(obj))
            SetHierarchyRequiresKeyFrame(obj);
    }
    protected void SetScale(MediaObject obj, Vector3 scale)
    {
        obj.LocalScale = scale;
        if (IsInScene(obj))
            SetHierarchyRequiresKeyFrame(obj);
    }
    protected void SetScale(MediaObject obj, float scale) => SetScale(obj, Vector3.Create(scale));
    protected void SetRotation(MediaObject obj, Quaternion rotation, Space space = Space.Local)
    {
        obj.SetRotation(rotation, space);
        if (IsInScene(obj))
            SetHierarchyRequiresKeyFrame(obj);
    }
    protected void Rotate(MediaObject obj, Quaternion rotation, Space space = Space.Local)
    {
        obj.Rotate(rotation, space);
        if (IsInScene(obj))
            SetHierarchyRequiresKeyFrame(obj);
    }

    protected void AddLocalNDCPostProcessingEffects(RenderObject obj, params ReadOnlySpan<PostProcessingEffect> effects)
    {
        foreach (var effect in effects)
        {
            obj.AddLocalNDCPostProcessingEffect(effect);
            if (IsInScene(obj))
                SetHierarchyRequiresKeyFrame(obj);
        }
    }
    protected void AddLocalWorldSpacePostProcessingEffects(RenderObject obj, params ReadOnlySpan<PostProcessingEffect> effects)
    {
        foreach (var effect in effects)
        {
            obj.AddLocalWorldSpacePostProcessingEffect(effect);
            if (IsInScene(obj))
                SetHierarchyRequiresKeyFrame(obj);
        }
    }

    protected void AddNDCSpacePostProcessingEffect(PostProcessingEffect effect, params ReadOnlySpan<string> layers)
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
    protected void AddWorldSpacePostProcessingEffect(PostProcessingEffect effect, params ReadOnlySpan<string> layers)
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


    protected void Morph<T, T2>(T source, T2 target, Easing type = Easing.Linear, float percent = 1f)
        where T : MediaObject, IMorphable
        where T2 : IMorphable
    {
        GenericTriangle2DObjectMorpher.Morph(source, target, type, percent);
        if (IsInScene(source))
            SetHierarchyRequiresKeyFrame(source);
    }

    protected void Play(bool stopAfter = false, params ReadOnlySpan<IMediaObjectAnimator> newAnimators)
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
    protected void Play(ulong tickRateMillis, ulong keyFrameGenerationTickRateMillis = 0, bool stopAfter = false, params ReadOnlySpan<IMediaObjectAnimator> animators)
    {
        foreach (var animator in animators)
        {
            SetKeyFrameTickRate(tickRateMillis, animator.Target);
            animator.KeyframeGenerationRateMillis = keyFrameGenerationTickRateMillis;
        }
        Play(stopAfter, animators);
    }
    protected void Play(ulong tickRateMillis, params ReadOnlySpan<IMediaObjectAnimator> animators)
        => Play(tickRateMillis, 0, false, animators);
    protected void Play(params ReadOnlySpan<IMediaObjectAnimator> newAnimators)
        => Play(false, newAnimators);

    protected void PlayConcurrent(params ReadOnlySpan<IMediaObjectAnimator> newAnimators)
    {

        foreach (var newAnimator in newAnimators)
        {
            concurrentAnimators.Add(newAnimator);
        }
    }
    protected void PlayConcurrent(ulong tickRateMillis, params ReadOnlySpan<IMediaObjectAnimator> newAnimators)
    {
        foreach (var animator in newAnimators)
        {
            SetKeyFrameTickRate(tickRateMillis, animator.Target);
        }
        PlayConcurrent(newAnimators);
    }


    protected void StopObjectAnimationsAfter(params ReadOnlySpan<IMediaObjectAnimator> animators)
    {
        foreach(var animator in animators)
        {
            stopAnimators.Add(animator);
        }
    }


    protected void SetKeyFrameTickRate(ulong tickRateMillis, params ReadOnlySpan<MediaObject> objects)
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
        ScreenPositionExtensions.ScreenToVector3Function = GetPosition;
    }

    protected void SetTrackCycling(bool cycleTrack = true, params ReadOnlySpan<MediaObject> objects)
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
    protected MediaObject[] GetObjectsFromHierarchy(params ReadOnlySpan<MediaObject> objects)
    {
        List<MediaObject> objs = [];
        foreach(var o in objects)
        {
            objs.Add(o);
            objs.AddRange(GetObjectsFromHierarchy(o.SubObjects.ToArray()));
        }
        return objs.ToArray();
    }

    protected void SetKeepTrackActive(bool keepActive = true, params ReadOnlySpan<MediaObject> objects)
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


    #endregion

    #region Internal
    public Scene()
    {
        BlockTemplates = MediaTrackerUtils.CreateBlockTemplates();
        RenderData = RenderData.Default;
    }

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
        PreAnimationUpdateTick();
        UpdateAnimators();
        UpdateDelayedAction();
        UpdatePostProcessingEffects();
        if (AnimationTimeMillis >= nextKeyFrameTimeMillis)
        {
            GenerateKeyFrames();
            nextKeyFrameTimeMillis += animationSettings.MinKeyFrameTickRateMillis;
        }
        PostAnimationUpdateTick();
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
            if (animator.Update(animationSettings.AnimationTickRateMillis / 1000f))
                SetHierarchyRequiresKeyFrame(animator.Target);
            if (animator.Completed)
                justCompleted.Add(animator.Target);
        }
        foreach (var concurrentAnimator in concurrentAnimators)
        {
            if (concurrentAnimator.Update(animationSettings.AnimationTickRateMillis / 1000f))
                SetHierarchyRequiresKeyFrame(concurrentAnimator.Target);
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
        CGameCtnMediaBlock newBlock = null;
        CGameCtnMediaTrack newTrack = null;
        int idx = -1;
        if (obj is RenderObject renderObj)
        {
            newBlock = renderObj.Renderer.CreateEmptyBlock(BlockTemplates);
            idx = renderObj.Renderer.AddRenderDataToBlock(renderObj, newBlock);
            newTrack = MediaTrackerUtils.DeepCopyTrack(BlockTemplates.Track);
            newTrack.Name = renderObj.Name;
            newTrack.Blocks.Add(newBlock);

            blockToRenderObjects[newBlock] = [renderObj];
            blockToKeyFrameData[newBlock] = new KeyFrameData() { KeyFrameTickRateMillis = animationSettings.MinKeyFrameTickRateMillis };

            clip.Tracks.Add(newTrack);
            tracks.Add(newTrack);

            requiresKeyFrame.Add(newBlock);
        }
        objectsInScene[obj] = (newTrack, newBlock, idx);
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
        if (obj is RenderObject renderObj && renderObj.CanShareBlock && TryFindNearestAvailableSharedParent(renderObj, out var sharedParent))
        {
            var (commonTrack, commonBlock, _) = objectsInScene[sharedParent];
            int idx = renderObj.Renderer.AddRenderDataToBlock(renderObj, commonBlock);
            objectsInScene[renderObj] = (commonTrack, commonBlock, idx);
            blockToRenderObjects[commonBlock].Add(renderObj);

            requiresKeyFrame.Add(commonBlock);
        }
        else
        {
            // Creates 
            RegisterObject(obj);
        }

        // register hierarchy
        foreach (var subObject in obj.SubObjects)
        {
            RegisterSubObject(subObject);
        }

    }
    bool TryFindNearestAvailableSharedParent(RenderObject obj, out RenderObject sharedParent)
    {
        var parent = obj.Parent;
        while (parent != null)
        {
            if (parent is RenderObject renderParent && renderParent.CanShareBlock && renderParent.Renderer.CanShareBlockWith(renderParent, obj))
            {
                sharedParent = renderParent;
                return true;
            }
            parent = parent.Parent;
        }
        sharedParent = null;
        return false;
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
            // generate Key in first obj
            if (key == null)
            {
                key = obj.Renderer.CreateAndAddEmptyKey(block);
                key.Time = TimeSingle.FromMilliseconds(keyFrameTime);
            }
            // generate keyframe data for obj
            obj.Renderer.SetKeyFrameData(obj, block, key, idx, RenderData, GetPPEffectData(obj));
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
    bool TryGetKeyFrameCount(RenderObject obj, out int keyFrameCount)
    {
        if (objectsInScene.TryGetValue(obj, out var val))
        {
            return TryGetKeyFrameCount(val.block, out keyFrameCount);
        }
        keyFrameCount = -1;
        return false;
    }
    bool TryGetKeyFrameCount(CGameCtnMediaBlock block, out int keyFrameCount)
    {
        if (block is IHasKeys hasKeys)
        {
            keyFrameCount = hasKeys.Keys.Count();
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
                cycleChunk.U01 = firstBlock.Keys.FirstOrDefault()?.Time.TotalSeconds ?? 0;
                cycleChunk.U02 = lastBlock.Keys.LastOrDefault()?.Time.TotalSeconds ?? 0;
            }
        }
    }

    protected Vector3 GetPosition(ScreenPosition loc) => loc switch
    {
        ScreenPosition.TOP => new Vector3(0, RenderData.ViewBox.Y / ((16f / 9f) * 2f), 0),
        ScreenPosition.BOTTOM => new Vector3(0, -RenderData.ViewBox.Y / ((16f / 9f) * 2f), 0),
        ScreenPosition.LEFT => new Vector3(-RenderData.ViewBox.X / 2f, 0, 0),
        ScreenPosition.RIGHT => new Vector3(RenderData.ViewBox.X / 2f, 0, 0),
        ScreenPosition.CENTER => Vector3.Zero,
        ScreenPosition.TOP_LEFT => new Vector3(-RenderData.ViewBox.X / 2f, RenderData.ViewBox.Y / ((16f / 9f) * 2f), 0),
        ScreenPosition.TOP_RIGHT => new Vector3(RenderData.ViewBox.X / 2f, RenderData.ViewBox.Y / ((16f / 9f) * 2f), 0),
        ScreenPosition.BOTTOM_LEFT => new Vector3(-RenderData.ViewBox.X / 2f, -RenderData.ViewBox.Y / ((16f / 9f) * 2f), 0),
        ScreenPosition.BOTTOM_RIGHT => new Vector3(RenderData.ViewBox.X / 2f, -RenderData.ViewBox.Y / ((16f / 9f) * 2f), 0),
        _ => throw new NotImplementedException(),
    };

    #endregion

    public void Animate(CGameCtnMediaClip clip, SceneAnimationSettings settings)
    {
        objects.Clear();
        this.clip = clip;
        animationSettings = settings;
        ResetScreenPositionFunction();

        Animate();

        CompleteKeyframesForUnfinishedBlocks();

        if (animationSettings.UpdateTrackOrder)
            UpdateTrackOrder();
        
        UpdateTrackCycling();
    }

   
    protected abstract void Animate();
    protected virtual void PreAnimationUpdateTick()
    {

    }
    protected virtual void PostAnimationUpdateTick()
    {

    }
}
