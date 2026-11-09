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

    public ToolResult<List<MaterialDef>> ExtractMaterials(ImportedScene scene, ConfigIndex configIndex)
    {
        List<MaterialDef> customMaterial = [];
        foreach (var mat in scene.Materials)
        {
            var customMatResult = ConvertMaterial(mat, configIndex);
            if (customMatResult.IsFailure)
            {
                if(!configIndex.ItemConfig.ConversionOptions.HasFlag(ItemConversionOptions.IgnoreMeshesWithInvalidMaterials) && mat.Name != "DefaultMaterial")
                {
                    return ToolResult.Fail(customMatResult);
                }
            }
            customMaterial.Add(customMatResult.Value);
        }
        return ToolResult.Success(customMaterial, nameof(FbxGbxConverter));
    }

    ToolResult<MaterialDef> ConvertMaterial(ImportedMaterial mat, ConfigIndex configIndex)
    {
        var customMat = CreateEmptyMaterialInstance();
        string matName = mat.Name;

        if (!TryFindMaterialConfig(matName, configIndex, out var materialConfig))
            return ToolResult.Fail(nameof(FbxGbxConverter), ErrorCodes.FbxGbxConverter.MissingMaterialConfig, matName);
        bool usingGameMaterial = _materialLibrary.Materials.TryGetValue(materialConfig.Link, out var dMaterial);

        customMat.Name = materialConfig.Name;
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

            customMat.CstValues = [r, g, b];
            customMat.Csts = [
                new CPlugMaterialUserInst.Cst()
                {
                    Name = "TargetColor",
                    Type = "Real",
                    ValueCount = 3,
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

    bool TryFindMaterialConfig(string matName, ConfigIndex configIndex, out MaterialConfig materialConfig)
    {
        return configIndex.MaterialConfigsByName.TryGetValue(matName, out materialConfig!);
    }

    //------------------------------
    // reconstruction
    //------------------------------
    public Dictionary<CPlugMaterialUserInst, int> RebuildMaterials(List<CPlugMaterialUserInst> materials, ItemConfig itemConfig, out List<ImportedMaterial> importedMaterials)
    {
        Dictionary<CPlugMaterialUserInst, int> materialIndices = new Dictionary<CPlugMaterialUserInst, int>();
        itemConfig.Materials = [];
        importedMaterials = [];
        foreach (var mat in materials)
        {
            var result = RebuildMaterial(mat);

            itemConfig.Materials.Add(result.MaterialConfig);
            materialIndices.Add(mat, importedMaterials.Count);
            importedMaterials.Add(result.Material);
        }
        return materialIndices;
    }

    (ImportedMaterial Material, MaterialConfig MaterialConfig) RebuildMaterial(CPlugMaterialUserInst materialUserInst)
    {
        var mat = new ImportedMaterial
        {
            Name = string.IsNullOrWhiteSpace(materialUserInst.Name) ? "UnnamedMaterial" : materialUserInst.Name,
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
        if (materialUserInst.CstValues?.Length > 0)
        {
            float r = LinearToSrgb(BitConverter.Int32BitsToSingle(materialUserInst.CstValues[0])) * 255f;
            float g = LinearToSrgb(BitConverter.Int32BitsToSingle(materialUserInst.CstValues[1])) * 255f;
            float b = LinearToSrgb(BitConverter.Int32BitsToSingle(materialUserInst.CstValues[2])) * 255f;
            matConfig.Color = Color.FromArgb((int)r, (int)g, (int)b);
        }

        return (mat, matConfig);
    }
}
