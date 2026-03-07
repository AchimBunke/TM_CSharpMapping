using GBX.NET;
using GBX.NET.Engines.Game;
using System.Numerics;

namespace TM_GenericMapping.Common;


public interface IRenderer
{
    public CGameCtnMediaBlock CreateEmptyBlock(BlockTemplates templates);
    public IKey CreateAndAddEmptyKey(CGameCtnMediaBlock block);

    public void SetKeyFrameData(RenderObject obj, CGameCtnMediaBlock block, IKey key, int idx, RenderData renderData, PostProcessingEffectData postProcessingEffectData);

    public bool CanShareBlockWith(RenderObject obj, MediaObject other);

    public abstract int AddRenderDataToBlock(RenderObject obj, CGameCtnMediaBlock block);
    public Vector3 DefaultPolygonNormal { get; }
}
public interface IRenderer<T> : IRenderer where T : RenderObject
{
    public void SetKeyFrameData(T obj, CGameCtnMediaBlock block, IKey key, int idx, RenderData renderData, PostProcessingEffectData postProcessingEffectData);

    public bool CanShareBlockWith(T obj, MediaObject other);

    public abstract int AddRenderDataToBlock(T obj, CGameCtnMediaBlock block);
}

public abstract class TriangleRenderer : IRenderer<TriangleObject>
{
    public HashSet<PostProcessingEffect> PostProcessingEffectsWorld { get; init; } = [];

    public virtual Vector3 DefaultPolygonNormal => Vector3.UnitZ;

    public bool CanShareBlockWith(TriangleObject obj, MediaObject other)
    {
        return (other is RenderObject ro) && obj.Renderer.GetType() == ro.Renderer.GetType();
    }

    public IKey CreateAndAddEmptyKey(CGameCtnMediaBlock block)
    {
        var key = new CGameCtnMediaBlockTriangles.Key(block as CGameCtnMediaBlockTriangles);
        (block as CGameCtnMediaBlockTriangles).Keys.Add(key);
        return key;
    }

    public abstract CGameCtnMediaBlock CreateEmptyBlock(BlockTemplates templates);


    public virtual int AddRenderDataToBlock(TriangleObject obj, CGameCtnMediaBlock block)
    {
        var triangleBlock = block as CGameCtnMediaBlockTriangles;
        int idx = triangleBlock.Vertices.Length;
        Int3 triangleOffset = (idx, idx, idx);
        triangleBlock.Vertices = triangleBlock.Vertices.Concat(obj.Colors.Select(c => new Vec4(c.X, c.Y, c.Z, c.W))).ToArray();
        triangleBlock.Triangles = triangleBlock.Triangles.Concat(obj.Triangles.Select(t => t + triangleOffset)).ToArray();
        return idx;
    }

    protected Vector3 ApplyGlobalWorldSpacePostProcessingEffects(Vector3 worldV, PostProcessingEffectData postProcessingEffectData)
    {
        if (postProcessingEffectData.WorldSpaceEffects.Length == 0)
            return worldV;
        foreach (var effect in postProcessingEffectData.WorldSpaceEffects.ToArray())
        {
            worldV = effect.Transform(worldV);
        }
        return worldV;
    }
    protected Vector3 ApplyLocalWorldSpacePostProcessingEffects(TriangleObject obj, Vector3 v)
    {
        foreach (var effect in obj.LocalWorldSpacePostProcessingEffects)
        {
            v = effect.Transform(v);
        }
        return v;
    }
    public Vector3 ApplyRenderingPostProcessing(Vector3 worldV)
    {
        if (PostProcessingEffectsWorld.Count == 0)
            return worldV;
        foreach (var effect in PostProcessingEffectsWorld)
        {
            worldV = effect.Transform(worldV);
        }
        return worldV;
    }


    public IKey CreateAndAddEmptyKey(RenderObject obj, CGameCtnMediaBlock block)
        => CreateAndAddEmptyKey((TriangleObject)obj, block);

    public void SetKeyFrameData(RenderObject obj, CGameCtnMediaBlock block, IKey key, int idx, RenderData renderData, PostProcessingEffectData postProcessingEffectData)
        => SetKeyFrameData((TriangleObject)obj, block, key, idx, renderData, postProcessingEffectData);

    public bool CanShareBlockWith(RenderObject obj, MediaObject other)
        => CanShareBlockWith((TriangleObject)obj, other);

    public int AddRenderDataToBlock(RenderObject obj, CGameCtnMediaBlock block)
        => AddRenderDataToBlock((TriangleObject)obj, block);
    public abstract void SetKeyFrameData(TriangleObject obj, CGameCtnMediaBlock block, IKey key, int idx, RenderData renderData, PostProcessingEffectData postProcessingEffectData);
}

/// <summary>
/// Renders TriangleObjects to 2DTriangles 
/// </summary>
public class Triangle2DRenderer : TriangleRenderer
{
    public override Vector3 DefaultPolygonNormal => Vector3.UnitZ;
    public override CGameCtnMediaBlock CreateEmptyBlock(BlockTemplates templates)
    {
        var block = MediaTrackerUtils.DeepCopyBlockTriangles2D(templates.Triangles2D);
        block.Keys.Clear();
        return block;
    }

    static Matrix4x4 MediaTrackerTransformationMatrix =
    Matrix4x4.CreateScale(1, (16f / 9f), 1) *
    Matrix4x4.CreateTranslation(0, 0, 0);

    private Matrix4x4 GetNDCTransformation(RenderData renderData)
    {
        var view = Matrix4x4.CreateLookAt(renderData.CameraPosition, renderData.CameraLookAt == default ? Vector3.UnitZ : renderData.CameraLookAt, Vector3.UnitY);
        if (renderData.Mode == CameraMode.Orthographic)
            return view * Matrix4x4.CreateOrthographic(renderData.ViewBox.X, renderData.ViewBox.Y, 0.01f, renderData.ViewBox.Z);
        else
            return view * Matrix4x4.CreatePerspectiveFieldOfView(renderData.FOV, renderData.ViewBox.X / renderData.ViewBox.Y, 0.001f, renderData.ViewBox.Z);
    }
    private Vector3 ApplyLocalNDCPostProcessingEffects(TriangleObject obj, Vector3 v)
    {
        foreach (var effect in obj.LocalNDCPostProcessingEffects)
        {
            v = effect.Transform(v);
        }
        return v;
    }
    private Vector3 ApplyGlobalNDCPostProcessingEffects(Vector3 ndcV, PostProcessingEffectData postProcessingEffectData)
    {

        if (postProcessingEffectData.NdcSpaceEffects.Length == 0)
            return ndcV;

        foreach (var effect in postProcessingEffectData.NdcSpaceEffects.ToArray())
        {
            ndcV = effect.Transform(ndcV);
        }
        return ndcV;
    }
   
    public override void SetKeyFrameData(TriangleObject obj, CGameCtnMediaBlock block, IKey key, int idx, RenderData renderData, PostProcessingEffectData postProcessingEffectData)
    {
        var triangleKey = key as CGameCtnMediaBlockTriangles.Key;
        for (int i = 0; i < obj.Vertices.Length; ++i)
        {
            var v = ToMediaTrackerCoordinates(obj, obj.Vertices[i], renderData, postProcessingEffectData);
            triangleKey.Positions[idx + i] = new Vec3(v.X, v.Y, v.Z);
        }
    }

    protected virtual Vector3 ToMediaTrackerCoordinates(TriangleObject obj, Vector3 vec3, RenderData renderData, PostProcessingEffectData postProcessingEffectData)
    {
        // Local → World
        var worldSpace = Vector3.Transform(vec3, obj.LocalToWorldTRS);
        worldSpace = ApplyGlobalWorldSpacePostProcessingEffects(worldSpace, postProcessingEffectData);
        worldSpace = ApplyLocalWorldSpacePostProcessingEffects(obj, worldSpace);

        // World → Clip space
        var ndcMatrix = GetNDCTransformation(renderData);
        Vector4 clipSpace = Vector4.Transform(new Vector4(worldSpace, 1f), ndcMatrix);

        // Perspective divide
        Vector3 ndcSpace = new Vector3(clipSpace.X / clipSpace.W, clipSpace.Y / clipSpace.W, clipSpace.Z / clipSpace.W);

        // Post-processing in NDC
        ndcSpace = ApplyGlobalNDCPostProcessingEffects(ndcSpace, postProcessingEffectData);
        ndcSpace = ApplyLocalNDCPostProcessingEffects(obj, ndcSpace);

        // NDC → MediaTracker
        var mediaTrackerSpace = Vector3.Transform(ndcSpace, MediaTrackerTransformationMatrix);

        return mediaTrackerSpace;
    }

}


/// <summary>
/// Renders TriangleObjects to 3DTriangles 
/// </summary>
public class Triangle3DRenderer : TriangleRenderer
{
    public override Vector3 DefaultPolygonNormal => Vector3.UnitY;
    /// <summary>
    /// Makes 3D Triangles move with your car.
    /// </summary>
    public bool RelativeToPlayer { get; init; } = false;
    /// <summary>
    /// RelativeToPlayer triangles will disappear when player does not look in correct direction.
    /// This property will create additional vertices preventing triangle idsappearing.
    /// </summary>
    public bool EnsureVisibilityWhenRelativeToPlayer { get; init; } = false;
    /// <summary>
    /// Defines the distance of additional triangles when using EnsureVisibilityWhenRelativeToPlayer.
    /// </summary>
    public float EnsureanceVerticesDistance { get; init; } = 10000f;
    /// <summary>
    /// Not sure if 8 triangles is safer!
    /// </summary>
    public enum Ensurance
    {
        _4,
        _8,
    }
    public Ensurance EnsuranceMode { get; init; } = Ensurance._4;
    public override CGameCtnMediaBlock CreateEmptyBlock(BlockTemplates templates)
    {
        var block = MediaTrackerUtils.DeepCopyBlockTriangles3D(templates.Triangles3D);
        block.Keys.Clear();
        if (RelativeToPlayer)
        {
            block.Chunks.Remove<CGameCtnMediaBlockTriangles3D.Chunk03029002>();
            var chunk = block.CreateChunk<CGameCtnMediaBlockTriangles3D.Chunk03029002>();
            chunk.U01 = 0;
        }
        return block;
    }
    public override int AddRenderDataToBlock(TriangleObject obj, CGameCtnMediaBlock block)
    {
        if(RelativeToPlayer && EnsureVisibilityWhenRelativeToPlayer)
        {
            var triangleBlock = block as CGameCtnMediaBlockTriangles;
            int idx = triangleBlock.Vertices.Length;
            if (idx != 0) // only add visibility vertices once per block
                return base.AddRenderDataToBlock(obj, block);

            Int3 triangleOffset = (idx, idx, idx);
            int ensuranceVerticesCount = EnsuranceMode switch
            {
                Ensurance._4 => 4,
                Ensurance._8 => 8,
                _ => throw new NotImplementedException(),
            };
            triangleBlock.Vertices = [
                ..triangleBlock.Vertices.Concat(obj.Colors.Select(c => new Vec4(c.X, c.Y, c.Z, c.W))).ToArray(),
                ..Enumerable.Repeat(new Vec4(), ensuranceVerticesCount)];
            triangleBlock.Triangles = triangleBlock.Triangles.Concat(obj.Triangles.Select(t => t + triangleOffset)).ToArray();
            return idx;
        }
        else
        {
            return base.AddRenderDataToBlock(obj, block);
        }   
    }
    public override void SetKeyFrameData(TriangleObject obj, CGameCtnMediaBlock block, IKey key, int idx, RenderData renderData, PostProcessingEffectData postProcessingEffectData)
    {
        var triangleKey = key as CGameCtnMediaBlockTriangles.Key;
        for (int i = 0; i < obj.Vertices.Length; ++i)
        {
            var v = ToWorldCoordinates(obj, obj.Vertices[i], renderData, postProcessingEffectData);
            triangleKey.Positions[idx + i] = new Vec3(v.X, v.Y, v.Z);
        }
        if (RelativeToPlayer && EnsureVisibilityWhenRelativeToPlayer && idx == 0)
        {
            switch (EnsuranceMode)
            {
                case Ensurance._4:
                    {
                        triangleKey.Positions[idx + obj.Vertices.Length + 0] = new Vec3(-EnsureanceVerticesDistance, 0, -EnsureanceVerticesDistance);
                        triangleKey.Positions[idx + obj.Vertices.Length + 1] = new Vec3(-EnsureanceVerticesDistance, 0, EnsureanceVerticesDistance);
                        triangleKey.Positions[idx + obj.Vertices.Length + 2] = new Vec3(EnsureanceVerticesDistance, 0, -EnsureanceVerticesDistance);
                        triangleKey.Positions[idx + obj.Vertices.Length + 3] = new Vec3(EnsureanceVerticesDistance, 0, EnsureanceVerticesDistance);
                    }
                    break;
                case Ensurance._8:
                    {
                        triangleKey.Positions[idx + obj.Vertices.Length + 0] = new Vec3(-EnsureanceVerticesDistance, 0, -EnsureanceVerticesDistance);
                        triangleKey.Positions[idx + obj.Vertices.Length + 1] = new Vec3(-EnsureanceVerticesDistance, 0, EnsureanceVerticesDistance);
                        triangleKey.Positions[idx + obj.Vertices.Length + 2] = new Vec3(EnsureanceVerticesDistance, 0, -EnsureanceVerticesDistance);
                        triangleKey.Positions[idx + obj.Vertices.Length + 3] = new Vec3(EnsureanceVerticesDistance, 0, EnsureanceVerticesDistance);

                        triangleKey.Positions[idx + obj.Vertices.Length + 4] = new Vec3(0, 0, -EnsureanceVerticesDistance);
                        triangleKey.Positions[idx + obj.Vertices.Length + 5] = new Vec3(0, 0, EnsureanceVerticesDistance);
                        triangleKey.Positions[idx + obj.Vertices.Length + 6] = new Vec3(-EnsureanceVerticesDistance, 0, 0);
                        triangleKey.Positions[idx + obj.Vertices.Length + 7] = new Vec3(EnsureanceVerticesDistance, 0, 0);
                    }
                    break;
                default: throw new NotImplementedException();
            }
           
        }
    }
    protected virtual Vector3 ToWorldCoordinates(TriangleObject obj, Vector3 vec3, RenderData renderData, PostProcessingEffectData postProcessingEffectData)
    {
        // Local → World
        var worldSpace = Vector3.Transform(vec3, obj.LocalToWorldTRS);
        worldSpace = ApplyGlobalWorldSpacePostProcessingEffects(worldSpace, postProcessingEffectData);
        worldSpace = ApplyRenderingPostProcessing(worldSpace);
        worldSpace = ApplyLocalWorldSpacePostProcessingEffects(obj, worldSpace);
        return worldSpace;
    }
}


public static class Rendering
{
    public static IRenderer DefaultTriangleRenderer = new Triangle3DRenderer();
}