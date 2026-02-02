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
