using GBX.NET;
using GBX.NET.Components;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.MwFoundations;
using GBX.NET.Engines.Plug;
using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace TM_GenericMapping.Items.MeshCompilation;

public sealed class FxConvertException : Exception
{
    public FxConvertException(string path, string message, Exception? inner = null)
        : base($"{path}: {message}", inner) { }
}

public sealed class ParticleFxConverter
{
    public const string SupportedFormat = "tm-fx/1";

    /// <summary>Non-fatal problems (unknown members, ignored extra data).</summary>
    public IList<string> Warnings { get; } = new List<string>();

    /// <summary>Throw on unknown classes/members instead of warning.</summary>
    public bool Strict { get; set; }

    /// <summary>
    /// Resolves a file path string to a node-typed member (e.g. a CPlugMaterial or CPlugBitmap
    /// reference).  Default: unsupported, empty strings become null.
    /// </summary>
    public Func<Type, string, object?>? ExternalRefResolver { get; set; }

    /// <summary>
    /// Tried before <see cref="ExternalRefResolver"/> for strings on node-typed members. A non-null result is written
    /// as a file reference into the owner's References.&lt;Member&gt;.Descriptor (how Nadeo's fx reference their
    /// .Texture.Gbx bitmaps), so the game loads the node from disk instead of it being embedded. Return null to fall back.
    /// </summary>
    public Func<Type, string, GbxRefTableFile?>? FileRefResolver { get; set; }

    private static readonly Lazy<Dictionary<string, Type>> NodeTypes = new(() =>
    {
        var map = new Dictionary<string, Type>(StringComparer.Ordinal);
        foreach (var t in typeof(CMwNod).Assembly.GetTypes())
            if (!t.IsAbstract && typeof(CMwNod).IsAssignableFrom(t)) map.TryAdd(t.Name, t);
        return map;
    });

    private const BindingFlags Inst = BindingFlags.Public | BindingFlags.Instance;

    // ------------------------------------------------------------------ entry points
    public CPlugVFXFile LoadFile(string path) => Convert(File.ReadAllText(path));

    public CPlugVFXFile Convert(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return Convert(doc.RootElement);
    }

    /// <summary>Accepts the full document ({"format","vfx"}) or a bare CPlugVFXFile node.</summary>
    public CPlugVFXFile Convert(JsonElement document) => ConvertRoot<CPlugVFXFile>(document);

    // CPlugFxSystem is what item prefab ents reference (see Nadeo's *.FxSys.Gbx + *.Prefab.Gbx)
    public CPlugFxSystem LoadFxSystemFile(string path) => ConvertFxSystem(File.ReadAllText(path));

    public CPlugFxSystem ConvertFxSystem(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return ConvertFxSystem(doc.RootElement);
    }

    /// <summary>Accepts the full document ({"format","vfx"}) or a bare CPlugFxSystem node.</summary>
    public CPlugFxSystem ConvertFxSystem(JsonElement document) => ConvertRoot<CPlugFxSystem>(document);

    private T ConvertRoot<T>(JsonElement document) where T : CMwNod
    {
        var root = document;
        if (document.TryGetProperty("format", out var fmt) && fmt.GetString() != SupportedFormat)
            throw new FxConvertException("$", $"unsupported format {fmt.GetString()} (expected {SupportedFormat})");
        if (document.TryGetProperty("vfx", out var vfx)) root = vfx;

        var node = BuildNode(root, "vfx");
        return node as T
            ?? throw new FxConvertException("vfx", $"root must be {typeof(T).Name}, got {node.GetType().Name}");
    }

    public void ApplyTo(CGameItemModel item, JsonElement document) => item.VFX = Convert(document);

    // ------------------------------------------------------------------------- nodes
    private CMwNod BuildNode(JsonElement el, string path)
    {
        var cname = Required(el, "$class", path).GetString() ?? "";
        if (!NodeTypes.Value.TryGetValue(cname, out var type))
            throw new FxConvertException(path, $"unknown GBX.NET class '{cname}'");

        var node = (CMwNod)Activator.CreateInstance(type, nonPublic: true)!;

        if (el.TryGetProperty("chunks", out var chunks))
            foreach (var c in chunks.EnumerateObject())
                ApplyChunk(node, type, c.Name, c.Value, $"{path}.chunks[{c.Name}]");

        if (el.TryGetProperty("props", out var props))
            foreach (var p in props.EnumerateObject())
                SetMember(node, p.Name, p.Value, $"{path}.{p.Name}");

        NoteExtra(el, path);
        return node;
    }

    private void ApplyChunk(CMwNod node, Type nodeType, string id, JsonElement body, string path)
    {
        var hex = (id.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? id[2..] : id)
            .ToUpperInvariant().PadLeft(8, '0');
        var chunkType = nodeType.GetNestedType("Chunk" + hex, BindingFlags.Public | BindingFlags.NonPublic);
        if (chunkType is null)
        {
            Problem(path, $"{nodeType.Name} has no chunk {hex}");
            return;
        }

        var create = typeof(CMwNod).GetMethod(nameof(CMwNod.CreateChunk))!.MakeGenericMethod(chunkType);
        var chunk = create.Invoke(node, null)!;

        foreach (var m in body.EnumerateObject())
            SetMember(chunk, m.Name, m.Value, $"{path}.{m.Name}");   // includes "Version"
    }

    private void SetMember(object target, string name, JsonElement value, string path)
    {
        var t = target.GetType();
        var prop = t.GetProperty(name, Inst);
        var field = prop is null ? t.GetField(name, Inst) : null;
        var memberType = prop?.PropertyType ?? field?.FieldType;
        if (memberType is null || (prop is not null && !prop.CanWrite))
        {
            Problem(path, $"{t.Name} has no writable member '{name}'");
            return;
        }

        if (value.ValueKind == JsonValueKind.String && typeof(CMwNod).IsAssignableFrom(memberType)
            && TrySetFileRef(target, name, memberType, value.GetString() ?? "", path))
            return;

        object? converted;
        try { converted = ConvertValue(value, memberType, path); }
        catch (FxConvertException) { throw; }
        catch (Exception e) { throw new FxConvertException(path, e.Message, e); }

        if (converted is null && memberType.IsValueType && Nullable.GetUnderlyingType(memberType) is null)
            return;   // null for a non-nullable value type: keep the default

        if (prop is not null) prop.SetValue(target, converted);
        else field!.SetValue(target, converted);
    }

    // ------------------------------------------------------------------------ structs
    private object BuildStruct(JsonElement el, string path)
    {
        var qname = Required(el, "$struct", path).GetString() ?? "";
        var parts = qname.Split('.');
        if (parts.Length != 2 || !NodeTypes.Value.TryGetValue(parts[0], out var owner))
            throw new FxConvertException(path, $"cannot resolve struct '{qname}'");
        var type = owner.GetNestedType(parts[1], BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new FxConvertException(path, $"{parts[0]} has no nested type '{parts[1]}'");

        var obj = Activator.CreateInstance(type, nonPublic: true)!;
        if (el.TryGetProperty("props", out var props))
            foreach (var p in props.EnumerateObject())
                SetMember(obj, p.Name, p.Value, $"{path}.{p.Name}");
        NoteExtra(el, path);
        return obj;
    }

    // ------------------------------------------------------------------------- curves
    private static PlugCurve BuildCurve(JsonElement el)
    {
        var pts = new List<Vec2>();
        if (el.TryGetProperty("points", out var arr))
            foreach (var p in arr.EnumerateArray())
                pts.Add(new Vec2((float)p[0].GetDouble(), (float)p[1].GetDouble()));

        return new PlugCurve
        {
            Version = el.TryGetProperty("version", out var v) ? v.GetInt32() : 3,
            Flags = el.TryGetProperty("flags", out var f) ? (ushort)f.GetInt32() : (ushort)0,
            FlagsV1 = 0,
            Count = pts.Count,
            Capacity = pts.Count,
            Values = pts.ToArray(),
        };
    }

    // ------------------------------------------------------------------------ values
    private object? ConvertValue(JsonElement el, Type target, string path)
    {
        var under = Nullable.GetUnderlyingType(target) ?? target;
        if (el.ValueKind == JsonValueKind.Null) return null;

        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
                if (el.TryGetProperty("$class", out _)) return BuildNode(el, path);
                if (el.TryGetProperty("$struct", out _)) return BuildStruct(el, path);
                if (under == typeof(PlugCurve)) return BuildCurve(el);
                throw new FxConvertException(path, "object has neither $class, $struct nor curve shape");

            case JsonValueKind.Array:
                return ConvertArray(el, under, path);

            case JsonValueKind.String:
                return ConvertString(el.GetString() ?? "", under, path);

            case JsonValueKind.True:
            case JsonValueKind.False:
                return el.GetBoolean();

            case JsonValueKind.Number:
                return ConvertNumber(el, under, path);
        }
        throw new FxConvertException(path, $"unsupported JSON {el.ValueKind}");
    }

    private object? ConvertString(string s, Type under, string path)
    {
        if (under == typeof(string)) return s;
        if (under == typeof(byte[])) return FromHex(s, path);
        if (typeof(CMwNod).IsAssignableFrom(under))        // external file reference
        {
            if (s.Length == 0) return null;
            if (ExternalRefResolver is null)
                throw new FxConvertException(path, $"external reference '{s}' ({under.Name}) needs ExternalRefResolver");
            return ExternalRefResolver(under, s);
        }
        throw new FxConvertException(path, $"cannot convert string to {under.Name}");
    }

    // GBX.NET exposes node refs as target.References.<Member> (a NodeRef<T> class inside a struct view),
    // setting its Descriptor makes the member a file reference in the ref table when saved
    private bool TrySetFileRef(object target, string memberName, Type nodeType, string s, string path)
    {
        if (s.Length == 0 || FileRefResolver is null) return false;
        GbxRefTableFile? file;
        try { file = FileRefResolver(nodeType, s); }
        catch (Exception e) { throw new FxConvertException(path, e.Message, e); }
        if (file is null) return false;

        var refs = target.GetType().GetProperty("References", Inst)?.GetValue(target);
        var slot = refs?.GetType().GetProperty(memberName, Inst)?.GetValue(refs);
        var descriptor = slot?.GetType().GetProperty("Descriptor", Inst);
        if (descriptor is null)
            throw new FxConvertException(path, $"{target.GetType().Name}.{memberName} cannot hold a file reference");
        descriptor.SetValue(slot, file);
        return true;
    }

    private static object ConvertNumber(JsonElement el, Type under, string path)
    {
        if (under.IsEnum) return Enum.ToObject(under, el.GetInt64());
        if (under == typeof(float)) return (float)el.GetDouble();
        if (under == typeof(double)) return el.GetDouble();
        if (under == typeof(int)) return el.GetInt32();
        if (under == typeof(uint)) return el.GetUInt32();
        if (under == typeof(short)) return el.GetInt16();
        if (under == typeof(ushort)) return el.GetUInt16();
        if (under == typeof(byte)) return el.GetByte();
        if (under == typeof(sbyte)) return el.GetSByte();
        if (under == typeof(long)) return el.GetInt64();
        if (under == typeof(ulong)) return el.GetUInt64();
        throw new FxConvertException(path, $"cannot convert number to {under.Name}");
    }

    private object? ConvertArray(JsonElement el, Type under, string path)
    {
        // NodeRefList<T> (node lists on nodes and chunks)
        if (under.IsGenericType && under.GetGenericTypeDefinition() == typeof(NodeRefList<>))
        {
            var elem = under.GetGenericArguments()[0];
            var list = (IList)Activator.CreateInstance(under)!;
            var i = 0;
            foreach (var item in el.EnumerateArray())
                list.Add(ConvertValue(item, elem, $"{path}[{i++}]"));
            return list;
        }

        if (under.IsArray)
        {
            var elem = under.GetElementType()!;
            var arr = Array.CreateInstance(elem, el.GetArrayLength());
            var i = 0;
            foreach (var item in el.EnumerateArray())
            {
                arr.SetValue(ConvertValue(item, elem, $"{path}[{i}]"), i);
                i++;
            }
            return arr;
        }

        if (under.IsGenericType && typeof(IList).IsAssignableFrom(under))     // List<T>
        {
            var elem = under.GetGenericArguments()[0];
            var list = (IList)Activator.CreateInstance(under)!;
            var i = 0;
            foreach (var item in el.EnumerateArray())
                list.Add(ConvertValue(item, elem, $"{path}[{i++}]"));
            return list;
        }

        if (under.IsValueType)                                                // Vec2/Vec3/Int3/Iso4/Box...
            return ConvertVector(el.EnumerateArray().Select(x => x.GetDouble()).ToArray(), under, path);

        throw new FxConvertException(path, $"cannot convert array to {under.Name}");
    }

    /// <summary>Builds a numeric record struct (Vec3, Int3, Iso4, ...) via its matching constructor.</summary>
    private static object ConvertVector(double[] nums, Type type, string path)
    {
        foreach (var ctor in type.GetConstructors())
        {
            var ps = ctor.GetParameters();
            if (ps.Length == nums.Length && ps.All(p => IsNumeric(p.ParameterType)))
                return ctor.Invoke(ps.Select((p, i) =>
                    System.Convert.ChangeType(nums[i], p.ParameterType, CultureInfo.InvariantCulture)).ToArray());

            // composite, e.g. a box made of two Vec3
            if (ps.Length > 1 && nums.Length % ps.Length == 0 && ps.All(p => p.ParameterType.IsValueType && !IsNumeric(p.ParameterType)))
            {
                var n = nums.Length / ps.Length;
                return ctor.Invoke(ps.Select((p, i) => ConvertVector(nums[(i * n)..((i + 1) * n)], p.ParameterType, path)).ToArray());
            }
        }
        throw new FxConvertException(path, $"no constructor of {type.Name} takes {nums.Length} numbers");
    }

    private static bool IsNumeric(Type t) =>
        t == typeof(float) || t == typeof(double) || t == typeof(int) || t == typeof(uint) ||
        t == typeof(short) || t == typeof(ushort) || t == typeof(byte) || t == typeof(sbyte) ||
        t == typeof(long) || t == typeof(ulong);

    private static byte[] FromHex(string s, string path)
    {
        if (s.Length % 2 != 0) throw new FxConvertException(path, "hex string has odd length");
        return s.Length == 0 ? Array.Empty<byte>() : System.Convert.FromHexString(s);
    }

    // ------------------------------------------------------------------------ helpers
    private static JsonElement Required(JsonElement el, string name, string path) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var v)
            ? v : throw new FxConvertException(path, $"missing '{name}'");

    private void NoteExtra(JsonElement el, string path)
    {
        if (el.TryGetProperty("extra", out var extra) && extra.ValueKind == JsonValueKind.Object)
            Warnings.Add($"{path}: 'extra' data (unknown to the schema) was not written to the GBX");
    }

    private void Problem(string path, string message)
    {
        if (Strict) throw new FxConvertException(path, message);
        Warnings.Add($"{path}: {message}");
    }
}
