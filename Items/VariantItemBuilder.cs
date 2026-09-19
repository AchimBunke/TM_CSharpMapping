using GBX.NET.Engines.GameData;
using TM_GenericMapping.Items.MeshCompilation;
using TM_GenericMapping.Messaging;

namespace TM_GenericMapping.Items;

public record ItemVariantInput(CGameItemModel ItemModel, Dictionary<string, string> Tags, bool HiddenInManualCycle);

public class VariantItemBuilder
{
    public ToolResult<CGameItemModel> CreateVariantItem(ReadOnlySpan<ItemVariantInput> variantInputs)
    {
        if(variantInputs.Length == 0)
            return ToolResult.Fail(nameof(VariantItemBuilder), ErrorCodes.VariantItemBuilder.MissingVariantInputs);

        var meshParser = new ItemParser();
        var firstVariantItem = variantInputs[0];

        var firstExtractionResult = meshParser.Parse(firstVariantItem.ItemModel);
        if(firstExtractionResult.IsFailure)
            return ToolResult.Fail(firstExtractionResult);
        var normalizedItem = firstExtractionResult.Value;

        var firstModel = normalizedItem.Model;

        var normVariantItem = new NormalizedItem
        {
            PlacementParam = normalizedItem.PlacementParam,
            Description = normalizedItem.Description,
            Name = normalizedItem.Name,
            Icon = normalizedItem.Icon,
            IconWebP = normalizedItem.IconWebP,
            WaypointType = normalizedItem.WaypointType,
        };
       
        normVariantItem.Model = new NormalizedModel
        {
            Type = ModelType.Variant_List,
            Variants = [],
        };
        normVariantItem.ModelPool.Add(0, normVariantItem.Model);


        for (int i = 0; i < variantInputs.Length; i++)
        {
            int meshCount = normVariantItem.MeshPool.Count;
            int modelCount = normVariantItem.ModelPool.Count;
            int shapeCount = normVariantItem.ShapePool.Count;
            int lightCount = normVariantItem.LightPool.Count;

            NormalizedItem normItem;
            if (i == 0)
                normItem = normalizedItem;
            else
            {
                var extractionResult = meshParser.Parse(variantInputs[i].ItemModel);
                if (extractionResult.IsFailure)
                    return ToolResult.Fail(extractionResult);
                normItem = extractionResult.Value;
            }

            Dictionary<int, int> meshKeyMapping = [];
            Dictionary<int, int> shapeKeyMapping = [];
            Dictionary<int, int> lightKeyMapping = [];
            Dictionary<int, int> modelKeyMapping = [];


            foreach (var kv in normItem.MeshPool)
            {
                var targetKey = normVariantItem.MeshPool.Count;
                meshKeyMapping.Add(kv.Key, targetKey);
                normVariantItem.MeshPool.Add(targetKey, kv.Value);
            }
            foreach (var kv in normItem.ShapePool)
            {
                var targetKey = normVariantItem.ShapePool.Count;
                shapeKeyMapping.Add(kv.Key, targetKey);
                normVariantItem.ShapePool.Add(targetKey, kv.Value);
            }
            foreach (var kv in normItem.LightPool)
            {
                var targetKey = normVariantItem.LightPool.Count;
                lightKeyMapping.Add(kv.Key, targetKey);
                normVariantItem.LightPool.Add(targetKey, kv.Value);
            }

            foreach (var kv in normItem.ModelPool)
            {
                var targetKey = normVariantItem.ModelPool.Count;
                modelKeyMapping.Add(kv.Key, targetKey);
                normVariantItem.ModelPool.Add(targetKey, kv.Value);

                foreach (var meshRef in kv.Value.Meshes)
                    meshRef.MeshKey = meshKeyMapping[meshRef.MeshKey];
                foreach (var shapeRef in kv.Value.Shapes)
                    shapeRef.ShapeKey = shapeKeyMapping[shapeRef.ShapeKey];
                foreach (var lightRef in kv.Value.Lights)
                    lightRef.LightKey = lightKeyMapping[lightRef.LightKey];
              
            }
           
            foreach (var kv in normItem.ModelPool) // second round every mdoel added
            {
                foreach (var childRef in kv.Value.Children)
                    childRef.ModelKey = modelKeyMapping[childRef.ModelKey];
            }

            if (!modelKeyMapping.TryGetValue(normItem.ModelPool.First().Key, out int rootModelKey))
                rootModelKey = normVariantItem.ModelPool.Count;
            
            var variantModel = CreateVariantModel(normItem, variantInputs[i]);
            variantModel.ModelKey = rootModelKey;
            normVariantItem.Model.Variants.Add(variantModel);

        }

        var meshBuilder = new ItemCompiler();
        var settings = BuildSettings.DefaultFromItem(normalizedItem);
        var compileOptions = new CompileOptions()
        {
            Target = ItemModel.Prefab,
            Optimization = ItemCompilerOptimization.None
        };
        var buildResult = meshBuilder.Compile(normVariantItem, settings, compileOptions);
        if(buildResult.IsFailure)
            return ToolResult.Fail(buildResult);

        return ToolResult.Success(buildResult.Value, nameof(VariantItemBuilder));

    }



    NormalizedVariant CreateVariantModel(NormalizedItem normalizedItem, ItemVariantInput input)
    {
        var group = new NormalizedVariant();
        group.Tags = input.Tags;
        group.HiddenInManualCycle = input.HiddenInManualCycle;
        return group;
    }
}
