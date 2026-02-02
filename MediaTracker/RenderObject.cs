using GBX.NET;
using GBX.NET.Engines.Game;
using GBX.NET.Inputs;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;

namespace TM_GenericMapping.Common;

public abstract class RenderObject : MediaObject
{

    public int Order { get; set; } = 0;
    public bool CanShareBlock { get; set; } = false;

    private List<PostProcessingEffect> _localWorldSpacePostProcessingEffects = new();
    public IReadOnlyList<PostProcessingEffect> LocalWorldSpacePostProcessingEffects => _localWorldSpacePostProcessingEffects;
    private List<PostProcessingEffect> _localNDCPostProcessingEffects = new();
    public IReadOnlyList<PostProcessingEffect> LocalNDCPostProcessingEffects => _localNDCPostProcessingEffects;

    protected RenderObject([NotNull] IRenderer renderer) 
    {
        Renderer = renderer ?? Rendering.DefaultTriangleRenderer ?? throw new ArgumentNullException(nameof(renderer));
    }
    protected RenderObject(RenderObject other) : base(other)
    {
        Order = other.Order;
        Renderer = other.Renderer;
        CanShareBlock = other.CanShareBlock;
        _localWorldSpacePostProcessingEffects = other._localWorldSpacePostProcessingEffects.ToList();
        _localNDCPostProcessingEffects = other._localNDCPostProcessingEffects.ToList();
    }

    public IRenderer Renderer { get; set; }

    //public abstract bool CanShareBlockWith(MediaObject other);
    //public abstract CGameCtnMediaBlock CreateEmptyBlock(BlockTemplates templates);
    //public abstract int AddRenderDataToBlock(CGameCtnMediaBlock block);
    //public abstract IKey CreateAndAddEmptyKey(CGameCtnMediaBlock block);
    //public abstract void SetKeyFrameData(CGameCtnMediaBlock block, IKey key, int idx, RenderData renderData, PostProcessingEffectData postProcessingData);

    //protected Matrix4x4 GetNDCTransformation(RenderData renderData) 
    //{
    //    var view = Matrix4x4.CreateLookAt(renderData.CameraPosition, renderData.CameraLookAt == default ? Vector3.UnitZ : renderData.CameraLookAt, Vector3.UnitY);
    //    if (renderData.Mode == CameraMode.Orthographic)
    //        return view * Matrix4x4.CreateOrthographic(renderData.ViewBox.X, renderData.ViewBox.Y, 0.01f, renderData.ViewBox.Z);
    //    else
    //        return view * Matrix4x4.CreatePerspectiveFieldOfView(renderData.FOV, renderData.ViewBox.X / renderData.ViewBox.Y, 0.001f, renderData.ViewBox.Z);
    //}
    //protected Vector3 ToMediaTrackerCoordinates(Vector3 vec3, RenderData renderData, PostProcessingEffectData postProcessingEffectData)
    //{
    //    // Local → World
    //    var worldSpace = Vector3.Transform(vec3, LocalToWorldTRS);
    //    worldSpace = ApplyGlobalWorldSpacePostProcessingEffects(worldSpace, postProcessingEffectData);
    //    worldSpace = ApplyLocalWorldSpacePostProcessingEffects(worldSpace);

    //    // World → Clip space
    //    var ndcMatrix = GetNDCTransformation(renderData);
    //    Vector4 clipSpace = Vector4.Transform(new Vector4(worldSpace, 1f), ndcMatrix);

    //    // Perspective divide
    //    Vector3 ndcSpace = new Vector3(clipSpace.X / clipSpace.W, clipSpace.Y / clipSpace.W, clipSpace.Z / clipSpace.W);

    //    // Post-processing in NDC
    //    ndcSpace = ApplyGlobalNDCPostProcessingEffects(ndcSpace, postProcessingEffectData);
    //    ndcSpace = ApplyLocalNDCPostProcessingEffects(ndcSpace);

    //    // NDC → MediaTracker
    //    var mediaTrackerSpace = Vector3.Transform(ndcSpace, MediaTrackerTransformationMatrix);

    //    return mediaTrackerSpace;
    //}
    //protected Matrix4x4 GetNDCTransformation(RenderData renderData) => Matrix4x4.CreateScale(2f / renderData.ViewBox.X, 2f / renderData.ViewBox.Y, 2f / renderData.ViewBox.Z);// + Matrix4x4.CreateTranslation(-1, -1, -1);
    //protected Vector3 ToMediaTrackerCoordinates(Vector3 vec3, RenderData renderData, PostProcessingEffectData postProcessingEffectData)
    //{

    //    localtransform->viewport coordinates(differs between types of objects(Images => -4; 4)) -> mediaTracker coordinates
    //    var worldSpace = Vector3.Transform(vec3, LocalToWorldTRS);
    //    var worldSpaceGlobalPP = ApplyGlobalWorldSpacePostProcessingEffects(worldSpace, postProcessingEffectData);
    //    var worldSpacePP = ApplyLocalWorldSpacePostProcessingEffects(worldSpaceGlobalPP);
    //    var ndcSpace = Vector3.Transform(worldSpacePP, GetNDCTransformation(renderData));
    //    var ndcSpaceGlobalPP = ApplyGlobalNDCPostProcessingEffects(ndcSpace, postProcessingEffectData);
    //    var ndcSpacePP = ApplyLocalNDCPostProcessingEffects(ndcSpaceGlobalPP);
    //    var mediaTrackerSpace = Vector3.Transform(ndcSpacePP, MediaTrackerTransformationMatrix);
    //    return mediaTrackerSpace;
    //}
    //private Vector3 ApplyGlobalNDCPostProcessingEffects(Vector3 ndcV, PostProcessingEffectData postProcessingEffectData)
    //{

    //    if (postProcessingEffectData.NdcSpaceEffects.Length == 0)
    //        return ndcV;

    //    foreach (var effect in postProcessingEffectData.NdcSpaceEffects.ToArray())
    //    {
    //        ndcV = effect.Transform(ndcV);
    //    }
    //    return ndcV;
    //}
    //private Vector3 ApplyGlobalWorldSpacePostProcessingEffects(Vector3 worldV, PostProcessingEffectData postProcessingEffectData)
    //{
    //    if (postProcessingEffectData.WorldSpaceEffects.Length == 0)
    //        return worldV;
    //    foreach (var effect in postProcessingEffectData.WorldSpaceEffects.ToArray())
    //    {
    //        worldV = effect.Transform(worldV);
    //    }
    //    return worldV;
    //}
    //private Vector3 ApplyLocalNDCPostProcessingEffects(Vector3 v)
    //{
    //    foreach (var effect in _localNDCPostProcessingEffects)
    //    {
    //        v = effect.Transform(v);
    //    }
    //    return v;
    //}
    //private Vector3 ApplyLocalWorldSpacePostProcessingEffects(Vector3 v)
    //{
    //    foreach (var effect in _localWorldSpacePostProcessingEffects)
    //    {
    //        v = effect.Transform(v);
    //    }
    //    return v;
    //}
    public void AddLocalNDCPostProcessingEffect(PostProcessingEffect effect)
    {
        _localNDCPostProcessingEffects.Add(effect);
    }
    public void AddLocalWorldSpacePostProcessingEffect(PostProcessingEffect effect)
    {
        _localWorldSpacePostProcessingEffects.Add(effect);
    }
}

