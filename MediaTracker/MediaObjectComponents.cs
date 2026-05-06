using System.Collections.ObjectModel;
using System.Reflection;

namespace TM_GenericMapping.Common;


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

[ComponentId("BuildInTypeList")]
public class BuildInTypeListComponent : ISerializableComponent, ICloneableComponent
{
    public List<object> Items { get; set; } = [];
    public BuildInTypeListComponent() { }
    public BuildInTypeListComponent(params ReadOnlySpan<object> items)
    {
        foreach (var item in items)
        {
            if (item is not int
                && item is not float
                && item is not double
                && item is not bool
                && item is not string)
            {
                throw new ArgumentException($"Type {item?.GetType().FullName} is not allowed");
            }

            this.Items.Add(item);
        }
    }
    public ICloneableComponent Clone(MediaObject container)
    {
        return new BuildInTypeListComponent() { Items = [.. this.Items] };
    }

    public void Deserialize(BinaryReader r, int version)
    {
        int count = r.ReadInt32();
        Items = new List<object>(count);
        for (int i = 0; i < count; ++i)
        {
            int type = r.ReadInt32();
            Items.Add(type switch
            {
                0 => r.ReadInt32(),
                1 => r.ReadSingle(),
                2 => r.ReadDouble(),
                3 => r.ReadString(),
                4 => r.ReadBoolean(),
                _ => 0
            });
        }
    }

    public void Serialize(BinaryWriter w, int version)
    {
        w.Write(Items.Count);
        foreach (var item in Items) 
        {
            switch (item)
            {
                case int:
                    w.Write(0);
                    w.Write((int)item); break;
                case float:
                    w.Write(1);
                    w.Write((float)item); break;
                case double:
                    w.Write(2);
                    w.Write((double)item); break;
                case string:
                    w.Write(3);
                    w.Write((string)item); break;
                case bool:
                    w.Write(4);
                    w.Write((bool)item); break;
                default: break;

            }
        }
    }
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
