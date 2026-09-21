using GBX.NET.Engines.Plug;
using System.Drawing;
using TM_GenericMapping.Common;
using TM_GenericMapping.Items.FbxGbxConversion.Importing;
using TM_GenericMapping.Items.FbxGbxConversion.Serialization;
using TM_GenericMapping.Messaging;
using TM_GenericMapping.Templating;

namespace TM_GenericMapping.Items.FbxGbxConversion;

/// <summary>
/// <see cref="ImportedMaterial"/> DTO instead of Assimp.Material, so the material conversion
/// path no longer depends on the import library.
/// </summary>
internal class FbxMaterialConverter
{
    private readonly DMaterialLibrary _materialLibrary;
    private readonly CPlugMaterialUserInst _materialTemplate;

    public FbxMaterialConverter(DMaterialLibrary materialLibrary)
    {
        _materialLibrary = materialLibrary;
        var solid2ModelTemplate = GbxTemplateLibrary.CreateCPlugSolid2ModelTemplate().Value;
        _materialTemplate = solid2ModelTemplate.CustomMaterials![0].MaterialUserInst!;
    }

    public ToolResult<List<MaterialDef>> ExtractMaterials(ImportedScene scene, FbxGbxConversionInput config)
    {
        List<MaterialDef> customMaterial = [];
        foreach (var mat in scene.Materials)
        {
            var customMatResult = ConvertMaterial(mat, config);
            if (customMatResult.IsFailure && !config.ItemConfig.ConversionOptions.HasFlag(ItemConversionOptions.IgnoreMeshesWithInvalidMaterials))
                return ToolResult.Fail(customMatResult);
            customMaterial.Add(customMatResult.Value);
        }
        return ToolResult.Success(customMaterial, nameof(FbxGbxConverter));
    }

    ToolResult<MaterialDef> ConvertMaterial(ImportedMaterial mat, FbxGbxConversionInput config)
    {
        var customMat = CreateEmptyMaterialInstance();
        string matName = mat.Name;

        if (!TryFindMaterialConfig(matName, config, out var materialConfig))
            return ToolResult.Fail(nameof(FbxGbxConverter), ErrorCodes.FbxGbxConverter.MissingMaterialConfig, matName);
        bool usingGameMaterial = _materialLibrary.Materials.TryGetValue(materialConfig.Link, out var dMaterial);

        customMat.MaterialName = materialConfig.Name;
        customMat.IsNatural = false;
        customMat.IsUsingGameMaterial = usingGameMaterial;
        if (usingGameMaterial)
        {
            customMat.Link = dMaterial!.LinkFull;
            customMat.SurfaceGameplayId = dMaterial.GameplayId;
            customMat.SurfacePhysicId = dMaterial.SurfaceId;
        }
        else
        {
            customMat.Link = materialConfig.Link;
        }
        if (materialConfig.PhysicsId.HasValue)
            customMat.SurfacePhysicId = materialConfig.PhysicsId.Value;
        if (materialConfig.GameplayId.HasValue)
            customMat.SurfaceGameplayId = materialConfig.GameplayId.Value;
        if(!string.IsNullOrEmpty(materialConfig.BaseTexture))
            customMat.BaseTexture = materialConfig.BaseTexture;
        if(!string.IsNullOrEmpty(materialConfig.Model))
            customMat.Model = materialConfig.Model;
        if (materialConfig.Color.HasValue)
        {
            var color = materialConfig.Color.Value;

            int r = BitConverter.SingleToInt32Bits(SrgbToLinear(color.R / 255.0f));
            int g = BitConverter.SingleToInt32Bits(SrgbToLinear(color.G / 255.0f));
            int b = BitConverter.SingleToInt32Bits(SrgbToLinear(color.B / 255.0f));

            customMat.Color = [r, g, b];
            customMat.Csts = [
                new CPlugMaterialUserInst.Cst()
                {
                    U01 = "TargetColor",
                    U02 = "Real",
                    U03 = 3,
                }
                ];
        }

        var materialDef = new MaterialDef(customMat, dMaterial);

        return ToolResult.Success(materialDef, nameof(FbxGbxConverter));
    }

    static float SrgbToLinear(float c)
    {
        c = Math.Clamp(c, 0f, 1f);
        return c <= 0.04045f
            ? c / 12.92f
            : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
    }

    static float LinearToSrgb(float value)
    {
        return value <= 0.0031308f
            ? value * 12.92f
            : 1.055f * MathF.Pow(value, 1f / 2.4f) - 0.055f;
    }

    CPlugMaterialUserInst CreateEmptyMaterialInstance()
    {
        var mat = ObjectCloner.DeepCloneObject(_materialTemplate)!;
        mat.Link = "";
        mat.IsUsingGameMaterial = true;
        mat.SurfaceGameplayId = CPlugSurface.GameplayId.None;
        mat.SurfacePhysicId = CPlugSurface.MaterialId.NotCollidable;
        return mat;
    }

    bool TryFindMaterialConfig(string matName, FbxGbxConversionInput config, out MaterialConfig materialConfig)
    {
        materialConfig = config.ItemConfig.MaterialConfiguration.FirstOrDefault(i => i!.Name == matName, null)!;
        return materialConfig is not null;
    }

    //------------------------------
    // reconstruction
    //------------------------------
    public Dictionary<CPlugMaterialUserInst, int> RebuildMaterials(List<CPlugMaterialUserInst> materials, ItemConfig itemConfig, out List<ImportedMaterial> importedMaterials)
    {
        Dictionary<CPlugMaterialUserInst, int> materialIndices = new Dictionary<CPlugMaterialUserInst, int>();
        itemConfig.MaterialConfiguration = [];
        importedMaterials = [];
        foreach (var mat in materials)
        {
            var result = RebuildMaterial(mat);

            itemConfig.MaterialConfiguration.Add(result.MaterialConfig);
            materialIndices.Add(mat, importedMaterials.Count);
            importedMaterials.Add(result.Material);
        }
        return materialIndices;
    }

    (ImportedMaterial Material, MaterialConfig MaterialConfig) RebuildMaterial(CPlugMaterialUserInst materialUserInst)
    {
        var mat = new ImportedMaterial
        {
            Name = string.IsNullOrWhiteSpace(materialUserInst.MaterialName) ? "UnnamedMaterial" : materialUserInst.MaterialName,
        };

        var matConfig = new MaterialConfig()
        {
            GameplayId = materialUserInst.SurfaceGameplayId,
            PhysicsId = materialUserInst.SurfacePhysicId,
            Name = mat.Name,
            //not reliable!
            Link = _materialLibrary.Materials.FirstOrDefault(m => m.Value.LinkFull == materialUserInst.Link, new KeyValuePair<string, DMaterial>("", null!)).Key,
            BaseTexture = materialUserInst.BaseTexture,
            Model = materialUserInst.Model,
        };
        if (materialUserInst.Color?.Length > 0)
        {
            float r = LinearToSrgb(BitConverter.Int32BitsToSingle(materialUserInst.Color[0])) * 255f;
            float g = LinearToSrgb(BitConverter.Int32BitsToSingle(materialUserInst.Color[1])) * 255f;
            float b = LinearToSrgb(BitConverter.Int32BitsToSingle(materialUserInst.Color[2])) * 255f;
            matConfig.Color = Color.FromArgb((int)r, (int)g, (int)b);
        }

        return (mat, matConfig);
    }
}
