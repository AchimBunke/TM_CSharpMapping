using GBX.NET;
using System.Collections;
using System.Drawing.Imaging;
using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using TM_GenericMapping.Common;

namespace TM_GenericMapping.MediaTracker.IO;

public static class ObjSerializer
{
    public struct ObjLoadSettings
    {
        public ObjMaterialMode ObjMaterialMode;
    }
    public enum ObjMaterialMode
    {
        PerVertexColor,
        PerVertexColor_Interpolated,
        PerFaceColor,
    }
    public interface IObjSource
    {
        Stream Open(string name);
        bool Exists(string name);
    }

    public sealed class DiskObjSource : IObjSource
    {
        private readonly string _baseDir;
        public DiskObjSource(string baseDir) => _baseDir = baseDir;

        public Stream Open(string name) => File.OpenRead(Path.Combine(_baseDir, name));
        public bool Exists(string name) => File.Exists(Path.Combine(_baseDir, name));
    }
    public sealed class StreamObjSource : IObjSource
    {
        private readonly Dictionary<string, MemoryStream> _files;

        public StreamObjSource(Dictionary<string, MemoryStream> files) => _files = files;

        public Stream Open(string name)
        {
            if (!_files.TryGetValue(name, out var ms))
                throw new FileNotFoundException($"Stream source has no entry for '{name}'");
            ms.Position = 0;
            return ms;
        }

        public bool Exists(string name) => _files.ContainsKey(name);
    }


    public static TriangleObject LoadFromObj(string path)
        => LoadFromObj(path, new ObjLoadSettings { ObjMaterialMode = ObjMaterialMode.PerVertexColor });
    public static TriangleObject LoadFromObj(string path, ObjLoadSettings settings)
    {
        var source = new DiskObjSource(Path.GetDirectoryName(path)!);
        var name = Path.GetFileName(path);
        return LoadFromObj(name, source, settings);
    }
    public static TriangleObject LoadFromObj(string fileName, IObjSource source, ObjLoadSettings settings)
    {

        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();

        var materials = new Dictionary<string, Vector4>();

        var root = CreateEmptyObject(Path.GetFileNameWithoutExtension(fileName));

        var subObjects = new List<TriangleObject>();

        TriangleObject current = CreateEmptyObject("Default");
        var vList = new List<Vector3>();
        var tList = new List<Int3>();
        var cList = new List<Vector4>();
        var vertexMap = new Dictionary<string, int>();
        var colorMap = new Dictionary<int, (Vector4 sum, int count)>();

        string currentMaterial = "";

        using var mainReader = new StreamReader(source.Open(fileName));
        string? raw = "";
        while (!mainReader.EndOfStream)
        {
            raw = mainReader.ReadLine();

            if (string.IsNullOrWhiteSpace(raw) || raw.StartsWith("#"))
                continue;

            var p = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            switch (p[0])
            {
                case "mtllib":
                    LoadMaterial(p[1], source, materials);
                    break;

                case "o":
                case "g":
                    FlushObject(current, vList, tList, cList, vertexMap, colorMap, settings, subObjects);
                    current = CreateEmptyObject(p.Length > 1 ? p[1] : "Unnamed");
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
                            var comps = p[i].Split('/');
                            int vIndex = ResolveIndex(int.Parse(comps[0]), positions.Count);

                            string key = settings.ObjMaterialMode == ObjMaterialMode.PerFaceColor
                                ? vIndex + "|" + currentMaterial   // duplicate per material
                                : $"{vIndex}";

                            if (!vertexMap.TryGetValue(key, out int idx))
                            {

                                idx = vList.Count;
                                vList.Add(positions[vIndex]);

                                Vector4 col = materials.TryGetValue(currentMaterial, out var mCol)
                                    ? mCol
                                    : new Vector4(1, 1, 1, 1);

                                cList.Add(col);
                                colorMap[idx] = (col, 1);

                                vertexMap[key] = idx;
                            }
                            if (settings.ObjMaterialMode == ObjMaterialMode.PerVertexColor_Interpolated)
                            {

                                Vector4 col = materials.TryGetValue(currentMaterial, out var mCol)
                                    ? mCol
                                    : new Vector4(1, 1, 1, 1);
                                var curValue = colorMap[idx];
                                colorMap[idx] = (curValue.sum + col, curValue.count + 1);
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


        FlushObject(current, vList, tList, cList, vertexMap, colorMap, settings, subObjects);

        foreach (var o in subObjects)
            root.AddSubObjects(o);
        return root;
    }


    static void LoadMaterial(string mtlName, IObjSource source, Dictionary<string, Vector4> materials)
    {
        if (!source.Exists(mtlName)) return;
        string? current = null;

        using var sr = new StreamReader(source.Open(mtlName));

        string? line = "";
        while (!sr.EndOfStream)
        {
            line = sr.ReadLine();
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
    static TriangleObject CreateEmptyObject(string name) => new()
    {
        Name = name,
        Vertices = Array.Empty<Vector3>(),
        Triangles = Array.Empty<Int3>(),
        Colors = Array.Empty<Vector4>()
    };

    static int ResolveIndex(int index, int count)
        => index > 0 ? index - 1 : count + index;

    static void FlushObject(TriangleObject current, 
        List<Vector3> vList,
        List<Int3> tList, 
        List<Vector4> cList, 
        Dictionary<string, int> vertexMap,
        Dictionary<int, (Vector4 sum, int count)> colorMap, 
        ObjLoadSettings settings,
        List<TriangleObject> subObjects)
    {
        if (vList.Count == 0)
            return;

        current.Vertices = vList.ToArray();
        current.Triangles = tList.ToArray();
        current.Colors = cList.ToArray();
        current.FillTrianglesCount = current.Triangles.Length;
        current.FillVertexCount = current.Vertices.Length;

        if (settings.ObjMaterialMode == ObjMaterialMode.PerVertexColor_Interpolated)
        {
            for (int i = 0; i < current.Colors.Length; i++)
            {
                if (colorMap.TryGetValue(i, out var col))
                {
                    current.Colors[i] = col.sum / col.count;
                }
            }
        }

        subObjects.Add(current);

        vList = new();
        tList = new();
        cList = new();
        vertexMap = new();
    }





    public static StoredVertexAnimation LoadAnimationFromObj(string path, out TriangleObject baseObject)
        => LoadAnimationFromObj(path, new ObjLoadSettings { ObjMaterialMode = ObjMaterialMode.PerVertexColor }, out baseObject);
    public static StoredVertexAnimation LoadAnimationFromObj(string path, ObjLoadSettings settings, out TriangleObject baseObject)
    {
        var dir = Path.GetDirectoryName(path)!;
        var source = new DiskObjSource(dir);

        // Re-implement the frame-discovery here (it's inherently disk-specific)
        // and pass an ordered list of frame names into the source-based overload.
        var fileName = Path.GetFileNameWithoutExtension(path);
        var match = Regex.Match(fileName, @"^(.+?)_(\d+)$");

        List<string> frameNames;
        if (match.Success)
        {
            var prefix = match.Groups[1].Value;
            frameNames = GetOrderedFrameNames(
                Directory.GetFiles(dir, "*.obj")
                .Where(f => { var m = Regex.Match(Path.GetFileNameWithoutExtension(f), @"^(.+?)_(\d+)$"); return m.Success && m.Groups[1].Value == prefix; }));
        }
        else
        {
            frameNames = [Path.GetFileName(path)];
        }

        return LoadAnimationFromObj(frameNames, source, settings, out baseObject);
    }

    public static StoredVertexAnimation LoadAnimationFromObj(IReadOnlyList<string> orderedFrameNames, IObjSource source, ObjLoadSettings settings, out TriangleObject baseObject)
    {
        if (orderedFrameNames.Count == 0)
            throw new ArgumentException("At least one frame is required.", nameof(orderedFrameNames));


        var animation = new StoredVertexAnimation();

        // ── 1. Discover all frame files ───────────────────────────────────────


        //string[] framePaths;
        //int digitCount = 0;

        //var match = Regex.Match(fileName, @"^(.+?)_(\d+)$");  // require underscore separator

        //if (match.Success)
        //{
        //    var prefix = match.Groups[1].Value;  // "walk"

        //    framePaths = Directory
        //        .GetFiles(dir, "*.obj")
        //        .Where(f =>
        //        {
        //            var n = Path.GetFileNameWithoutExtension(f);
        //            var m = Regex.Match(n, @"^(.+?)_(\d+)$");
        //            return m.Success && m.Groups[1].Value == prefix;
        //            // no digit-count check — 1,2,...,11,12 are different lengths
        //        })
        //        .OrderBy(f =>
        //        {
        //            var n = Path.GetFileNameWithoutExtension(f);
        //            var m = Regex.Match(n, @"_(\d+)$");
        //            return m.Success ? int.Parse(m.Groups[1].Value) : 0;  // numeric sort
        //        })
        //        .ToArray();
        //}
        //else
        //{
        //    // No numeric suffix — treat as a single-frame "animation"
        //    framePaths = new[] { path };
        //}

        //if (framePaths.Length == 0)
        //    throw new FileNotFoundException(
        //        $"No frame .obj files found for pattern derived from: {path}");

        // ── 2. Load frame 0 — this defines topology ───────────────────────────
        var reference = LoadFromObj(orderedFrameNames[0], source, settings);
        int totalReferenceVertices = CountVertices(reference);

        List<StoredVertexAnimationFrame> frames = new List<StoredVertexAnimationFrame>();
        // ── 3. Parse only "v" lines from frames 1..N ─────────────────────────
        for (int frameIndex = 0; frameIndex < orderedFrameNames.Count; frameIndex++)
        {
            var positions = ParsePositionsOnly_New(orderedFrameNames[frameIndex], source, settings);

            if (positions.Count != totalReferenceVertices)
            {
                Logger.Error(
                    $"[LoadAnimationFromObj] Frame {frameIndex} ({Path.GetFileName(orderedFrameNames[frameIndex])}) " +
                    $"has {positions.Count} vertices but reference has {totalReferenceVertices}. Skipping.");
                continue;
            }
            ApplyFramePositions(reference, positions, frameIndex, frames);
        }
        animation.VertexAnimationFrames = frames.ToArray();
        baseObject = reference;
        return animation;

        // ── Local helpers ─────────────────────────────────────────────────────

        static int CountVertices(TriangleObject root)
        {
            int count = root.Vertices?.Length ?? 0;
            if (root.SubObjects != null)
                foreach (var sub in root.SubObjects.OfType<TriangleObject>())
                    count += CountVertices(sub);
            return count;
        }


        static List<Vector3> ParsePositionsOnly_New(string fileName, IObjSource source, ObjLoadSettings settings)
        {
            // Raw OBJ positions (global list, 1-based in the file)
            var positions = new List<Vector3>();

            // Deduplicated local vertex list (same logic as LoadFromObj)
            var vList = new List<Vector3>();

            // Use int key instead of string to avoid per-face allocations.
            // For PerFaceColor we still need a compound key — store as long (hi=vIdx, lo=matId).
            var vertexMapInt = new Dictionary<int, int>();   // PerVertexColor / Interpolated
            var vertexMapLong = new Dictionary<long, int>();  // PerFaceColor

            string currentMaterial = "";
            int currentMatId = 0;
            var matIds = new Dictionary<string, int>();

            bool perFace = settings.ObjMaterialMode == ObjMaterialMode.PerFaceColor;
            using var mainReader = new StreamReader(source.Open(fileName));
            string? raw = "";
            while (!string.IsNullOrEmpty(raw = mainReader.ReadLine()))
            {
                if (raw.Length == 0) continue;

                // Skip comment lines without allocating
                if (raw[0] == '#') continue;

                // Peek at the first token without splitting the whole line
                // "v "  → position
                // "f "  → face
                // "usemtl " → material
                // everything else → skip
                var span = raw.AsSpan();

                // ── "v " ──────────────────────────────────────────────────────────────
                if (span.Length >= 2 && span[0] == 'v' && span[1] == ' ')
                {
                    // Parse the three floats directly from the span — zero allocations
                    span = span.Slice(2);
                    if (TryParseFloat(ref span, out float x) &&
                        TryParseFloat(ref span, out float y) &&
                        TryParseFloat(ref span, out float z))
                    {
                        positions.Add(new Vector3(x, y, z));
                    }
                    continue;
                }

                // ── "usemtl " ─────────────────────────────────────────────────────────
                if (span.StartsWith("usemtl ".AsSpan(), StringComparison.Ordinal))
                {
                    currentMaterial = raw.Substring(7).Trim();
                    if (!matIds.TryGetValue(currentMaterial, out currentMatId))
                    {
                        currentMatId = matIds.Count;
                        matIds[currentMaterial] = currentMatId;
                    }
                    continue;
                }

                // ── "f " ──────────────────────────────────────────────────────────────
                // Only needed to build the deduplicated vList (same dedup as LoadFromObj)
                if (span.Length >= 2 && span[0] == 'f' && span[1] == ' ')
                {
                    span = span.Slice(2);
                    while (span.Length > 0)
                    {
                        // Skip leading spaces
                        span = span.TrimStart();
                        if (span.Length == 0) break;

                        // Find end of this face-vertex token (next space or end)
                        int tokenEnd = span.IndexOf(' ');
                        var token = tokenEnd < 0 ? span : span.Slice(0, tokenEnd);
                        span = tokenEnd < 0 ? ReadOnlySpan<char>.Empty : span.Slice(tokenEnd + 1);

                        // Parse only the position index (before the first '/')
                        int slash = token.IndexOf('/');
                        var idxSpan = slash < 0 ? token : token.Slice(0, slash);


                        if (!int.TryParse(idxSpan, out int rawIdx)) continue;
                        int vIndex = ResolveIndex(rawIdx, positions.Count);

                        if (perFace)
                        {
                            long key = ((long)vIndex << 32) | (uint)currentMatId;
                            if (!vertexMapLong.TryGetValue(key, out int idx))
                            {
                                idx = vList.Count;
                                vList.Add(positions[vIndex]);
                                vertexMapLong[key] = idx;
                            }
                        }
                        else
                        {
                            if (!vertexMapInt.TryGetValue(vIndex, out int idx))
                            {
                                idx = vList.Count;
                                vList.Add(positions[vIndex]);
                                vertexMapInt[vIndex] = idx;
                            }
                        }
                    }
                    continue;
                }

                // All other tokens (vn, vt, o, g, s, mtllib…) — skip cheaply
            }

            return vList;
        }


        // ── Span-based float parser ───────────────────────────────────────────────────
        // Advances `span` past the consumed token + trailing whitespace.
        static bool TryParseFloat(ref ReadOnlySpan<char> span, out float value)
        {
            span = span.TrimStart();
            if (span.IsEmpty) { value = 0; return false; }

            int end = span.IndexOf(' ');
            var token = end < 0 ? span : span.Slice(0, end);
            span = end < 0 ? ReadOnlySpan<char>.Empty : span.Slice(end + 1);

            return float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

        }

        static void ApplyFramePositions(
            TriangleObject root,
            List<Vector3> allPositions,
            int frameIndex,
            List<StoredVertexAnimationFrame> frames)
        {
            // Walk the sub-object tree in the same depth-first order that
            // LoadFromObj produced it so that vertex slices stay aligned.
            int offset = 0;
            var animframe = new StoredVertexAnimationFrame();
            frames.Add(animframe);
            ApplyRecursive(root, allPositions, frameIndex, ref offset, animframe);
        }

        static void ApplyRecursive(
            TriangleObject obj,
            List<Vector3> allPositions,
            int frameIndex,
            ref int offset,
            StoredVertexAnimationFrame storedAnimationFrame)
        {
            int count = obj.Vertices?.Length ?? 0;
            if (count > 0)
            {
                var frameVerts = new Vector3[count];
                for (int i = 0; i < count; i++)
                    frameVerts[i] = allPositions[offset + i];

                storedAnimationFrame.Vertices = frameVerts;
                offset += count;
            }

            if (obj.SubObjects != null)
            {
                List<StoredVertexAnimationFrame> subFrames = new();
                foreach (var sub in obj.SubTriangleObjects)
                {
                    var subAnimationFrame = new StoredVertexAnimationFrame();
                    subFrames.Add(subAnimationFrame);
                    ApplyRecursive(sub, allPositions, frameIndex, ref offset, subAnimationFrame);
                }
                storedAnimationFrame.SubAnimations = subFrames.ToArray();
            }
        }
    }

    public static List<string> GetOrderedFrameNames(IEnumerable<string> fileNames)
    {
        // Match anything ending in _<digits>.obj or _<digits>.mtl
        var frameRegex = new Regex(@"^(.+)_(\d+)\.(obj)$", RegexOptions.IgnoreCase);

        var groups = fileNames
            .Select(f => (name: f, match: frameRegex.Match(f)))
            .Where(x => x.match.Success)
            .GroupBy(x => x.match.Groups[1].Value) // group by prefix ("ship1", "plane")
            .OrderByDescending(g => g.Count())      // most frames wins
            .First();

        return groups
            .OrderBy(x => int.Parse(x.match.Groups[2].Value))  // numeric sort on _X
            .ThenBy(x => x.match.Groups[3].Value)              // .mtl before .obj (or vice versa)
            .Select(x => x.name)
            .ToList();
    }




}
