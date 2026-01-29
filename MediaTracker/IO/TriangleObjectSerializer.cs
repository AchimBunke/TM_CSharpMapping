namespace TM_GenericMapping.MediaTracker.IO;

public static class TriangleObjectSerializer
{
    public static void Save<T>(T obj, string path) where T : IBinarySerializable
    {
        using var w = new BinaryWriter(File.Create(path));
        obj.Write(w);
    }
    public static void Save(TriangleObject obj, string path)
    {
        var triangleObjectData = obj.AsTriangleObjectData();
        Save(triangleObjectData, path);
    }

    public static T Load<T>(string path) where T : IBinarySerializable, new()
    {
        try
        {
            using var r = new BinaryReader(File.OpenRead(path));
            var obj = new T();
            obj.Read(r);
            return obj;
        }
        catch (EndOfStreamException)
        {
            throw new InvalidDataException("Unexpected end of file");
        }
    }

    public static TriangleObject Load(string path)
    {
        var triangleObjectData = Load<TriangleObjectData>(path);

        var triangleObject = new TriangleObject(triangleObjectData);
        return triangleObject;
    }
}
