using GBX.NET.Engines.GameData;
using TM_GenericMapping.Messaging;

namespace TM_GenericMapping.Items;

public record ItemVariantInput(CGameItemModel ItemModel, Dictionary<string, string> Tags, bool HiddenInManualCycle);

public class VariantItemBuilder
{
    public ToolResult<CGameItemModel> CreateVariantItem(ReadOnlySpan<ItemVariantInput> variantInputs)
    {
        if(variantInputs.Length == 0)
            return ToolResult.Fail(nameof(VariantItemBuilder), ErrorCodes.VariantItemBuilder.MissingVariantInputs);

        var meshExtractor = new MeshExtractor();
        var firstVariantItem = variantInputs[0];

        var firstExtractionResult = meshExtractor.ExtractMesh(firstVariantItem.ItemModel);
        if(firstExtractionResult.IsFailure)
            return ToolResult.Fail(firstExtractionResult);
        var normalizedItem = firstExtractionResult.Value;

        List<VariantGroup> variantGroups =
            [
                CreateVariantGroup(normalizedItem, firstVariantItem)
            ];
        List<MeshGroup> meshGroups = normalizedItem.Groups.ToList();
        List<NormalizedMesh> normalizedMeshes = normalizedItem.Meshes.ToList();
        List<NormalizedLight> normalizedLights = normalizedItem.Lights.ToList();
        foreach (var g in normalizedItem.Groups)
        {
            g.VariantIndex = 0;
        }

        for (int i = 1; i < variantInputs.Length; i++)
        {
            int groupCount = meshGroups.Count;
            int meshCount = normalizedMeshes.Count;
            int lightCount = normalizedLights.Count;
            
            var extractionResult = meshExtractor.ExtractMesh(variantInputs[i].ItemModel);
            if(extractionResult.IsFailure)
                return ToolResult.Fail(extractionResult);
            var normItem = extractionResult.Value;
            foreach(var g in normItem.Groups)
            {
                g.VariantIndex = i;
            }
            foreach (var m in normItem.Meshes)
            {
                m.GroupIndex += groupCount;
            }
            foreach (var l in normItem.Lights)
            {
                l.GroupIndex += groupCount;
            }
            variantGroups.Add(CreateVariantGroup(normItem, variantInputs[i]));
            meshGroups.AddRange(normItem.Groups);
            normalizedMeshes.AddRange(normItem.Meshes);
            normalizedLights.AddRange(normItem.Lights);
        }
        normalizedItem.VariantGroups = variantGroups.ToArray();
        normalizedItem.Groups = meshGroups.ToArray();
        normalizedItem.Meshes = normalizedMeshes.ToArray();
        normalizedItem.Lights = normalizedLights.ToArray();

        var meshBuilder = new MeshBuilder();
        var settings = MeshBuilder.BuildSettings.DefaultFromMesh(normalizedItem);

        var buildResult = meshBuilder.BuildItem(normalizedItem, settings);
        if(buildResult.IsFailure)
            return ToolResult.Fail(buildResult);

        return ToolResult.Success(buildResult.Value, nameof(VariantItemBuilder));

    }



    VariantGroup CreateVariantGroup(NormalizedItem normalizedItem, ItemVariantInput input)
    {
        var group = new VariantGroup();
        group.Tags = input.Tags;
        group.HiddenInManualCycle = input.HiddenInManualCycle;
        return group;
    }
}
