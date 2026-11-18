using GBX.NET;
using GBX.NET.Components;
using GBX.NET.Engines.MwFoundations;
using GBX.NET.Engines.Plug;
using System.Numerics;
using System.Text.Json;
using TM_GenericMapping.Common;
using TM_GenericMapping.Items.FbxGbxConversion.Importing;
using TM_GenericMapping.Items.FbxGbxConversion.Serialization;
using TM_GenericMapping.Items.MeshCompilation;
using TM_GenericMapping.Messaging;

namespace TM_GenericMapping.Items.FbxGbxConversion;

internal class ParticleFxDef
{
    public required CPlugFxSystem FxSystem { get; set; }
    public required ParticleFxConfig Config { get; set; }
    public Vector3 Position { get; set; }
    public Quaternion Rotation { get; set; }
}

internal static class FbxParticleFxConverter
{
    /// <summary>Finds all scene nodes that have a <see cref="ParticleFxConfig"/> and loads their fx system.</summary>
    public static ToolResult<List<ParticleFxDef>> ExtractParticleFx(ImportedScene scene, ConfigIndex configIndex)
    {
        List<ParticleFxDef> defs = [];
        if (configIndex.ParticleFxConfigsByName.Count == 0)
            return ToolResult.Success(defs, nameof(FbxParticleFxConverter));

        foreach (var node in scene.CollectNodes())
        {
            if (!configIndex.ParticleFxConfigsByName.TryGetValue(node.Name, out var fxConfig))
                continue;

            var fxResult = GetOrLoadFxSystem(fxConfig, configIndex);
            if (fxResult.IsFailure)
                return ToolResult.Fail(fxResult);

            Matrix4x4.Decompose(node.GlobalTransform, out _, out var rotation, out var position);
            defs.Add(new ParticleFxDef { FxSystem = fxResult.Value, Config = fxConfig, Position = position, Rotation = rotation });
        }
        return ToolResult.Success(defs, nameof(FbxParticleFxConverter));
    }

    /// <summary>Each fx config is converted once, so every variant references the same CPlugFxSystem instance.</summary>
    static ToolResult<CPlugFxSystem> GetOrLoadFxSystem(ParticleFxConfig fxConfig, ConfigIndex configIndex)
    {
        if (configIndex.LoadedFxSystemsByName.TryGetValue(fxConfig.Name, out var cached))
            return ToolResult.Success(cached, nameof(FbxParticleFxConverter));

        if (fxConfig.Fx is not { ValueKind: JsonValueKind.Object } fxJson)
            return ToolResult.Fail<CPlugFxSystem>(nameof(FbxGbxConverter), ErrorCodes.FbxGbxConverter.InvalidParticleFx, $"{fxConfig.Name}: missing Fx object");

        var baseDirectory = configIndex.ItemConfig.BaseDirectory ?? Directory.GetCurrentDirectory();
        var loadedRefs = new Dictionary<string, CMwNod>(StringComparer.OrdinalIgnoreCase);
        var converter = new ParticleFxConverter
        {
            ExternalRefResolver = (type, reference) => ResolveExternalRef(type, reference, baseDirectory, loadedRefs),
            FileRefResolver = (type, assetId) => ResolveFileRef(type, assetId, configIndex.ItemConfig),
        };

        CPlugFxSystem fxSystem;
        try
        {
            fxSystem = converter.ConvertFxSystem(fxJson);
        }
        catch (Exception e)
        {
            return ToolResult.Fail<CPlugFxSystem>(nameof(FbxGbxConverter), ErrorCodes.FbxGbxConverter.InvalidParticleFx, $"{fxConfig.Name}: {e.Message}");
        }

        foreach (var warning in converter.Warnings)
            Logger.Warn($"ParticleFx {fxConfig.Name}: {warning}");

        configIndex.LoadedFxSystemsByName[fxConfig.Name] = fxSystem;
        return ToolResult.Success(fxSystem, nameof(FbxParticleFxConverter));
    }

    /// <summary>
    /// Node member strings that are not asset ids (see <see cref="ResolveFileRef"/>) are .Gbx paths relative to
    /// the config file (e.g. a SubSystem). The node is parsed and embedded.
    /// </summary>
    static object? ResolveExternalRef(Type type, string reference, string baseDirectory, Dictionary<string, CMwNod> loadedRefs)
    {
        var path = ResolvePath(reference, baseDirectory);
        if (loadedRefs.TryGetValue(path, out var cached))
            return cached;

        if (!File.Exists(path))
            throw new FileNotFoundException($"referenced file '{reference}' not found", path);

        var node = Gbx.ParseNode(path);
        if (node is null || !type.IsInstanceOfType(node))
            throw new InvalidDataException($"'{path}' is {node?.GetType().Name ?? "null"}, expected {type.Name}");

        loadedRefs[path] = node;
        return node;
    }

    /// <summary>
    /// Asset ids on node members (e.g. "Bitmap" of a CPlugParticleGpuModel) become file references like the veget
    /// tree bitmaps, not embedded. Returns null for non-asset strings so they are handled as .Gbx paths.
    /// </summary>
    static GbxRefTableFile? ResolveFileRef(Type nodeType, string assetId, ItemConfig itemConfig)
    {
        if (itemConfig.ResolveAsset(assetId) is not { } asset)
            return null;

        var expected = asset.Type switch
        {
            // particle bitmap slots expect a CPlugBitmap (.Texture.Gbx); raw images only fit TextureSet image slots
            AssetType.Bitmap => typeof(CPlugBitmap),
            AssetType.ImgFile => throw new InvalidDataException($"asset '{assetId}' is an ImgFile, particle fx need a Bitmap asset (.Texture.Gbx)"),
            // TODO: Material/Shader references once custom particle materials are needed
            AssetType.Material or AssetType.Shader => throw new NotSupportedException($"asset '{assetId}': {asset.Type} assets are not supported for particle fx yet"),
            _ => throw new NotSupportedException($"asset '{assetId}': unknown asset type {asset.Type}"),
        };
        if (!nodeType.IsAssignableFrom(expected))
            throw new InvalidDataException($"asset '{assetId}' is a {asset.Type}, member expects {nodeType.Name}");

        return NodeRefUtils.CreateFile(asset.Path);
    }

    static string ResolvePath(string path, string? baseDirectory)
        => Path.IsPathRooted(path) || string.IsNullOrEmpty(baseDirectory)
            ? path
            : Path.GetFullPath(Path.Combine(baseDirectory, path));
}
