using GBX.NET;
using GBX.NET.Engines.Game;
using System.Numerics;
using TM_GenericMapping.MediaTracker;
using TmEssentials;

namespace TM_GenericMapping.Common;

public interface IRenderer
{
    public CGameCtnMediaBlock CreateEmptyBlock(BlockTemplates templates);
    public bool CanShareBlockWith(RenderObject obj, MediaObject other);
}
public interface ITwoKeyRenderer : IRenderer
{
    public void SetDataToStart(MediaObject obj, CGameCtnMediaBlock block);
    public void SetDataToEnd(MediaObject obj, CGameCtnMediaBlock block);
}
public interface IKeysRenderer : IRenderer
{
    public IKey CreateAndAddEmptyKey(CGameCtnMediaBlock block);

    public void SetKeyFrameData(RenderObject obj, CGameCtnMediaBlock block, IKey key, int idx, PostProcessingEffectData postProcessingEffectData);


    public abstract int AddRenderDataToBlock(RenderObject obj, CGameCtnMediaBlock block);
}
public interface IKeysRenderer<T> : IKeysRenderer where T : RenderObject
{
    public void SetKeyFrameData(T obj, CGameCtnMediaBlock block, IKey key, int idx, PostProcessingEffectData postProcessingEffectData);

    public bool CanShareBlockWith(T obj, MediaObject other);

    public abstract int AddRenderDataToBlock(T obj, CGameCtnMediaBlock block);
}
public abstract class KeysRendererBase<T> : IKeysRenderer<T> where T : RenderObject
{
    public abstract CGameCtnMediaBlock CreateEmptyBlock(BlockTemplates templates);
    public abstract void SetKeyFrameData(T obj, CGameCtnMediaBlock block, IKey key, int idx, PostProcessingEffectData postProcessingEffectData);
    public abstract IKey CreateAndAddEmptyKey(CGameCtnMediaBlock block);
    public abstract bool CanShareBlockWith(T obj, MediaObject other);
    public abstract int AddRenderDataToBlock(T obj, CGameCtnMediaBlock block);
    public void SetKeyFrameData(RenderObject obj, CGameCtnMediaBlock block, IKey key, int idx, PostProcessingEffectData postProcessingEffectData)
        => SetKeyFrameData((T)obj, block, key, idx, postProcessingEffectData);
    public IKey CreateAndAddEmptyKey(RenderObject obj, CGameCtnMediaBlock block)
        => CreateAndAddEmptyKey(block);
    public bool CanShareBlockWith(RenderObject obj, MediaObject other)
        => CanShareBlockWith((T)obj, other);
    public int AddRenderDataToBlock(RenderObject obj, CGameCtnMediaBlock block)
        => AddRenderDataToBlock((T)obj, block);
}
public interface ITwoKeyRenderer<T> : ITwoKeyRenderer where T : MediaObject
{
    public void SetDataToStart(T obj, CGameCtnMediaBlock block);
    public void SetDataToEnd(T obj, CGameCtnMediaBlock block);
}

public abstract class TriangleRenderer : KeysRendererBase<TriangleObject>
{
    public HashSet<PostProcessingEffect> PostProcessingEffectsWorld { get; init; } = [];

    public virtual Vector3 DefaultPolygonNormal => Vector3.UnitZ;

    public override bool CanShareBlockWith(TriangleObject obj, MediaObject other)
    {
        return (other is RenderObject ro) && obj.Renderer.GetType() == ro.Renderer.GetType();
    }

    public override IKey CreateAndAddEmptyKey(CGameCtnMediaBlock block)
    {
        var key = new CGameCtnMediaBlockTriangles.Key((block as CGameCtnMediaBlockTriangles)!);
        (block as CGameCtnMediaBlockTriangles)!.Keys.Add(key);
        return key;
    }

    public virtual int AddTriangleDataToBlock(TriangleObject obj, CGameCtnMediaBlock block)
    {
        var triangleBlock = block as CGameCtnMediaBlockTriangles;
        int idx = triangleBlock!.Vertices.Length;
        Int3 triangleOffset = (idx, idx, idx);

        var oldVertices = triangleBlock.Vertices;
        int oldLen = oldVertices.Length;
        int addLen = obj.Colors.Length;

        var newVertices = new Vec4[oldLen + addLen];

        Array.Copy(oldVertices, newVertices, oldLen);

        for (int i = 0; i < addLen; i++)
        {
            var c = obj.Colors[i];
            newVertices[oldLen + i] = new Vec4(c.X, c.Y, c.Z, c.W);
        }

        triangleBlock.Vertices = newVertices;

        var oldTris = triangleBlock.Triangles;
        int oldTrisLen = oldTris.Length;
        int addTrisLen = obj.Triangles.Length;

        var newTriangles = new Int3[oldTrisLen + addTrisLen];
        Array.Copy(oldTris, newTriangles, oldTrisLen);

        for (int i = 0; i < addTrisLen; i++)
        {
            newTriangles[oldTrisLen + i] = triangleOffset + obj.Triangles[i];
        }

        triangleBlock.Triangles = newTriangles;

        //triangleBlock.Vertices = triangleBlock.Vertices.Concat(obj.Colors.Select(c => new Vec4(c.X, c.Y, c.Z, c.W))).ToArray();
        //triangleBlock.Triangles = triangleBlock.Triangles.Concat(obj.Triangles.Select(t => t + triangleOffset)).ToArray();
        return idx;
    }
    public override int AddRenderDataToBlock(TriangleObject obj, CGameCtnMediaBlock block)
        => AddTriangleDataToBlock(obj, block);

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

}

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
public static class ScreenPositionExtensions
{

    public static Vector3 DefaultScreenToVector3Function(ScreenPosition loc)
    {
        const float halfH = Triangle2DRenderer.DefaultOrthographicSize;
        const float halfW = halfH * (16f / 9f);

        return loc switch
        {
            ScreenPosition.TOP => new Vector3(0, halfH, 0),
            ScreenPosition.BOTTOM => new Vector3(0, -halfH, 0),
            ScreenPosition.LEFT => new Vector3(-halfW, 0, 0),
            ScreenPosition.RIGHT => new Vector3(halfW, 0, 0),
            ScreenPosition.CENTER => Vector3.Zero,
            ScreenPosition.TOP_LEFT => new Vector3(-halfW, halfH, 0),
            ScreenPosition.TOP_RIGHT => new Vector3(halfW, halfH, 0),
            ScreenPosition.BOTTOM_LEFT => new Vector3(-halfW, -halfH, 0),
            ScreenPosition.BOTTOM_RIGHT => new Vector3(halfW, -halfH, 0),
            _ => throw new NotImplementedException()
        };
    }

    public static Func<ScreenPosition, Vector3> ScreenToVector3Function = (s) => DefaultScreenToVector3Function(s);
    public static Vector3 ToVector3(this ScreenPosition screenPos) => ScreenToVector3Function(screenPos);


}

/// <summary>
/// Renders TriangleObjects to 2DTriangles 
/// </summary>
public class Triangle2DRenderer : TriangleRenderer
{
    public const float DefaultOrthographicSize = 10f;
    public IRenderingCamera Camera { get; set; }
    public bool IsOrthographic { get; set; } = true;
    public float OrthographicSize { get; set; } = DefaultOrthographicSize;
    public float AspectRatio { get; set; } = 16f / 9f;
    public Triangle2DRenderer()
    {
        Camera = new CustomCameraObject()
        {
            Position = new Vector3(0, 0, 0),
            Rotation = Quaternion.Identity,
            FOV = 80f,
            NearClipPlane = 0.05f,
        };
    }


    public override CGameCtnMediaBlock CreateEmptyBlock(BlockTemplates templates)
    {
        var block = MediaTrackerUtils.DeepCopyBlockTriangles2D(templates.Triangles2D);
        block.Keys.Clear();
        return block;
    }

    //public static Matrix4x4 MediaTrackerTransformationMatrix =
    //Matrix4x4.CreateScale(1, (16f / 9f), 1) *
    //Matrix4x4.CreateTranslation(0, 0, 0);

    //private Matrix4x4 GetNDCTransformation(RenderData renderData)
    //{
    //    var view = Matrix4x4.CreateLookAt(renderData.CameraPosition, renderData.CameraLookAt == default ? Vector3.UnitZ : renderData.CameraLookAt, Vector3.UnitY);
    //    if (renderData.Mode == CameraMode.Orthographic)
    //        return view * Matrix4x4.CreateOrthographic(renderData.ViewBox.X, renderData.ViewBox.Y, 0.01f, renderData.ViewBox.Z);
    //    else
    //        return view * Matrix4x4.CreatePerspectiveFieldOfView(renderData.FOV, renderData.ViewBox.X / renderData.ViewBox.Y, 0.001f, renderData.ViewBox.Z);
    //}
    //private Vector3 ApplyLocalNDCPostProcessingEffects(TriangleObject obj, Vector3 v)
    //{
    //    foreach (var effect in obj.LocalNDCPostProcessingEffects)
    //    {
    //        v = effect.Transform(v);
    //    }
    //    return v;
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
   
    public override void SetKeyFrameData(TriangleObject obj, CGameCtnMediaBlock block, IKey key, int idx, PostProcessingEffectData postProcessingEffectData)
    {
        var triangleKey = key as CGameCtnMediaBlockTriangles.Key;
        for (int i = 0; i < obj.Vertices.Length; ++i)
        {
            var v = ToMediaTrackerCoordinates(obj, obj.Vertices[i], postProcessingEffectData);
            triangleKey!.Positions[idx + i] = new Vec3(v.X, v.Y, v.Z);
        }
    }

    //protected virtual Vector3 ToMediaTrackerCoordinates(TriangleObject obj, Vector3 vec3, RenderData renderData, PostProcessingEffectData postProcessingEffectData)
    //{
    //    // Local → World
    //    var worldSpace = Vector3.Transform(vec3, obj.LocalToWorldTRS);
    //    worldSpace = ApplyGlobalWorldSpacePostProcessingEffects(worldSpace, postProcessingEffectData);
    //    worldSpace = ApplyLocalWorldSpacePostProcessingEffects(obj, worldSpace);

    //    // World → Clip space
    //    var ndcMatrix = GetNDCTransformation(renderData);
    //    Vector4 clipSpace = Vector4.Transform(new Vector4(worldSpace, 1f), ndcMatrix);

    //    // Perspective divide
    //    Vector3 ndcSpace = new Vector3(clipSpace.X / clipSpace.W, clipSpace.Y / clipSpace.W, clipSpace.Z / clipSpace.W);

    //    // Post-processing in NDC
    //    ndcSpace = ApplyGlobalNDCPostProcessingEffects(ndcSpace, postProcessingEffectData);
    //    ndcSpace = ApplyLocalNDCPostProcessingEffects(obj, ndcSpace);

    //    // NDC → MediaTracker
    //    var mediaTrackerSpace = Vector3.Transform(ndcSpace, MediaTrackerTransformationMatrix);

    //    return mediaTrackerSpace;
    //}

    protected virtual Vector3 ToMediaTrackerCoordinates(TriangleObject obj, Vector3 vec3, PostProcessingEffectData postProcessingEffectData)
    {
        Vector3 camPosition = Camera.GetPosition();
        Quaternion camRotation = Camera.GetRotation();

        // GBX right-handed Y-up, identity = looking down +Z
        Vector3 forward = Vector3.Transform(-Vector3.UnitZ, camRotation);  // was +UnitZ
        Vector3 up = Vector3.Transform(Vector3.UnitY, camRotation);
        Vector3 right = Vector3.Transform(Vector3.UnitX, camRotation);

        // --- View matrix (column-major for System.Numerics) ---
        Matrix4x4 view = new Matrix4x4(
             right.X, up.X, forward.X, 0,
             right.Y, up.Y, forward.Y, 0,
             right.Z, up.Z, forward.Z, 0,
            -Vector3.Dot(right, camPosition),
            -Vector3.Dot(up, camPosition),
            -Vector3.Dot(forward, camPosition),
            1
        );

        /*  // --- Projection matrix Diagonal FOV (Editor Preview)---
      float fovRad = cam.FOV * MathF.PI / 180f;
      float aspect = 18.66f / 9f;
      float near = cam.NearClipPlane;
      float far = 10000f;
      float tanHalfFov = MathF.Tan(fovRad / 2f);

      Matrix4x4 proj = new Matrix4x4(
          1f / (aspect * tanHalfFov), 0, 0, 0,
          0, 1f / tanHalfFov, 0, 0,
          0, 0, (far + near) / (far - near), 1,  // RH +Z forward: no negation
          0, 0, -(2f * far * near) / (far - near), 0
      );
      */
        // --- Projection matrix ---
        Matrix4x4 proj;

        if (IsOrthographic)
        {
            float orthoH = OrthographicSize;
            float orthoW = orthoH * AspectRatio;
            float near = -10000f; // hard coded for ortho
            float far = 10000f;

            proj = new Matrix4x4(
                1f / orthoW, 0, 0, 0,
                0, 1f / orthoH, 0, 0,
                0, 0, 2f / (far - near), 0,
                0, 0, -(far + near) / (far - near), 1
            );
        }
        else
        {
            float fovRad = Camera.GetFOV() * MathF.PI / 180f;
            float aspect = AspectRatio;
            float near = Camera.GetNearClipPlane();
            float far = 10000f;
            float tanHalfFov = MathF.Tan(fovRad / 2f);

            proj = new Matrix4x4(
                1f / (aspect * tanHalfFov), 0, 0, 0,
                0, 1f / tanHalfFov, 0, 0,
                0, 0, (far + near) / (far - near), 1,
                0, 0, -(2f * far * near) / (far - near), 0
            );
        }

        Matrix4x4 viewProj = view * proj;

        vec3 = Vector3.Transform(vec3, obj.LocalToWorldTRS);

        // --- Transform vertex ---
        float x = vec3.X * viewProj.M11 + vec3.Y * viewProj.M21 + vec3.Z * viewProj.M31 + viewProj.M41;
        float y = vec3.X * viewProj.M12 + vec3.Y * viewProj.M22 + vec3.Z * viewProj.M32 + viewProj.M42;
        float z = vec3.X * viewProj.M13 + vec3.Y * viewProj.M23 + vec3.Z * viewProj.M33 + viewProj.M43;
        float w = vec3.X * viewProj.M14 + vec3.Y * viewProj.M24 + vec3.Z * viewProj.M34 + viewProj.M44;

        if (MathF.Abs(w) < 1e-6f)
            return new Vector3(float.NaN, float.NaN, 0f);

        // --- Perspective divide -> NDC, already centered [-1,1] ---
        float ndcX = x / w;
        float ndcY = IsOrthographic ? -(y / w) : y / w;
        float ndcZ = z / w; // [-1, 1] range after divide
        float depthZ = (ndcZ + 1f) / 2f;

        // MT origin = center, so NDC maps directly; apply MT matrix for aspect correction
        Vector3 ndc = new Vector3(-ndcX, -ndcY, depthZ);
        return ndc;
    }

    public Vector3 GetPosition(ScreenPosition loc, float depth = 1) => ToWorldCoordinates(loc switch
    {
        ScreenPosition.TOP => new Vector3(0, 1, 0),
        ScreenPosition.BOTTOM => new Vector3(0, -1, 0),
        ScreenPosition.LEFT => new Vector3(-1, 0, 0),
        ScreenPosition.RIGHT => new Vector3(1, 0, 0),
        ScreenPosition.CENTER => Vector3.Zero,
        ScreenPosition.TOP_LEFT => new Vector3(-1, 1, 0),
        ScreenPosition.TOP_RIGHT => new Vector3(1, 1, 0),
        ScreenPosition.BOTTOM_LEFT => new Vector3(-1, -1, 0),
        ScreenPosition.BOTTOM_RIGHT => new Vector3(1, -1, 0),
        _ => throw new NotImplementedException()
    });

    public virtual Vector3 ToWorldCoordinates(Vector3 mtCoords, float worldDepth = 1f)
    {
        // Undo MT flips to get back to raw NDC
        float ndcX = -mtCoords.X;
        float ndcY = IsOrthographic ? -mtCoords.Y : -mtCoords.Y;  // same flip both cases TODO: maybe not?

        Vector3 camPosition = Camera.GetPosition();
        Quaternion camRotation = Camera.GetRotation();

        Vector3 forward = Vector3.Transform(-Vector3.UnitZ, camRotation);
        Vector3 up = Vector3.Transform(Vector3.UnitY, camRotation);
        Vector3 right = Vector3.Transform(Vector3.UnitX, camRotation);

        if (IsOrthographic)
        {
            float halfH = OrthographicSize;
            float halfW = halfH * AspectRatio;

            return camPosition
                 + right * (ndcX * halfW)
                 + up * (ndcY * halfH)
                 + forward * worldDepth;
        }
        else
        {
            float fovRad = Camera.GetFOV() * MathF.PI / 180f;
            float tanHalfFov = MathF.Tan(fovRad / 2f);
            float aspect = AspectRatio;

            Vector3 rayDir = Vector3.Normalize(
                  forward
                + right * (ndcX * aspect * tanHalfFov)
                + up * (ndcY * tanHalfFov)
            );

            return camPosition + rayDir * worldDepth;
        }
    }

}
public class ActiveCameraTriangle2DRenderer : Triangle2DRenderer
{
    SceneCameraManager _sceneCameraManager;
    public ActiveCameraTriangle2DRenderer(SceneCameraManager sceneCameraManager)
    {
        _sceneCameraManager = sceneCameraManager;
        IsOrthographic = false;
    }
    protected override Vector3 ToMediaTrackerCoordinates(TriangleObject obj, Vector3 vec3, PostProcessingEffectData postProcessingEffectData)
    {
        if (_sceneCameraManager.ActiveCamera is IRenderingCamera renderingCamera)
            this.Camera = renderingCamera;
        else
            throw new InvalidOperationException("Missing active Camera");
        return base.ToMediaTrackerCoordinates(obj, vec3, postProcessingEffectData);
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
    public Vector3[] AdditionalEnsuranceVertices { get; init; } = [];
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
            int idx = triangleBlock!.Vertices.Length;
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
                ..Enumerable.Repeat(new Vec4(), ensuranceVerticesCount),
                ..Enumerable.Repeat(new Vec4(), AdditionalEnsuranceVertices.Length)];
            triangleBlock.Triangles = triangleBlock.Triangles.Concat(obj.Triangles.Select(t => t + triangleOffset)).ToArray();
            return idx;
        }
        else
        {
            return base.AddRenderDataToBlock(obj, block);
        }   
    }
    public override void SetKeyFrameData(TriangleObject obj, CGameCtnMediaBlock block, IKey key, int idx, PostProcessingEffectData postProcessingEffectData)
    {
        var triangleKey = (key as CGameCtnMediaBlockTriangles.Key)!;
        for (int i = 0; i < obj.Vertices.Length; ++i)
        {
            var v = ToWorldCoordinates(obj, obj.Vertices[i], postProcessingEffectData);
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
            for (int i = 0; i < AdditionalEnsuranceVertices.Length; ++i)
            {
                triangleKey.Positions[idx + obj.Vertices.Length + EnsuranceMode switch
                {
                    Ensurance._4 => 4,
                    Ensurance._8 => 8,
                    _ => throw new NotImplementedException(),
                } + i] = AdditionalEnsuranceVertices[i];
            }
           
        }
    }
    protected virtual Vector3 ToWorldCoordinates(TriangleObject obj, Vector3 vec3, PostProcessingEffectData postProcessingEffectData)
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
    public static IKeysRenderer DefaultTriangleRenderer = new Triangle3DRenderer();
}



public class PlayerCameraRenderer : ITwoKeyRenderer<PlayerCameraObject>
{
    public bool CanShareBlockWith(RenderObject obj, MediaObject other)
        => false;

    public CGameCtnMediaBlock CreateEmptyBlock(BlockTemplates templates)
    {
        var block = templates.GetEmptyPlayerCameraBlock();
        block.End = block.Start;
        return block;
    }

    public void SetDataToEnd(MediaObject obj, CGameCtnMediaBlock block)
        => SetDataToStart((PlayerCameraObject)obj, block);

    public void SetDataToEnd(PlayerCameraObject obj, CGameCtnMediaBlock block)
    {
        var cameraBlock = (block as CGameCtnMediaBlockCameraGame)!;
        cameraBlock.GameCam = obj.CameraType;
    }


    public void SetDataToStart(MediaObject obj, CGameCtnMediaBlock block)
        => SetDataToStart((PlayerCameraObject)obj, block);

    public void SetDataToStart(PlayerCameraObject obj, CGameCtnMediaBlock block)
    {
        var cameraBlock = (block as CGameCtnMediaBlockCameraGame)!;
        cameraBlock.GameCam = obj.CameraType;
    }
}


public class DepthOfFieldRenderer : KeysRendererBase<DepthOfFieldObject>
{
    public override CGameCtnMediaBlock CreateEmptyBlock(BlockTemplates templates)
    {
        var block = templates.GetEmptyDepthOfFieldBlock();
        block.Keys?.Clear();
        return block;
    }
    public override void SetKeyFrameData(DepthOfFieldObject obj, CGameCtnMediaBlock block, IKey key, int idx, PostProcessingEffectData postProcessingEffectData)
    {
        var dofKey = (key as CGameCtnMediaBlockDOF.Key)!;
        dofKey.ZFocus = obj.FocusDistance;
        dofKey.LensSize = obj.LensSize;
        dofKey.Target = obj.Target;
        dofKey.TargetPosition = obj.TargetPosition;

    }
    public override IKey CreateAndAddEmptyKey(CGameCtnMediaBlock block)
    {
        var key = new CGameCtnMediaBlockDOF.Key();
        (block as CGameCtnMediaBlockDOF)!.Keys?.Add(key);
        return key;
    }

    public override bool CanShareBlockWith(DepthOfFieldObject obj, MediaObject other)
        => false;

    public override int AddRenderDataToBlock(DepthOfFieldObject obj, CGameCtnMediaBlock block)
    {
        return 0;
    }
}


public class CustomCameraRenderer : KeysRendererBase<CustomCameraObject>
{
    public override CGameCtnMediaBlock CreateEmptyBlock(BlockTemplates templates)
    {
        var block = templates.GetEmptyCustomCameraBlock();
        block.Keys?.Clear();
        return block;
    }
    public override void SetKeyFrameData(CustomCameraObject obj, CGameCtnMediaBlock block, IKey key, int idx, PostProcessingEffectData postProcessingEffectData)
    {
        var camKey = (key as CGameCtnMediaBlockCameraCustom.Key)!;
        camKey.Position = obj.Position;
        camKey.PitchYawRoll = obj.Rotation.ToPitchYawRoll();
        camKey.U01 = 1065353216; // not sure what this does but its default
        camKey.Anchor = obj.Anchor;
        camKey.AnchorVis = obj.AnchorVisibility;
        camKey.AnchorRot = obj.AnchorRotation;
        camKey.Fov = obj.FOV;
        camKey.Interpolation = obj.Interpolation;
        camKey.NearZ = obj.NearClipPlane;
        camKey.Target = obj.Target;
        camKey.TargetPosition = obj.TargetPosition;
        camKey.LeftTangent = new CGameCtnMediaBlockCameraCustom.InterpVal()
        {
            PitchYawRoll = Vector3.Zero,
            Position = new Vector3(1, 0, 0),
            TargetPosition = new Vector3(0, 0, 0),
            Fov = obj.FOV,
            NearZ = obj.NearClipPlane,
        };
        camKey.RightTangent = new CGameCtnMediaBlockCameraCustom.InterpVal()
        {
            PitchYawRoll = Vector3.Zero,
            Position = new Vector3(1, 0, 0),
            TargetPosition = new Vector3(0, 0, 0),
            Fov = obj.FOV,
            NearZ = obj.NearClipPlane,
        };
    }
    public override IKey CreateAndAddEmptyKey(CGameCtnMediaBlock block)
    {
        var key = new CGameCtnMediaBlockCameraCustom.Key();
        (block as CGameCtnMediaBlockCameraCustom)!.Keys?.Add(key);
        return key;
    }
    public override bool CanShareBlockWith(CustomCameraObject obj, MediaObject other)
        => false;

    public override int AddRenderDataToBlock(CustomCameraObject obj, CGameCtnMediaBlock block)
    {
        return 0;
    }
}
public class PathCameraRenderer : KeysRendererBase<PathCameraObject>
{
    public override CGameCtnMediaBlock CreateEmptyBlock(BlockTemplates templates)
    {
        var block = templates.GetEmptyCustomCameraBlock();
        block.Keys?.Clear();
        return block;
    }
    public override void SetKeyFrameData(PathCameraObject obj, CGameCtnMediaBlock block, IKey key, int idx, PostProcessingEffectData postProcessingEffectData)
    {
        var camKey = (key as CGameCtnMediaBlockCameraPath.Key)!;
        camKey.Position = obj.Position;
        camKey.PitchYawRoll = obj.Rotation.ToPitchYawRoll();
        camKey.Anchor = obj.Anchor;
        camKey.AnchorVis = obj.AnchorVisibility;
        camKey.AnchorRot = obj.AnchorRotation;
        camKey.Fov = obj.FOV;
        camKey.Weight = obj.Weight;
        camKey.NearZ = obj.NearClipPlane;
        camKey.Target = obj.Target;
        camKey.TargetPosition = obj.TargetPosition;
    }
    public override IKey CreateAndAddEmptyKey(CGameCtnMediaBlock block)
    {
        var key = new CGameCtnMediaBlockCameraPath.Key();
        (block as CGameCtnMediaBlockCameraPath)!.Keys?.Add(key);
        return key;
    }
    public override bool CanShareBlockWith(PathCameraObject obj, MediaObject other)
        => false;

    public override int AddRenderDataToBlock(PathCameraObject obj, CGameCtnMediaBlock block)
    {
        return 0;
    }
}
public class OrbitalCameraRenderer : KeysRendererBase<OrbitalCameraObject>
{
    public override CGameCtnMediaBlock CreateEmptyBlock(BlockTemplates templates)
    {
        var block = templates.GetEmptyCustomCameraBlock();
        block.Keys?.Clear();
        return block;
    }
    public override void SetKeyFrameData(OrbitalCameraObject obj, CGameCtnMediaBlock block, IKey key, int idx, PostProcessingEffectData postProcessingEffectData)
    {
        var camKey = (key as CGameCtnMediaBlockCameraOrbital.Key)!;
        camKey.TargetPosition = obj.TargetPosition;
        camKey.Radius = obj.Radius;
        camKey.Fov = obj.FOV;
        camKey.Longitude = obj.Longitude;
        camKey.Latitude = obj.Latitude;
    }
    public override IKey CreateAndAddEmptyKey(CGameCtnMediaBlock block)
    {
        var key = new CGameCtnMediaBlockCameraOrbital.Key();
        (block as CGameCtnMediaBlockCameraOrbital)!.Keys?.Add(key);
        return key;
    }
    public override bool CanShareBlockWith(OrbitalCameraObject obj, MediaObject other)
        => false;
    public override int AddRenderDataToBlock(OrbitalCameraObject obj, CGameCtnMediaBlock block)
    {
        return 0;
    }
}
