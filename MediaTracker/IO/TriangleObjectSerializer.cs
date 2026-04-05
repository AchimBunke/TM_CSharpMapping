using GBX.NET;
using System.Globalization;
using System.Numerics;

namespace TM_GenericMapping.Common.IO;

public static class TriangleObjectSerializer
{
    public static void Save<T>(T obj, string path) where T : IBinarySerializable
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
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


    /// <summary>
    /// AI, Not tested
    /// </summary>
    /// <param name="path"></param>
    /// <returns></returns>
    public static TriangleObject LoadFromObj(string path)
    {
        var baseDir = Path.GetDirectoryName(path)!;

        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();

        var materials = new Dictionary<string, Vector4>();

        void LoadMtl(string file)
        {
            string? current = null;

            foreach (var line in File.ReadLines(Path.Combine(baseDir, file)))
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                    continue;

                var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                if (p[0] == "newmtl")
                    current = p[1];
                else if (p[0] == "Kd" && current != null)
                {
                    materials[current] = new Vector4(
                        float.Parse(p[1], CultureInfo.InvariantCulture),
                        float.Parse(p[2], CultureInfo.InvariantCulture),
                        float.Parse(p[3], CultureInfo.InvariantCulture),
                        1f);
                }
            }
        }

        TriangleObject CreateObject(string name) => new()
        {
            Name = name,
            Vertices = Array.Empty<Vector3>(),
            Triangles = Array.Empty<Int3>(),
            Colors = Array.Empty<Vector4>()
        };

        var root = CreateObject(Path.GetFileNameWithoutExtension(path));
        var subObjects = new List<TriangleObject>();

        TriangleObject current = CreateObject("Default");
        var vList = new List<Vector3>();
        var tList = new List<Int3>();
        var cList = new List<Vector4>();
        var vertexMap = new Dictionary<string, int>();

        string currentMaterial = "";

        int ResolveIndex(int index, int count)
            => index > 0 ? index - 1 : count + index;

        void FlushObject()
        {
            if (vList.Count == 0)
                return;

            current.Vertices = vList.ToArray();
            current.Triangles = tList.ToArray();
            current.Colors = cList.ToArray();
            current.FillTrianglesCount = current.Triangles.Length;
            current.FillVertexCount = current.Vertices.Length;

            subObjects.Add(current);

            vList = new();
            tList = new();
            cList = new();
            vertexMap = new();
        }

        foreach (var raw in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(raw) || raw.StartsWith("#"))
                continue;

            var p = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            switch (p[0])
            {
                case "mtllib":
                    //LoadMtl(p[1]);
                    break;

                case "o":
                case "g":
                    FlushObject();
                    current = CreateObject(p.Length > 1 ? p[1] : "Unnamed");
                    break;

                case "usemtl":
                    currentMaterial = p[1];
                    break;

                case "v":
                    positions.Add(new Vector3(
                        float.Parse(p[1], CultureInfo.InvariantCulture),
                        float.Parse(p[2], CultureInfo.InvariantCulture),
                        float.Parse(p[3], CultureInfo.InvariantCulture)));
                    break;

                case "f":
                    {
                        var face = new List<int>();

                        for (int i = 1; i < p.Length; i++)
                        {
                            string key = p[i] + "|" + currentMaterial;

                            if (!vertexMap.TryGetValue(key, out int idx))
                            {
                                var comps = p[i].Split('/');
                                int vIndex = ResolveIndex(int.Parse(comps[0]), positions.Count);

                                idx = vList.Count;
                                vList.Add(positions[vIndex]);

                                if (materials.TryGetValue(currentMaterial, out var col))
                                    cList.Add(col);
                                else
                                    cList.Add(new Vector4(1, 1, 1, 1));

                                vertexMap[key] = idx;
                            }

                            face.Add(idx);
                        }

                        for (int i = 1; i < face.Count - 1; i++)
                        {
                            tList.Add(new Int3(face[0], face[i], face[i + 1]));
                        }

                        break;
                    }
            }
        }

        FlushObject();

        foreach(var o in subObjects)
            root.AddSubObjects(o);
        return root;
    }
}
