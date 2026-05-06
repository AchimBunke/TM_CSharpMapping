using TM_GenericMapping.Common;

namespace TM_GenericMapping.MediaTracker;


public class SceneCameraManager
{
    public SceneCameraManager(SceneTimeline scene)
    {
        Scene = scene;
    }
    public SceneTimeline Scene { get; }

    Dictionary<CameraObject, (bool stopped, ulong startTimeMillis)> cameras = [];

    public CameraObject? ActiveCamera
    {
        get;
        private set;
    }
    public bool HasActiveCamera => ActiveCamera != null;

    internal void AddCamera(CameraObject camera)
    {
        ExceptionUtils.Ensure(!cameras.ContainsKey(camera), () => new InvalidOperationException("Camera already added to the scene"));
        cameras[camera] = (false, Scene.AnimationTimeMillis);
        if (!HasActiveCamera)
            ActiveCamera = camera;
    }
    public void StopCamera(CameraObject camera)
    {
        ExceptionUtils.Ensure(cameras.ContainsKey(camera), () => new InvalidOperationException("Camera not added to scene!"));
        var cameraData = cameras[camera];
        cameraData.stopped = true;
        cameras[camera] = cameraData;

        Scene.RequireKeyFrame(camera);

        if (ActiveCamera == camera)
            ActiveCamera = null;
    }
}

