using GBX.NET;
using GBX.NET.Engines.Game;
using System.Numerics;
using TM_GenericMapping.Common;
using TM_GenericMapping.Messaging;
using static GBX.NET.Engines.Game.CGameCtnMediaBlock;

namespace TM_GenericMapping.MediaTracker.IO;

public class BlockToMediaObjectConverter
{
 


    Triangle2DRenderer _standardOrthographic2DRenderer = new Triangle2DRenderer() { OrthographicSize = 1 };
    Triangle3DRenderer _standard3DRenderer = new Triangle3DRenderer();
    public BlockToMediaObjectConverter()
    {

    }

    (IKey keyStart, IKey keyEnd) FindSurroundingKeys(IHasKeys hasKeys, int timeMillis)
    {
        var indices = FindSurroundingKeyIdx(hasKeys, timeMillis);
        return (hasKeys.Keys.ElementAt(indices.keyStart), hasKeys.Keys.ElementAt(indices.keyEnd));
    }
    (int keyStart, int keyEnd) FindSurroundingKeyIdx(IHasKeys hasKeys, int timeMillis)
    {
        int k0 = 0;
        int k1 = 1;
        for (int i = 0; i < hasKeys.Keys.Count() - 1; i++)
        {
            if (hasKeys.Keys.ElementAt(i).Time.TotalMilliseconds <= timeMillis && hasKeys.Keys.ElementAt(i + 1).Time.TotalMilliseconds >= timeMillis)
            {
                k0 = i;
                k1 = i + 1;
                break;
            }
        }
        return (k0, k1);
    }
    public int GetKeyTime(IHasKeys block, int keyIdx)
        => (int)block.Keys.ElementAt(Math.Clamp(keyIdx, 0, block.Keys.Count() - 1)).Time.TotalMilliseconds;
    public bool TryGetBlockAtTime(CGameCtnMediaTrack track, int timeMillis, out CGameCtnMediaBlock? block)
    {
        foreach(var b in track.Blocks)
        {
            if (b is not IHasKeys hasKeys)
                continue;
            if (!hasKeys.Keys.Any())
                continue;
            if (timeMillis >= hasKeys.Keys.ElementAt(0).Time.TotalMilliseconds && timeMillis <= hasKeys.Keys.Last().Time.TotalMilliseconds)
            {
                block = b;
                return true;
            }
        }
        block = null;
        return false;
    }

    public ToolResult<TriangleObject> ReconstructTriangles3DKey(CGameCtnMediaTrack trianglesTrack, int keyIdx = int.MaxValue, TriangleRenderer triangleRenderer = null!)
    {
        var trianglesBlock = trianglesTrack.Blocks.OfType<CGameCtnMediaBlockTriangles>().FirstOrDefault();
        if (trianglesBlock == null)
            return ToolResult.Fail(nameof(BlockToMediaObjectConverter), ErrorCodes.BlockToMediaObjectConverter.MissingTriangleBlocks);
        var obj = ReconstructTriangles3DKey(trianglesBlock, keyIdx, triangleRenderer);
        if (obj.IsFailure)
            return ToolResult.Fail(obj);
        if (!string.IsNullOrEmpty(trianglesTrack.Name))
            obj.Value.Name = trianglesTrack.Name;
        return ToolResult.Success(obj.Value, nameof(BlockToMediaObjectConverter));
    }
    public ToolResult<TriangleObject> ReconstructTriangles3D(CGameCtnMediaTrack trianglesTrack, int timeMillis = int.MaxValue, TriangleRenderer triangleRenderer = null!)
    {
        var trianglesBlock = trianglesTrack.Blocks.OfType<CGameCtnMediaBlockTriangles>().FirstOrDefault();
        if (trianglesBlock == null)
            return ToolResult.Fail(nameof(BlockToMediaObjectConverter), ErrorCodes.BlockToMediaObjectConverter.MissingTriangleBlocks);
        var obj = ReconstructTriangles3D(trianglesBlock, timeMillis, triangleRenderer);
        if (obj.IsFailure)
            return ToolResult.Fail(obj);
        if (!string.IsNullOrEmpty(trianglesTrack.Name))
            obj.Value.Name = trianglesTrack.Name;
        return ToolResult.Success(obj.Value, nameof(BlockToMediaObjectConverter));
    }
    public ToolResult<TriangleObject> ReconstructTriangles3DKey(CGameCtnMediaBlockTriangles trianglesBlock, int keyIdx = int.MaxValue, TriangleRenderer triangleRenderer = null!)
    {
        if (trianglesBlock.Keys.Count == 0)
            return ToolResult.Fail(nameof(BlockToMediaObjectConverter), ErrorCodes.BlockToMediaObjectConverter.MissingKeys);
        var key = trianglesBlock.Keys[Math.Clamp(keyIdx, 0, trianglesBlock.Keys.Count - 1)];

       return ReconstructTriangles3D(trianglesBlock, (int)key.Time.TotalMilliseconds, triangleRenderer);
    }
    public ToolResult<TriangleObject> ReconstructTriangles3D(CGameCtnMediaBlockTriangles trianglesBlock, int timeMillis = int.MaxValue, TriangleRenderer triangleRenderer = null!)
    {
        if (trianglesBlock.Keys.Count == 0)
            return ToolResult.Fail(nameof(BlockToMediaObjectConverter), ErrorCodes.BlockToMediaObjectConverter.MissingKeys);
        var triangles = trianglesBlock.Triangles.ToArray();
        var colors = trianglesBlock.Vertices.Select(c => c.ToColor()).ToArray();

        timeMillis = Math.Clamp(timeMillis, (int)trianglesBlock.Keys[0].Time.TotalMilliseconds, (int)trianglesBlock.Keys.Last().Time.TotalMilliseconds);

        var (s,e) = FindSurroundingKeys(trianglesBlock, timeMillis);
        var startKey = (s as CGameCtnMediaBlockTriangles.Key)!;
        var endKey = (e as CGameCtnMediaBlockTriangles.Key)!;
        Vector3[] vertices;

        if (timeMillis == (int)startKey.Time.TotalMilliseconds)
            vertices = startKey.Positions.Select(p => p.ToVector3()).ToArray();
        else if (timeMillis == (int)endKey.Time.TotalMilliseconds)
            vertices = endKey.Positions.Select(p => p.ToVector3()).ToArray();
        else
        {
            vertices = new Vector3[startKey.Positions.Length];
            float t = (timeMillis - startKey.Time.TotalMilliseconds) / (float)(endKey.Time.TotalMilliseconds - startKey.Time.TotalMilliseconds);
            t = Math.Clamp(t, 0f, 1f);
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i] = Vector3.Lerp(startKey.Positions[i].ToVector3(), endKey.Positions[i].ToVector3(), t);
            }
        }


        var triangleObject = new TriangleObject(
            points: vertices,
            triangles: triangles,
            colors: colors,
            renderer: triangleRenderer ?? _standard3DRenderer);
        return ToolResult.Success(triangleObject, nameof(BlockToMediaObjectConverter));
    }

    public ToolResult<TriangleObject> ReconstructTriangles2DKey(CGameCtnMediaBlockTriangles block, int keyIdx = int.MaxValue,  TriangleRenderer triangleRenderer = null!)
    {
        if (block.Keys.Count == 0)
            return ToolResult.Fail(nameof(BlockToMediaObjectConverter), ErrorCodes.BlockToMediaObjectConverter.MissingKeys);
        var key = block.Keys[Math.Clamp(keyIdx, 0, block.Keys.Count - 1)];

        return ReconstructTriangles2D(block, (int)key.Time.TotalMilliseconds, triangleRenderer);
    }
    public ToolResult<TriangleObject> ReconstructTriangles2D(CGameCtnMediaBlockTriangles block, int timeMillis = int.MaxValue, TriangleRenderer triangleRenderer = null!)
    {
        if (block.Keys.Count == 0)
            return ToolResult.Fail(nameof(BlockToMediaObjectConverter), ErrorCodes.BlockToMediaObjectConverter.MissingKeys);

        var triangles = block.Triangles.ToArray();
        var colors = block.Vertices.Select(c => c.ToColor()).ToArray();

        timeMillis = Math.Clamp(timeMillis, (int)block.Keys[0].Time.TotalMilliseconds, (int)block.Keys.Last().Time.TotalMilliseconds);

        var (s, e) = FindSurroundingKeys(block, timeMillis);
        var startKey = (s as CGameCtnMediaBlockTriangles.Key)!;
        var endKey = (e as CGameCtnMediaBlockTriangles.Key)!;
        Vector3[] vertices;

        if (timeMillis == (int)startKey.Time.TotalMilliseconds)
            vertices = startKey.Positions.Select(p => p.ToVector3()).ToArray();
        else if (timeMillis == (int)endKey.Time.TotalMilliseconds)
            vertices = endKey.Positions.Select(p => p.ToVector3()).ToArray();
        else
        {
            vertices = new Vector3[startKey.Positions.Length];
            float t = (timeMillis - startKey.Time.TotalMilliseconds) / (float)(endKey.Time.TotalMilliseconds - startKey.Time.TotalMilliseconds);
            t = Math.Clamp(t, 0f, 1f);
            for (int i = 0; i < vertices.Length; i++)
            {
                var vertex = Vector3.Lerp(startKey.Positions[i].ToVector3(), endKey.Positions[i].ToVector3(), t);
                vertex.Y *= -1;
                vertices[i] = _standardOrthographic2DRenderer.ToWorldCoordinates(vertex, vertex.Z);
            }
        }


        var triangleObject = new TriangleObject(
            points: vertices,
            triangles: triangles,
            colors: colors,
            renderer: triangleRenderer ?? _standardOrthographic2DRenderer);
        return ToolResult.Success(triangleObject, nameof(BlockToMediaObjectConverter));
    }

    public ToolResult<CustomCameraObject> ReconstructCustomCameraKey(CGameCtnMediaBlockCameraCustom block, int keyIdx = int.MaxValue)
    {
        if (block.Keys == null || block.Keys.Count == 0)
            return ToolResult.Fail(nameof(BlockToMediaObjectConverter), ErrorCodes.BlockToMediaObjectConverter.MissingKeys);

        var key = block.Keys[Math.Clamp(keyIdx, 0, block.Keys.Count - 1)];

        var cameraObject = new CustomCameraObject()
        {
            Anchor = key.Anchor,
            AnchorRotation = key.AnchorRot,
            AnchorVisibility = key.AnchorVis,
            FOV = key.Fov,
            Interpolation = key.Interpolation,
            NearClipPlane = key.NearZ ?? 0.05f,
            Target = key.Target,
            TargetPosition = key.TargetPosition.ToVector3()
        };
        cameraObject.Position = key.Position.ToVector3();
        cameraObject.Rotation = Quaternion.CreateFromPitchYawRoll(key.PitchYawRoll);
        return ToolResult.Success(cameraObject, nameof(BlockToMediaObjectConverter));
    }
    public ToolResult<PathCameraObject> ReconstructPathCameraKey(CGameCtnMediaBlockCameraPath block, int keyIdx = int.MaxValue)
    {
        if (block.Keys == null || block.Keys.Count == 0)
            return ToolResult.Fail(nameof(BlockToMediaObjectConverter), ErrorCodes.BlockToMediaObjectConverter.MissingKeys);

        var key = block.Keys[Math.Clamp(keyIdx, 0, block.Keys.Count - 1)];

        var cameraObject = new PathCameraObject()
        {
            Anchor = key.Anchor,
            AnchorRotation = key.AnchorRot,
            AnchorVisibility = key.AnchorVis,
            FOV = key.Fov,
            Weight = key.Weight,
            Target = key.Target,
            TargetPosition = key.TargetPosition.ToVector3()
        };
        cameraObject.Position = key.Position.ToVector3();
        cameraObject.Rotation = Quaternion.CreateFromPitchYawRoll(key.PitchYawRoll);
        return ToolResult.Success(cameraObject, nameof(BlockToMediaObjectConverter));
    }

    public ToolResult<DepthOfFieldObject> ReconstructDepthOfFieldObjectKey(CGameCtnMediaBlockDOF block, int keyIdx = int.MaxValue)
    {
        if (block.Keys == null || block.Keys.Count == 0)
            return ToolResult.Fail(nameof(BlockToMediaObjectConverter), ErrorCodes.BlockToMediaObjectConverter.MissingKeys);

        var key = block.Keys[Math.Clamp(keyIdx, 0, block.Keys.Count - 1)];

        var dofObject = new DepthOfFieldObject(
            focusDistance: key.ZFocus,
            lensSize: key.LensSize,
            target: key.Target ?? -1,
            targetPosition: key.TargetPosition?.ToVector3());
        return ToolResult.Success(dofObject, nameof(BlockToMediaObjectConverter));
    }

    public ToolResult<MediaObject> ReconstructObjectKey(CGameCtnMediaBlock block, int keyIdx = int.MaxValue)
        => ReconstructObjectKey<MediaObject>(block, keyIdx);
    public ToolResult<MediaObject> ReconstructObject(CGameCtnMediaBlock block, int timeMillis = int.MaxValue)
       => ReconstructObject<MediaObject>(block, timeMillis);
    public ToolResult<T> ReconstructObjectKey<T>(CGameCtnMediaBlock block, int keyIdx = int.MaxValue)
        where T : MediaObject
    {
        switch (block)
        {
            case CGameCtnMediaBlockTriangles3D cGameCtnMediaBlockTriangles3D:
                return ReconstructTriangles3DKey(cGameCtnMediaBlockTriangles3D, keyIdx).Cast<T>();
            case CGameCtnMediaBlockTriangles2D cGameCtnMediaBlockTriangles:
                return ReconstructTriangles2DKey(cGameCtnMediaBlockTriangles, keyIdx).Cast<T>();
            case CGameCtnMediaBlockCameraCustom cGameCtnMediaBlockCameraCustom:
                return ReconstructCustomCameraKey(cGameCtnMediaBlockCameraCustom, keyIdx).Cast<T>();
            case CGameCtnMediaBlockCameraPath cGameCtnMediaBlockCameraPath:
                return ReconstructPathCameraKey(cGameCtnMediaBlockCameraPath, keyIdx).Cast<T>();
            case CGameCtnMediaBlockDOF cGameCtnMediaBlockDOF:
                return ReconstructDepthOfFieldObjectKey(cGameCtnMediaBlockDOF, keyIdx).Cast<T>();
            default:
                return ToolResult.Fail(nameof(BlockToMediaObjectConverter), ErrorCodes.BlockToMediaObjectConverter.UnsupportedBlockType);
        }
    }

    public ToolResult<T> ReconstructObject<T>(CGameCtnMediaBlock block, int timeMillis = int.MaxValue)
       where T : MediaObject
    {
        switch (block)
        {
            case CGameCtnMediaBlockTriangles3D cGameCtnMediaBlockTriangles3D:
                return ReconstructTriangles3D(cGameCtnMediaBlockTriangles3D, timeMillis).Cast<T>();
            case CGameCtnMediaBlockTriangles2D cGameCtnMediaBlockTriangles:
                return ReconstructTriangles2D(cGameCtnMediaBlockTriangles, timeMillis).Cast<T>();
            case CGameCtnMediaBlockCameraCustom cGameCtnMediaBlockCameraCustom:
            default:
                return ToolResult.Fail(nameof(BlockToMediaObjectConverter), ErrorCodes.BlockToMediaObjectConverter.UnsupportedBlockType);
        }
    }

}
