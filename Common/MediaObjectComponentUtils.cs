using TM_GenericMapping.MediaTracker.Components;

namespace TM_GenericMapping.Common;

public static class MediaObjectComponentUtils
{
    public static IEnumerable<ObjectType> FindObjects<ComponentType, ObjectType>(MediaObject root) 
        where ComponentType : IMediaObjectComponent
        where ObjectType : MediaObject
    {
        if (root is ObjectType obj && root.TryGetComponent<ComponentType>(out var _))
            yield return obj;
        foreach (var s in root.SubObjects)
            foreach (var found in FindObjects<ComponentType, ObjectType>(s))
                yield return found;
    }

    public static IEnumerable<MediaObject> FindObjects<ComponentType>(MediaObject root)
       where ComponentType : IMediaObjectComponent
        => FindObjects<ComponentType, MediaObject>(root);
}
