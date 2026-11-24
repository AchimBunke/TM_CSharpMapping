using GBX.NET.Engines.Plug;
using TM_GenericMapping.Items.FbxGbxConversion.Serialization;
using TM_GenericMapping.Items.MeshCompilation;
using TM_GenericMapping.Messaging;

namespace TM_GenericMapping.Items.FbxGbxConversion;

/// <summary>
/// Maps a <see cref="MaterialConfigType.Custom"/> material config onto a <see cref="CPlugMaterialUserInst"/>:
/// a Nadeo shading model (Model) with textures given as UserTextures. Found by in-game tests (2026-11):
/// <list type="bullet">
/// <item>Working models: TDSN, TDOSN, TDOBSN, TDSNE, TDSNI, TDSNI_Night, TIAdd. The mesh needs tangents.</item>
/// <item>UserTextures paths are relative to Documents\Trackmania (own files, so they must lie below Items\) or to
/// GameData (game textures). Item-relative paths aren't found. Own files only resolve locally for prefabs/moving items;
/// game textures work everywhere.</item>
/// <item>Roughness (layer 4) is accepted but had no visible effect in any test.</item>
/// <item>UvAnims only animate on TIAdd + layer "SelfIllum" on static items.</item>
/// </list>
/// Pure string handling, no file system access, so it also runs in WebAssembly.
/// </summary>
public static class CustomMaterialUtils
{
    // UserTextures layer numbers: NadeoImporter's texture enum (Diffuse 0, DiffuseO 1, BaseColor 2, BaseColorO 3,
    // Specular 4, Normal 5, Energy 6, TeamMask 7, SelfIllum 8, Damage 9, Dirt 10, Shield 11, RoughMetal 12).
    // In-game, TDSN takes its color only from layer 0; the opacity models TDOSN/TDOBSN need layer 1 (layer 0 = invisible).
    public const int LayerDiffuse = 0;
    public const int LayerDiffuseOpacity = 1;
    public const int LayerSpecular = 4;
    public const int LayerNormal = 5;
    public const int LayerEnergy = 6;
    public const int LayerSelfIllum = 8;

    public static int ColorLayer(string? model) => model is "TDOSN" or "TDOBSN" ? LayerDiffuseOpacity : LayerDiffuse;

    /// <summary>
    /// Path as the game expects it in UserTextures. GameTexture: as given (below GameData). Own file: the game reads it
    /// relative to Documents\Trackmania, so the (absolute) asset path is cut at its first "Items\" segment. A path without
    /// one is written as given.
    /// </summary>
    public static string ToUserTexturePath(string path, AssetType type)
    {
        var p = path.Replace('/', '\\');
        if (type == AssetType.GameTexture)
            return p.TrimStart('\\');
        if (p.StartsWith(@"Items\", StringComparison.OrdinalIgnoreCase))
            return p;
        int items = p.IndexOf(@"\Items\", StringComparison.OrdinalIgnoreCase);
        return items >= 0 ? p[(items + 1)..] : p;
    }

    /// <summary>
    /// UvAnim.Data as the game reads it (layout confirmed with NadeoImporter output and in-game):
    /// TransSubTexture(2) = FrameCount (32 bit) | FramesX (16 bit) | FramesY (16 bit);
    /// everything else = float SpeedU in the low 32 bits, float SpeedV in the high 32 bits.
    /// </summary>
    public static ulong EncodeUvAnimData(UvAnimConfig anim)
    {
        if (anim.Data.HasValue)
            return anim.Data.Value;
        if (anim.Type is "TransSubTexture" or "TransSubTexture2")
            return (uint)anim.FrameCount | ((ulong)(ushort)anim.FramesX << 32) | ((ulong)(ushort)anim.FramesY << 48);
        return (uint)BitConverter.SingleToInt32Bits(anim.SpeedU) | ((ulong)(uint)BitConverter.SingleToInt32Bits(anim.SpeedV) << 32);
    }

    /// <summary>
    /// Writes model, textures, constants, tiling and UV animations of a Custom material config into <paramref name="material"/>.
    /// Fails only for asset ids that aren't in <see cref="ItemConfig.Assets"/>; everything else is written as given.
    /// </summary>
    public static ToolResult<None> ApplyCustomMaterial(CPlugMaterialUserInst material, MaterialConfig config, ItemConfig itemConfig)
    {
        var failures = new List<string>();
        var textures = new List<CPlugMaterialUserInst.UserTexture>();
        void AddTexture(string? assetId, int layer, string slot)
        {
            if (string.IsNullOrEmpty(assetId))
                return;
            var asset = itemConfig.ResolveAsset(assetId);
            if (asset is null)
            {
                failures.Add($"{config.Name}.{slot}: unknown asset '{assetId}'");
                return;
            }
            textures.Add(new CPlugMaterialUserInst.UserTexture
            {
                Layer = layer,
                Texture = ToUserTexturePath(asset.Value.Path, asset.Value.Type),
            });
        }

        AddTexture(config.ColorImage, ColorLayer(config.Model), nameof(config.ColorImage));
        AddTexture(config.RoughnessImage, LayerSpecular, nameof(config.RoughnessImage));
        AddTexture(config.NormalImage, LayerNormal, nameof(config.NormalImage));
        AddTexture(config.EnergyImage, LayerEnergy, nameof(config.EnergyImage));
        AddTexture(config.SelfIllumImage, LayerSelfIllum, nameof(config.SelfIllumImage));
        if (failures.Count > 0)
            return ToolResult.Fail<None>(nameof(FbxGbxConverter), ErrorCodes.FbxGbxConverter.MissingAsset, string.Join("; ", failures));

        material.Name = config.Name;
        material.Model = config.Model ?? "";
        material.Link = "";
        material.BaseTexture = config.BaseTexture ?? "";
        // "Is based on game textures" in NadeoImporter; the game-written sample .Mat.Gbx with UserTextures has it set.
        material.IsUsingGameMaterial = true;
        material.UserTextures = textures.ToArray();
        material.Csts = [];
        material.CstValues = [];
        if (config.PhysicsId.HasValue)
            material.SurfacePhysicId = config.PhysicsId.Value;
        if (config.GameplayId.HasValue)
            material.SurfaceGameplayId = config.GameplayId.Value;
        if (config.TilingU.HasValue)
            material.TilingU = config.TilingU.Value;
        if (config.TilingV.HasValue)
            material.TilingV = config.TilingV.Value;
        if (config.SelfIllumScale.HasValue)
            AddRealConstant(material, "SelfIllumScale", config.SelfIllumScale.Value);

        material.UvAnims = (config.UvAnims ?? []).Select(a => new CPlugMaterialUserInst.UvAnim
        {
            Layer = a.Layer, Type = a.Type, Period = a.Period, Data = EncodeUvAnimData(a), InputVal = a.InputVal,
        }).ToArray();

        return ToolResult.Success(None.Value, nameof(FbxGbxConverter));
    }

    /// <summary>Appends a "Real" constant; the values are stored as raw float bits (like TargetColor).</summary>
    public static void AddRealConstant(CPlugMaterialUserInst material, string name, params float[] values)
    {
        material.Csts = [.. material.Csts ?? [], new CPlugMaterialUserInst.Cst { Name = name, Type = "Real", ValueCount = values.Length }];
        material.CstValues = [.. material.CstValues ?? [], .. values.Select(BitConverter.SingleToInt32Bits)];
    }
}
