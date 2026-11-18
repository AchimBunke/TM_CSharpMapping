using GBX.NET;
using GBX.NET.Components;
using GBX.NET.Engines.MwFoundations;
using GBX.NET.Engines.Plug;
using GBX.NET.Serialization.Chunking;
using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace TM_GenericMapping.Items.MeshCompilation;

/// <summary>
/// Inverse of <see cref="ParticleFxConverter"/>: writes a parsed particle node tree (e.g. a Nadeo
/// <c>*.ParticleModel.Gbx</c> = <see cref="CPlugParticleEmitterModel"/>, or a <c>*.FxSys.Gbx</c> = <see cref="CPlugFxSystem"/>)
/// as "tm-fx/1" json, member names as GBX.NET C# members:
/// <c>{"$class", "props": {Named: v}, "chunks": {"0x090C6000": {"Version", "U01"}}}</c>, archives as <c>{"$struct": "Owner.Name", "props"}</c>.
/// <para>
/// File references: only named <see cref="CPlugBitmap"/> members (the ones the converter can write back as file reference)
/// keep their path; every other reference (materials, shaders, anonymous chunk members, ...) is written empty and reported
/// in <see cref="Warnings"/>, because game-internal paths can not be resolved when converting an item.
/// </para>
/// </summary>
public sealed partial class ParticleFxJsonWriter
{
    public IList<string> Warnings { get; } = new List<string>();

    const BindingFlags Inst = BindingFlags.Public | BindingFlags.Instance;
    static readonly HashSet<string> SkippedMembers = ["GameVersion", "Chunks", "References", "Id", "Ignore"];

    [GeneratedRegex(@"^U\d+$")]
    private static partial Regex AnonymousMember();

    public JsonObject WriteDocument(CMwNod root, string? source = null)
    {
        var doc = new JsonObject { ["format"] = ParticleFxConverter.SupportedFormat };
        if (source is not null)
            doc["source"] = source;
        doc["vfx"] = WriteNode(root, "vfx");
        return doc;
    }

    // ------------------------------------------------------------------------- nodes
    JsonObject WriteNode(CMwNod node, string path)
    {
        var type = node.GetType();
        var props = new JsonObject();
        var refs = type.GetProperty("References", Inst)?.GetValue(node);
        foreach (var p in type.GetProperties(Inst))
        {
            if (!IsDataMember(p) || p.DeclaringType == typeof(CMwNod))
                continue;
            var refFile = refs is null ? null : FileOf(refs.GetType().GetProperty(p.Name, Inst)?.GetValue(refs));
            props[p.Name] = WriteMember(p.PropertyType, () => p.GetValue(node), refFile, named: true, $"{path}.{p.Name}");
        }

        var chunks = new JsonObject();
        foreach (var chunk in node.Chunks)
        {
            var body = new JsonObject();
            if (chunk is IVersionable v)
                body["Version"] = v.Version;
            var chunkRefs = (chunk as Chunk)?.EnumerateReferences().ToDictionary(kv => kv.Key, kv => kv.Value) ?? [];
            // GBX.NET generates anonymous chunk members as public fields (U01, ...), some as properties
            var members = chunk.GetType().GetFields(Inst)
                .Where(f => !f.IsInitOnly)
                .Select(f => (f.Name, Type: f.FieldType, Get: (Func<object?>)(() => f.GetValue(chunk))))
                .Concat(chunk.GetType().GetProperties(Inst).Where(IsDataMember)
                    .Select(p => (p.Name, Type: p.PropertyType, Get: (Func<object?>)(() => p.GetValue(chunk)))))
                .Where(m => AnonymousMember().IsMatch(m.Name))
                .OrderBy(m => int.Parse(m.Name[1..], CultureInfo.InvariantCulture));
            foreach (var (name, memberType, get) in members)
            {
                chunkRefs.TryGetValue(name, out var nodeRef);
                body[name] = WriteMember(memberType, get, FileOf(nodeRef), named: false,
                    $"{path}.chunks[0x{chunk.Id:X8}].{name}");
            }
            chunks[$"0x{chunk.Id:X8}"] = body;
        }

        return new JsonObject { ["$class"] = type.Name, ["props"] = props, ["chunks"] = chunks };
    }

    static bool IsDataMember(PropertyInfo p)
        => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0 && !SkippedMembers.Contains(p.Name)
           && p.GetSetMethod() is not null;

    static GbxRefTableFile? FileOf(object? nodeRef)
        => nodeRef is NodeRef r ? r.Descriptor as GbxRefTableFile : null;

    /// <summary>Value of a member; node typed members follow the reference rules (see class summary).</summary>
    JsonNode? WriteMember(Type memberType, Func<object?> get, GbxRefTableFile? refFile, bool named, string path)
    {
        var under = Nullable.GetUnderlyingType(memberType) ?? memberType;
        if (typeof(CMwNod).IsAssignableFrom(under))
        {
            if (typeof(CPlugBitmap).IsAssignableFrom(under) && named)
            {
                if (refFile is not null)
                    return refFile.FilePath;
                if (SafeGet(get) is not null)
                    Warnings.Add($"{path}: embedded bitmap is not supported, left empty (pick a bitmap asset)");
                return "";
            }
            if (IsReferenceOnly(under))
            {
                if (refFile is not null)
                    Warnings.Add($"{path}: reference '{refFile.FilePath}' left empty (only named bitmaps are kept)");
                else if (SafeGet(get) is not null)
                    Warnings.Add($"{path}: embedded {under.Name} left empty");
                return "";
            }
            if (refFile is not null)   // e.g. an emitter in its own .ParticleModel.Gbx: written inline when GBX.NET loaded it
            {
                if (SafeGet(get) is CMwNod external)
                    return WriteNode(external, path);
                Warnings.Add($"{path}: referenced file '{refFile.FilePath}' could not be loaded, left empty");
                return null;
            }
        }
        object? value;
        try { value = get(); }
        catch (Exception e) { Warnings.Add($"{path}: {e.Message}"); return null; }
        return WriteValue(value, path);
    }

    static object? SafeGet(Func<object?> get)
    {
        try { return get(); } catch { return null; }
    }

    /// <summary>Classes the editor authors as path strings, never as nested node (see FX/tools/gen_schema.py REF_CLASSES).</summary>
    static bool IsReferenceOnly(Type t) => t.Name is "CPlugBitmap" or "CPlugMaterial" or "CPlugShader" or "CPlugSound"
        or "CPlugDecalModel" or "CPlugSoundSurface" or "CPlugSolid2Model" or "CPlugFileImg" or "CPlugBitmapAtlas";

    // ------------------------------------------------------------------------ values
    JsonNode? WriteValue(object? value, string path)
    {
        switch (value)
        {
            case null: return null;
            case string s: return s;
            case bool b: return b;
            case float f: return (double)f;
            case double d: return d;
            case byte[] bytes: return Convert.ToHexString(bytes).ToLowerInvariant();
            case Enum e: return Convert.ToInt64(e, CultureInfo.InvariantCulture);
            case CMwNod n: return WriteNode(n, path);
            case PlugCurve c: return WriteCurve(c);
        }
        var t = value.GetType();
        if (t.IsPrimitive)
            return IsInteger(t)
                ? JsonValue.Create(Convert.ToInt64(value, CultureInfo.InvariantCulture))
                : JsonValue.Create(Convert.ToDouble(value, CultureInfo.InvariantCulture));
        if (value is IEnumerable list)
        {
            var arr = new JsonArray();
            var i = 0;
            foreach (var item in list)
                arr.Add(WriteValue(item, $"{path}[{i++}]"));
            return arr;
        }
        if (t.IsValueType && t.Namespace == "GBX.NET")
        {
            var nums = new List<double>();
            if (TryFlatten(value, nums))
                return new JsonArray(nums.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray());
            return value.ToString();
        }
        if (t.IsClass && t.DeclaringType is not null && typeof(CMwNod).IsAssignableFrom(t.DeclaringType))
            return WriteStruct(value, path);
        Warnings.Add($"{path}: {t.Name} is not supported, omitted");
        return null;
    }

    static bool IsInteger(Type t) => t != typeof(float) && t != typeof(double);

    JsonObject WriteStruct(object value, string path)
    {
        var t = value.GetType();
        var props = new JsonObject();
        foreach (var p in t.GetProperties(Inst))
        {
            if (!IsDataMember(p))
                continue;
            props[p.Name] = WriteMember(p.PropertyType, () => p.GetValue(value), null, named: false, $"{path}.{p.Name}");
        }
        return new JsonObject { ["$struct"] = $"{t.DeclaringType!.Name}.{t.Name}", ["props"] = props };
    }

    static JsonObject WriteCurve(PlugCurve c)
    {
        var points = new JsonArray();
        foreach (var v in c.Values ?? [])
            points.Add(new JsonArray(JsonValue.Create((double)v.X), JsonValue.Create((double)v.Y)));
        return new JsonObject { ["version"] = c.Version, ["flags"] = (int)c.Flags, ["points"] = points };
    }

    /// <summary>Numbers of a vector-like struct in constructor order (Vec3, Int3, Iso4, BoxAligned, ...).</summary>
    static bool TryFlatten(object value, List<double> into)
    {
        var t = value.GetType();
        if (t.IsPrimitive)
        {
            into.Add(Convert.ToDouble(value, CultureInfo.InvariantCulture));
            return true;
        }
        var ctor = t.GetConstructors().OrderByDescending(c => c.GetParameters().Length).FirstOrDefault();
        if (ctor is null || ctor.GetParameters().Length == 0)
            return false;
        foreach (var param in ctor.GetParameters())
        {
            var prop = t.GetProperty(param.Name!, Inst | BindingFlags.IgnoreCase);
            if (prop is null || !TryFlatten(prop.GetValue(value)!, into))
                return false;
        }
        return true;
    }
}
