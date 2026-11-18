using GBX.NET;
using GBX.NET.Components;
using GBX.NET.Engines.Meta;
using TM_GenericMapping.Common;
using TM_GenericMapping.Items.FbxGbxConversion.Serialization;
using TM_GenericMapping.Items.MeshCompilation;
using TM_GenericMapping.Messaging;
using static GBX.NET.Engines.Plug.CPlugSurface;

namespace TM_GenericMapping.Items.FbxGbxConversion;

internal static class FbxVegetTreeConverter
{
    /// <summary>True if the material config uses texture asset fields (tree material) instead of a Link.</summary>
    public static bool HasTextureAssets(MaterialConfig config)
        => !string.IsNullOrEmpty(config.ColorImage) || !string.IsNullOrEmpty(config.NormalImage) || !string.IsNullOrEmpty(config.RoughnessImage);

    /// <summary>
    /// Creates the template tree model that is stored in <see cref="NormalizedModel.VegetTreeModel"/> and later cloned by the compiler.
    /// Boilerplate: map the tree parameters of <paramref name="config"/> onto the GBX model here.
    /// </summary>
    public static ToolResult<CPlugVegetTreeModel> CreateModelTemplate(VegetTreeConfig config, ItemConfig itemConfig)
    {
        var template = new CPlugVegetTreeModel
        {
            AngleMax_RotXZ_Deg = config.AngleMaxRotXZDeg,
            EnableRandomRotationY = config.EnableRandomRotationY,
            Force_No_Collision = config.ForceNoCollision,
            Impostor_AllPlanesVisible = config.ImpostorAllPlanesVisible,
            LodDistAtFov90 = config.LodDistAtFov90,
            ScaleVar01 = config.PropagationMaxRandomScaleReductionPerInstance,
            PropagationEmissionSpawnRadius = config.PropagationEmissionSpawnRadius,
            PropagationLeafPerSecondCount = config.PropagationLeafPerSecondCount,
            PropagationLeafSize = config.PropagationLeafSize,
            PlaneComputeMode = config.PlaneComputeMode,
            PropagationAtlasGrid = config.PropagationAtlasGrid,
            PropagationConeHalfAngleDeg = config.PropagationConeHalfAngleDeg,
            PropagationEmissionPositions = config.PropagationEmissionPositions,
            PropagationEnable = config.EnablePropagation,
            PropagationGroundGenPointCount = config.PropagationGroundGenPointCount,
            ReceivesPSSM = config.ReceivesPSSM,
        };
        //TODO: Unknown once they become known.

        var failures = new List<string>();
        void OnFail(string id, string slot)
        {
            failures.Add($"{config.Name}.{slot}: unknown asset '{id}'");
        }
        if (!string.IsNullOrWhiteSpace(config.PropagationBaseColorBitmap))
        {
            var normAsset = Resolve(config.PropagationBaseColorBitmap, nameof(config.PropagationBaseColorBitmap), itemConfig, OnFail);
            if (normAsset is not null)
                template.References.PropagationBaseColor.Descriptor = NodeRefUtils.CreateFile(normAsset.Path);
        }
        if (!string.IsNullOrWhiteSpace(config.PropagationNormalBitmap))
        {
            var normAsset = Resolve(config.PropagationNormalBitmap, nameof(config.PropagationNormalBitmap), itemConfig, OnFail);
            if (normAsset is not null)
                template.References.PropagationNormal.Descriptor = NodeRefUtils.CreateFile(normAsset.Path);
        }
        if(failures.Count > 0)
            return ToolResult.Fail<CPlugVegetTreeModel>(nameof(FbxGbxConverter), ErrorCodes.FbxGbxConverter.MissingAsset, string.Join("; ", failures));

        return ToolResult.Success(template, nameof(FbxGbxConverter));
    }

    static NormalizedAsset? Resolve(string? id, string slot, ItemConfig config, Action<string,string> onFail)
    {
        if (string.IsNullOrEmpty(id))
            return null;
        var resolved = config.ResolveAsset(id);
        if (resolved is null)
        {
            onFail(id, slot);
            return null;
        }
        return new NormalizedAsset(resolved.Value.Path, resolved.Value.Type);
    }

    /// <summary>Resolves the asset ids of a material config via the asset registry.</summary>
    public static ToolResult<NormalizedTextureAssets> ResolveTextureAssets(ItemConfig config, MaterialConfig materialConfig)
    {
        var failures = new List<string>();

        void OnFail(string id, string slot)
        {
            failures.Add($"{materialConfig.Name}.{slot}: unknown asset '{id}'");
        }

        var assets = new NormalizedTextureAssets(
            Resolve(materialConfig.ColorImage, nameof(materialConfig.ColorImage), config, OnFail),
            Resolve(materialConfig.NormalImage, nameof(materialConfig.NormalImage), config, OnFail),
            Resolve(materialConfig.RoughnessImage, nameof(materialConfig.RoughnessImage), config, OnFail),

            Resolve(materialConfig.DiffuseBitmap, nameof(materialConfig.DiffuseBitmap), config, OnFail),
            Resolve(materialConfig.NormalBitmap, nameof(materialConfig.NormalBitmap), config, OnFail),
            Resolve(materialConfig.RoughnessBitmap, nameof(materialConfig.RoughnessBitmap), config, OnFail),
            
            materialConfig.PhysicsId,
            materialConfig.GameplayId);

        if (failures.Count > 0)
            return ToolResult.Fail<NormalizedTextureAssets>(nameof(FbxGbxConverter), ErrorCodes.FbxGbxConverter.MissingAsset, string.Join("; ", failures));
        return ToolResult.Success(assets, nameof(FbxGbxConverter));
    }

    /// <summary>
    /// Returns the <see cref="NormalizedMaterial"/> pool key (created if necessary) of a tree material.
    /// The GBX TextureSet itself is left to the GBX creation code (<see cref="NormalizedMaterial.TextureAssets"/>).
    /// </summary>
    public static ToolResult<int> GetOrAddTextureSetMaterial(NormalizedItem item, MaterialDef material)
    {
        if (material.TextureAssets is null)
            return ToolResult.Fail<int>(nameof(FbxGbxConverter), ErrorCodes.FbxGbxConverter.InvalidVegetTreeConfig,
                $"Material '{material.MaterialInstance.Name}' of a VegetTree mesh has no ColorImage/NormalImage/RoughnessImage.");

        var name = material.MaterialInstance.Name ?? string.Empty;
        foreach (var (key, existing) in item.MaterialPool)
        {
            if (existing.Type == MaterialType.TextureSet && existing.Name == name && existing.TextureAssets == material.TextureAssets)
                return ToolResult.Success(key, nameof(FbxGbxConverter));
        }

        int newKey = item.MaterialPool.Count == 0 ? 0 : item.MaterialPool.Keys.Max() + 1;
        item.MaterialPool[newKey] = new NormalizedMaterial
        {
            Type = MaterialType.TextureSet,
            TextureAssets = material.TextureAssets,
            Name = name,
        };
        return ToolResult.Success(newKey, nameof(FbxGbxConverter));
    }

    /// <summary>Validates tree references of all mesh configs and the tree LOD indices.</summary>
    public static ToolResult<None> ValidateNodes(IEnumerable<NodeDef> nodes, ConfigIndex configIndex)
    {
        foreach (var node in nodes)
        {
            var treeName = node.NodeConfig.VegetTree;
            if (string.IsNullOrEmpty(treeName))
                continue;

            if (!configIndex.VegetTreeConfigsByName.TryGetValue(treeName, out var tree))
                return ToolResult.Fail<None>(nameof(FbxGbxConverter), ErrorCodes.FbxGbxConverter.MissingVegetTreeConfig, $"{node.NodeConfig.Name}: {treeName}");

            var flags = node.NodeConfig.MeshFlags;
            if (flags.HasFlag(MeshFlags.Moving) || flags.HasFlag(MeshFlags.TriggerEffect) || flags.HasFlag(MeshFlags.TriggerWaypoint))
                return ToolResult.Fail<None>(nameof(FbxGbxConverter), ErrorCodes.FbxGbxConverter.InvalidVegetTreeConfig,
                    $"Mesh '{node.NodeConfig.Name}' is part of tree '{treeName}' and cannot be Moving/TriggerEffect/TriggerWaypoint.");

            int maxLod = configIndex.ItemConfig.LodParameters?.MaxLodDistances.Count ?? 0;
            if (node.NodeConfig.Lods.Any(l => l < 0 || l > maxLod))
                return ToolResult.Fail<None>(nameof(FbxGbxConverter), ErrorCodes.FbxGbxConverter.MissingLodDistanceConfig,
                    $"Mesh '{node.NodeConfig.Name}' references LOD index outside 0..{maxLod} of tree '{treeName}'.");
        }
        return ToolResult.Success(None.Value, nameof(FbxGbxConverter));
    }
}
