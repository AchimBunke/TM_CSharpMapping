using Assimp;
using GBX.NET.Engines.Plug;
using TM_GenericMapping.Common;
using TM_GenericMapping.Items.FbxGbxConversion.Serialization;
using TM_GenericMapping.Messaging;
using TM_GenericMapping.Templating;

namespace TM_GenericMapping.Items.FbxGbxConversion;

internal record MaterialDef(CPlugMaterialUserInst MaterialInstance, DMaterial? DMaterial);

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

    public ToolResult<List<MaterialDef>> ExtractMaterials(Scene scene, FbxGbxConversionInput config)
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

    ToolResult<MaterialDef> ConvertMaterial(Assimp.Material mat, FbxGbxConversionInput config)
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
    CPlugMaterialUserInst CreateEmptyMaterialInstance()
    {
        var mat = ObjectCloner.DeepCloneObject(_materialTemplate)!;
        mat.Link = "";
        mat.IsUsingGameMaterial = true;
        mat.SurfaceGameplayId = CPlugMaterialUserInst.GameplayId.None;
        mat.SurfacePhysicId = CPlugSurface.MaterialId.NotCollidable;
        return mat;
    }

    bool TryFindMaterialConfig(string matName, FbxGbxConversionInput config, out MaterialConfig materialConfig)
    {
        materialConfig = config.ItemConfig.MaterialConfiguration.FirstOrDefault(i => i!.Name == matName, null)!;
        return materialConfig is not null;
    }


}
