using TM_GenericMapping.Common;

namespace TM_GenericMapping.MediaTracker.Components;

public abstract class UpdatableComponentBase : IUpdatableComponent
{
    public bool IsInitialized { get; private set; }

    public virtual void OnUpdate(MediaObject obj, SceneTimeline scene, float deltaTime) { }
    public virtual void OnUpdate(MediaObject obj, SceneTimeline scene, ulong deltaTimeMillis) { }
    public virtual void OnInit(MediaObject obj, SceneTimeline scene) { }

    public void Init(MediaObject obj, SceneTimeline scene)
    {
        if(IsInitialized)
            return;

        IsInitialized = true;
        OnInit(obj, scene);
    }

    public void Update(MediaObject obj, SceneTimeline scene, ulong deltaTimeMillis)
    {
        OnUpdate(obj, scene, deltaTimeMillis);
        OnUpdate(obj, scene, deltaTimeMillis / 1000f);
    }

}
