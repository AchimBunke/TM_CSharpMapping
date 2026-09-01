
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Plug;
using System.Diagnostics.CodeAnalysis;

namespace TM_GenericMapping.Items;

public class ItemGameplayEffectConverter
{

    public interface IConversionRule
    {
        bool AppliesTo(CPlugMaterialUserInst mat);

        void Apply(CPlugMaterialUserInst mat);
    }
    public sealed class LinkToLinkRule : IConversionRule
    {
        public required string From { get; init; }
        public required string To { get; init; }

        public bool AppliesTo(CPlugMaterialUserInst mat)
            => mat.Link == From;
        

        public void Apply(CPlugMaterialUserInst mat)
            => mat.Link = To;
    }
    public sealed class SurfacePhysicsToSurfacePhysicsRule : IConversionRule
    {
        public required CPlugSurface.MaterialId From { get; init; }
        public required CPlugSurface.MaterialId To { get; init; }

        public bool AppliesTo(CPlugMaterialUserInst mat)
            => mat.SurfacePhysicId == From;


        public void Apply(CPlugMaterialUserInst mat)
            => mat.SurfacePhysicId = To;
    }
    public sealed class SurfaceGameplayToSurfaceGameplayRule : IConversionRule
    {
        public required CPlugMaterialUserInst.GameplayId From { get; init; }
        public required CPlugMaterialUserInst.GameplayId To { get; init; }

        public bool AppliesTo(CPlugMaterialUserInst mat)
            => mat.SurfaceGameplayId == From;


        public void Apply(CPlugMaterialUserInst mat)
            => mat.SurfaceGameplayId = To;
    }
    public sealed class MaterialNameToLinkRule : IConversionRule
    {
        public required string From { get; init; }
        public required string To { get; init; }

        public bool AppliesTo(CPlugMaterialUserInst mat)
            => mat.MaterialName == From;


        public void Apply(CPlugMaterialUserInst mat)
            => mat.Link = To;
    }
    public sealed class MaterialNameToSurfacePhysicsRule : IConversionRule
    {
        public required string From { get; init; }
        public required CPlugSurface.MaterialId To { get; init; }

        public bool AppliesTo(CPlugMaterialUserInst mat)
            => mat.MaterialName == From;


        public void Apply(CPlugMaterialUserInst mat)
            => mat.SurfacePhysicId = To;
    }
    public sealed class MaterialNameToSurfaceGameplayRule : IConversionRule
    {
        public required string From { get; init; }
        public required CPlugMaterialUserInst.GameplayId To { get; init; }

        public bool AppliesTo(CPlugMaterialUserInst mat)
            => mat.MaterialName == From;


        public void Apply(CPlugMaterialUserInst mat)
            => mat.SurfaceGameplayId = To;
    }
    public sealed class MaterialNameDefinedRule : IConversionRule
    {
        public required string From { get; init; }
        public required IConversionRule InnerRule { get; init; }

        public bool AppliesTo(CPlugMaterialUserInst mat)
            => mat.MaterialName == From && InnerRule.AppliesTo(mat);


        public void Apply(CPlugMaterialUserInst mat)
            => InnerRule.Apply(mat);
    }
    public static class ConversionRules
    {
        public static IEnumerable<IConversionRule> CreateLinkMappings(Dictionary<string, string> linkMappings, string requiredMaterialName = "")
        {
            foreach (var kvp in linkMappings)
            {
                if(string.IsNullOrEmpty(requiredMaterialName))
                    yield return new LinkToLinkRule { From = kvp.Key, To = kvp.Value };
                else
                    yield return new MaterialNameDefinedRule { From = requiredMaterialName, InnerRule = new LinkToLinkRule { From = kvp.Key, To = kvp.Value } };
            }
        }
        public static IEnumerable<IConversionRule> CreateSurfacePhysicsMappings(Dictionary<CPlugSurface.MaterialId, CPlugSurface.MaterialId> surfacePhysicsMappings, string requiredMaterialName = "")
        {
            foreach (var kvp in surfacePhysicsMappings)
            {
                if(string.IsNullOrEmpty(requiredMaterialName))
                    yield return new SurfacePhysicsToSurfacePhysicsRule { From = kvp.Key, To = kvp.Value };
                else
                    yield return new MaterialNameDefinedRule { From = requiredMaterialName, InnerRule = new SurfacePhysicsToSurfacePhysicsRule { From = kvp.Key, To = kvp.Value } };
            }
        }
        public static IEnumerable<IConversionRule> CreateSurfaceGameplayIdMappings(Dictionary<CPlugMaterialUserInst.GameplayId, CPlugMaterialUserInst.GameplayId> surfaceGameplayIdMappings, string requiredMaterialName = "")
        {
            foreach (var kvp in surfaceGameplayIdMappings)
            {
                if(string.IsNullOrEmpty(requiredMaterialName))
                    yield return new SurfaceGameplayToSurfaceGameplayRule { From = kvp.Key, To = kvp.Value };
                else
                    yield return new MaterialNameDefinedRule { From = requiredMaterialName, InnerRule = new SurfaceGameplayToSurfaceGameplayRule { From = kvp.Key, To = kvp.Value } };
            }
        }
        public static IEnumerable<IConversionRule> CreateMaterialNameToLinkMappings(Dictionary<string, string> materialNameToLinkMappings)
        {
            foreach (var kvp in materialNameToLinkMappings)
            {
                yield return new MaterialNameToLinkRule { From = kvp.Key, To = kvp.Value };
            }
        }
        public static IEnumerable<IConversionRule> CreateMaterialNameToSurfacePhysicsMappings(Dictionary<string, CPlugSurface.MaterialId> materialNameToSurfacePhysicsMappings)
        {
            foreach (var kvp in materialNameToSurfacePhysicsMappings)
            {
                yield return new MaterialNameToSurfacePhysicsRule { From = kvp.Key, To = kvp.Value };
            }
        }
        public static IEnumerable<IConversionRule> CreateMaterialNameToSurfaceGameplayIdMappings(Dictionary<string, CPlugMaterialUserInst.GameplayId> materialNameToSurfaceGameplayIdMappings)
        {
            foreach (var kvp in materialNameToSurfaceGameplayIdMappings)
            {
                yield return new MaterialNameToSurfaceGameplayRule { From = kvp.Key, To = kvp.Value };
            }
        }

    }


    public record class ItemGameplayEffectConverterSettings
    {
        public List<IConversionRule> ConversionRules = [];
    }

    private ItemGameplayEffectConverterSettings _settings;
    public ItemGameplayEffectConverter(ItemGameplayEffectConverterSettings settings)
    {
        _settings = settings;
    }

    public void SwitchGameplayEffectOnItem(CGameItemModel item)
    {
        if (ItemExtensions.TryGetCrystal(item, out var crystal))
            SwitchGameplayEffectOnCPlugCrystal(crystal);
        else if (ItemExtensions.TryGetDynaObjectModel(item, out var dyna))
            SwitchGameplayEffectOnDynaObjectModel(dyna);
        else if(ItemExtensions.TryGetSolid2Model(item, out var solidModel))
            SwitchGameplayEffectOnSolid2Model(solidModel);
    }
    public void SwitchGameplayEffectOnSolid2Model(CPlugSolid2Model solid)
    {
        
        foreach (var mat in solid.CustomMaterials ?? [])
        {
            var matInst = mat.MaterialUserInst!;
            ApplyConversionRules(matInst);
        }

    }
    public void SwitchGameplayEffectOnDynaObjectModel(CPlugDynaObjectModel dyna)
    {
        if (dyna.Mesh != null)
            SwitchGameplayEffectOnSolid2Model(dyna.Mesh);
    }
    public void SwitchGameplayEffectOnCPlugCrystal(CPlugCrystal crystal)
    {
        foreach(var mat in crystal.Materials)
        {
            var matInst = mat.MaterialUserInst!;
            ApplyConversionRules(matInst);
        }
    }
    void ApplyConversionRules(CPlugMaterialUserInst mat)
    {
        foreach(var rule in _settings.ConversionRules)
        {
            if (rule.AppliesTo(mat))
                rule.Apply(mat);
        }
    }
}
