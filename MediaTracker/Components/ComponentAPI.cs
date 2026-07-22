using System.Reflection;
using TM_GenericMapping.Common;

namespace TM_GenericMapping.MediaTracker.Components;

public interface IMediaObjectComponent { }
public interface ICloneableComponent : IMediaObjectComponent
{
    /// <summary>
    /// Must not access MediaObject data.. only for reference assignments for container objects
    /// </summary>
    /// <param name="container"></param>
    /// <returns></returns>
    ICloneableComponent Clone(MediaObject container);
}
/// <summary>
/// Must include parameterless constructor
/// </summary>
public interface ISerializableComponent : IMediaObjectComponent
{
    void Serialize(BinaryWriter w, int version);
    void Deserialize(BinaryReader r, int version);
}
public interface IUpdatableComponent : IMediaObjectComponent
{
    bool IsInitialized { get; }
    void Update(MediaObject obj, SceneTimeline scene, ulong deltaTimeMillis);
    void Init(MediaObject obj, SceneTimeline scene);
}




[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ComponentIdAttribute : Attribute
{
    public string Id { get; }

    public ComponentIdAttribute(string id)
    {
        Id = id;
    }
}
public static class ComponentRegistry
{
    private static readonly Dictionary<Type, string> typeToId = new();
    private static readonly Dictionary<string, Func<ISerializableComponent>> map = new();

    static ComponentRegistry()
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            foreach (var type in asm.GetTypes())
            {
                if (type.IsAbstract || !typeof(ISerializableComponent).IsAssignableFrom(type))
                    continue;

                var attr = type.GetCustomAttribute<ComponentIdAttribute>();
                if (attr == null)
                    continue;

                if (map.ContainsKey(attr.Id))
                    throw new Exception($"Duplicate ComponentId: {attr.Id}");

                typeToId[type] = attr.Id;
                map[attr.Id] = () => (ISerializableComponent)Activator.CreateInstance(type)!;
            }
        }
    }
    public static ISerializableComponent Create(string id)
    {
        if (!map.TryGetValue(id, out var ctor))
            throw new Exception($"Unknown component id: {id}");

        return ctor();
    }
    public static string GetId(Type type)
    {
        if (!typeToId.TryGetValue(type, out var id))
            throw new Exception($"Unknown component type: {type.FullName}");

        return id;
    }
}