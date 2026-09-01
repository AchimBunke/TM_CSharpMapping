using GBX.NET.Engines.Game;
using GBX.NET.Inputs;
using TM_GenericMapping.Common;
using TM_GenericMapping.IO;
using TM_GenericMapping.MediaTracker.IO;
using TM_GenericMapping.Messaging;
using TmEssentials;
using static GBX.NET.Engines.Game.CGameCtnMediaBlock;

namespace TM_GenericMapping.MediaTracker.Tools;

public class TriangleProjector
{
    public struct ProjectionSettings
    {
        public int CameraKeyIdx = int.MaxValue;
        public bool KeyTimeAsStartTime = true;
        public bool KeyTimeInterpolation = true;
        public float AspectRatio = 16f / 9f;
        public ProjectionSettings() { }
    }
    BlockTemplates blockTemplates = MediaTrackerUtils.CreateBlockTemplates();
    BlockToMediaObjectConverter converter = new BlockToMediaObjectConverter();
    SceneAnimationSettings sceneAnimationSettings = new SceneAnimationSettings()
    {
        AnimationTickRateMillis = 1,
        MinKeyFrameTickRateMillis = 1,
    };
    public TriangleProjector()
    {

    }

    public TriangleObject Project2D(TriangleObject triangles3D, Triangle2DRenderer renderer2D)
    {
        var triangles2D = triangles3D.Clone();
        triangles2D.Renderer = renderer2D;
        return triangles2D;
    }
    public TriangleObject Project2D(TriangleObject triangles3D, IRenderingCamera projectionCamera, ProjectionSettings settings)
    {
        var renderer = new Triangle2DRenderer() { Camera = projectionCamera, IsOrthographic = false, AspectRatio = settings.AspectRatio };
        return Project2D(triangles3D, renderer);
    }
    public TriangleObject Project2D(TriangleObject triangles3D, SceneTimeline scene, ProjectionSettings settings)
    {
        return Project2D(triangles3D, new ActiveCameraTriangle2DRenderer(scene.CameraManager) { AspectRatio = settings.AspectRatio });
    }


    public ToolResult<CGameCtnMediaTrack> Project2D(CGameCtnMediaBlockTriangles3D triangles3D, CGameCtnMediaBlockCamera camera)
        => Project2D(triangles3D, camera, new());
    public ToolResult<CGameCtnMediaTrack> Project2D(CGameCtnMediaBlockTriangles3D triangles3D, CGameCtnMediaBlockCamera camera, ProjectionSettings settings)
    {
        if(camera is not IHasKeys validCameraBlock)
            return ToolResult.Fail(nameof(TriangleProjector), ErrorCodes.TriangleProjector.InvalidCameraType);

        var clip = new Clip() { SavePath = "" };
        clip.Create(blockTemplates);

        var cameraObj = converter.ReconstructObjectKey<CameraObject>(camera, settings.CameraKeyIdx);
        if (cameraObj.IsFailure)
            return ToolResult.Fail(cameraObj);
        if (cameraObj.Value is not IRenderingCamera renderingCamera)
            return ToolResult.Fail(nameof(TriangleProjector), ErrorCodes.TriangleProjector.InvalidCameraType);
        int cameraKeyTime = converter.GetKeyTime(validCameraBlock, settings.CameraKeyIdx);

        var triObj3D = converter.ReconstructTriangles3D(triangles3D, cameraKeyTime);
        if (triObj3D.IsFailure)
            return ToolResult.Fail(triObj3D);



        var projectedObj2D = Project2D(triObj3D.Value, renderingCamera, settings);
        projectedObj2D.Name = triObj3D.Value.Name + " Projected2D";
        var timeMillis = settings.KeyTimeAsStartTime ? cameraKeyTime : 0;
        ISceneScript.CreateClip(new GenericSceneBuilder((scene) =>
        {
            scene.Add(projectedObj2D);
        }), clip, sceneAnimationSettings);
        var triangleBlock = clip.MediaClip.Tracks[0].Blocks[0] as CGameCtnMediaBlockTriangles2D;
        triangleBlock!.Keys[0].Time = TimeSingle.FromMilliseconds(timeMillis);
        triangleBlock.Keys[1].Time = TimeSingle.FromMilliseconds(timeMillis + 1000);
        return ToolResult.Success(clip.MediaClip.Tracks.First(), nameof(TriangleProjector));
    }

    public ToolResult<CGameCtnMediaClip> Project2D(CGameCtnMediaClip clip, CGameCtnMediaBlockTriangles3D triangles3D, CGameCtnMediaBlockCamera camera)
        => Project2D(clip,triangles3D, camera, new());
    /// <summary>
    /// Modifies clip!
    /// </summary>
    /// <param name="clip"></param>
    /// <param name="triangles3D"></param>
    /// <param name="camera"></param>
    /// <param name="cameraKeyIdx"></param>
    /// <returns></returns>
    public ToolResult<CGameCtnMediaClip> Project2D(CGameCtnMediaClip clip, CGameCtnMediaBlockTriangles3D triangles3D, CGameCtnMediaBlockCamera camera, ProjectionSettings settings)
    {
        var trackResult = Project2D(triangles3D, camera, settings);
        if (trackResult.IsFailure)
            return ToolResult.Fail(trackResult);
        clip.Tracks.Add(trackResult.Value);
        return ToolResult.Success(clip, nameof(TriangleProjector));
    }

    public ToolResult<CGameCtnMediaClip> Project2D(CGameCtnMediaClip clip, CGameCtnMediaTrack triangleTrack, CGameCtnMediaTrack cameraTrack)
        => Project2D(clip, triangleTrack, cameraTrack, new());
    public ToolResult<CGameCtnMediaClip> Project2D(CGameCtnMediaClip clip, CGameCtnMediaTrack triangleTrack, CGameCtnMediaTrack cameraTrack, ProjectionSettings settings)
    {
        var cameraBlock = cameraTrack.Blocks[0] as CGameCtnMediaBlockCamera;
        if(cameraBlock is not IHasKeys hasKeys)
            return ToolResult.Fail(nameof(TriangleProjector), ErrorCodes.TriangleProjector.InvalidCameraType);
        var keyTime = converter.GetKeyTime(hasKeys, settings.CameraKeyIdx);
        if (!converter.TryGetBlockAtTime(triangleTrack, keyTime, out var block))
            block = triangleTrack.Blocks.LastOrDefault();
        if (block is not CGameCtnMediaBlockTriangles3D triangles3D || triangles3D == null)
            return ToolResult.Fail(nameof(TriangleProjector), ErrorCodes.TriangleProjector.MissingTriangleBlock);
        return Project2D(clip, triangles3D, cameraBlock, settings);
    }


}
