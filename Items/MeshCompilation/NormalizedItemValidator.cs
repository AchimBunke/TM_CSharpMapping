using TM_GenericMapping.Messaging;

namespace TM_GenericMapping.Items.MeshCompilation;

public static class NormalizedItemValidator
{
    public static ToolResult<None> Validate(NormalizedItemV3 normalizedItem)
    {
        if(normalizedItem.Model == null)
            return ToolResult.Fail(nameof(NormalizedItemValidator), ErrorCodes.NormalizedItemValidator.ValidationError, "Model is null");

        foreach(var mesh in normalizedItem.MeshPool.Values)
        {
            var result = ValidateMesh(mesh);
            if(result.IsFailure)
                return result;
        }
        foreach (var shape in normalizedItem.ShapePool.Values)
        {
            var result = ValidateShape(shape);
            if (result.IsFailure)
                return result;
        }
        foreach (var light in normalizedItem.LightPool.Values)
        {
            var result = ValidateLight(light);
            if (result.IsFailure)
                return result;
        }

        var modelResult = ValidateModel(normalizedItem.Model, normalizedItem, []);
        if (modelResult.IsFailure)
            return modelResult;

        return ToolResult.Success(nameof(NormalizedItemValidator));
    }
    
    static ToolResult<None> ValidateMesh(NormalizedMeshV3 mesh)
    {
        if(mesh.Positions.Length == 0 && mesh.Indices.Length > 0)
            return ToolResult.Fail(nameof(NormalizedItemValidator), ErrorCodes.NormalizedItemValidator.ValidationError, "Mesh has indices but no positions");
        if (mesh.Indices.Length == 0 && mesh.Positions.Length > 0)
            return ToolResult.Fail(nameof(NormalizedItemValidator), ErrorCodes.NormalizedItemValidator.ValidationError, "Mesh has positions but no indices");

        return ToolResult.Success(nameof(NormalizedItemValidator));
    }
    static ToolResult<None> ValidateShape(NormalizedShapeV3 shape)
    {
        if (shape.Positions.Length == 0 && shape.Indices.Length > 0)
            return ToolResult.Fail(nameof(NormalizedItemValidator), ErrorCodes.NormalizedItemValidator.ValidationError, "Shape has indices but no positions");
        if (shape.Indices.Length == 0 && shape.Positions.Length > 0)
            return ToolResult.Fail(nameof(NormalizedItemValidator), ErrorCodes.NormalizedItemValidator.ValidationError, "Shape has positions but no indices");

        if(shape.SurfaceMaterialIds.Length != shape.Indices.Length / 3)
            return ToolResult.Fail(nameof(NormalizedItemValidator), ErrorCodes.NormalizedItemValidator.ValidationError, "Shape has mismatched surface material IDs");

        return ToolResult.Success(nameof(NormalizedItemValidator));
    }

    static ToolResult<None> ValidateLight(NormalizedLightV3 light)
    {
        return ToolResult.Success(nameof(NormalizedItemValidator));
    }

   
    static ToolResult<None> ValidateEntityRef(EntityRef entityRef, NormalizedItemV3 item, HashSet<int> visitedContainerModels)
    {
        if(!item.ModelPool.TryGetValue(entityRef.ModelKey, out var model))
            return ToolResult.Fail(nameof(NormalizedItemValidator), ErrorCodes.NormalizedItemValidator.ValidationError, $"Model key {entityRef.ModelKey} does not exist in model pool");

        switch (model.Type)
        {
            case ModelTypeV3.Static:
                break;
            case ModelTypeV3.Dynamic:
                {
                    if (entityRef.RelativeMovingParentKey.HasValue)
                    {
                        var result = CheckRelativeMovingParentIndex(model, item, entityRef.RelativeMovingParentKey.Value);
                        if (result.IsFailure)
                            return result;
                    }
                }
                break;
            case ModelTypeV3.Container:
                {
                    if (visitedContainerModels.Contains(entityRef.ModelKey))
                        return ToolResult.Fail(nameof(NormalizedItemValidator), ErrorCodes.NormalizedItemValidator.ValidationError, $"Cyclic model reference detected for model key {entityRef.ModelKey}");
                    visitedContainerModels.Add(entityRef.ModelKey);
                }
                break;
            case ModelTypeV3.Variant_List:
                return ToolResult.Fail(nameof(NormalizedItemValidator), ErrorCodes.NormalizedItemValidator.ValidationError, $"Entity cannot be a Variant_List");
        }

        return ValidateModel(model, item, visitedContainerModels);
    }
    static ToolResult<None> ValidateModel(NormalizedModelV3 model, NormalizedItemV3 item, HashSet<int> visitedContainerModels)
    {
        switch (model.Type)
        {
            case ModelTypeV3.Static:
                return ValidateStaticModel(model, item, visitedContainerModels);
            case ModelTypeV3.Dynamic:
                return ValidateDynamicModel(model, item, visitedContainerModels);
            case ModelTypeV3.Container:
                {
                    foreach (var childRef in model.Children)
                    {
                        var result = ValidateEntityRef(childRef, item, [.. visitedContainerModels]);
                        if (result.IsFailure)
                            return result;
                    }

                    if (model.Variants.Count > 0)
                        return ToolResult.Fail(nameof(NormalizedItemValidator), ErrorCodes.NormalizedItemValidator.ValidationError, $"Container Model must not contain variants");
                }
                break;
            case ModelTypeV3.Variant_List:
                {
                    if (model.Children.Count > 0)
                        return ToolResult.Fail(nameof(NormalizedItemValidator), ErrorCodes.NormalizedItemValidator.ValidationError, $"Variant_List Model must not contain children");

                    foreach (var variant in model.Variants)
                    {
                        var result = ValidateVariant(variant, item);
                        if (result.IsFailure)
                            return result;
                    }
                }
                break;
            case ModelTypeV3.Trigger_Waypoint:
                return ValidateWaypointModel(model, item, visitedContainerModels);
            case ModelTypeV3.Trigger_Special:
                return ValidateSpecialModel(model, item, visitedContainerModels);
        }
        return ToolResult.Success(nameof(NormalizedItemValidator));
    }

    static ToolResult<None> ValidateStaticModel(NormalizedModelV3 model, NormalizedItemV3 item, HashSet<int> visitedContainerModels)
    {
        foreach (var mesh in model.Meshes)
        {
            var result = ValidateMeshRef(mesh, item);
            if (result.IsFailure)
                return result;
        }
        foreach (var light in model.Lights)
        {
            var result = ValidateLightRef(light, item);
            if (result.IsFailure)
                return result;
        }
        if (model.Shapes.Any(s => s.Role != ShapeRoleV3.Static))
            return ToolResult.Fail(nameof(NormalizedItemValidator), ErrorCodes.NormalizedItemValidator.ValidationError, $"Static Model must not contain non-static shapes");
        if (model.Shapes.Count > 1)
            return ToolResult.Fail(nameof(NormalizedItemValidator), ErrorCodes.NormalizedItemValidator.ValidationError, $"Static Model must not contain more than one shape");
        foreach (var shape in model.Shapes)
        {
            var result = ValidateShapeRef(shape, item);
            if (result.IsFailure)
                return result;
        }
        return ToolResult.Success(nameof(NormalizedItemValidator));
    }
    static ToolResult<None> ValidateDynamicModel(NormalizedModelV3 model, NormalizedItemV3 item, HashSet<int> visitedContainerModels)
    {
        foreach (var mesh in model.Meshes)
        {
            var result = ValidateMeshRef(mesh, item);
            if (result.IsFailure)
                return result;
        }
        foreach (var light in model.Lights)
        {
            var result = ValidateLightRef(light, item);
            if (result.IsFailure)
                return result;
        }
        if (model.Shapes.Any(s => s.Role != ShapeRoleV3.Static && s.Role != ShapeRoleV3.Dynamic))
            return ToolResult.Fail(nameof(NormalizedItemValidator), ErrorCodes.NormalizedItemValidator.ValidationError, $"Dynamic Model must not contain non-static and non-dynamic shapes");
        if (model.Shapes.Count(s => s.Role == ShapeRoleV3.Dynamic) > 1)
            return ToolResult.Fail(nameof(NormalizedItemValidator), ErrorCodes.NormalizedItemValidator.ValidationError, $"Dynamic Model must not contain more than one dynamic shape");
        if (model.Shapes.Count(s => s.Role == ShapeRoleV3.Static) > 1)
            return ToolResult.Fail(nameof(NormalizedItemValidator), ErrorCodes.NormalizedItemValidator.ValidationError, $"Dynamic Model must not contain more than one static shape");
        foreach (var shape in model.Shapes)
        {
            var result = ValidateShapeRef(shape, item);
            if (result.IsFailure)
                return result;
        }
        return ToolResult.Success(nameof(NormalizedItemValidator));
    }
    static ToolResult<None> ValidateWaypointModel(NormalizedModelV3 model, NormalizedItemV3 item, HashSet<int> visitedContainerModels)
    {
        if (!model.WaypointType.HasValue)
            return ToolResult.Fail(nameof(NormalizedItemValidator), ErrorCodes.NormalizedItemValidator.ValidationError, $"Trigger_Waypoint Model must have a WaypointType");
        if (!model.WaypointNoRespawn.HasValue)
            return ToolResult.Fail(nameof(NormalizedItemValidator), ErrorCodes.NormalizedItemValidator.ValidationError, $"Trigger_Waypoint Model must have a WaypointNoRespawn value");

        if (model.Shapes.Any(s => s.Role != ShapeRoleV3.Trigger_Waypoint))
            return ToolResult.Fail(nameof(NormalizedItemValidator), ErrorCodes.NormalizedItemValidator.ValidationError, $"Trigger_Waypoint Model must not contain non-trigger_waypoint shapes");
        return ToolResult.Success(nameof(NormalizedItemValidator));
    }
    static ToolResult<None> ValidateSpecialModel(NormalizedModelV3 model, NormalizedItemV3 item, HashSet<int> visitedContainerModels)
    {
        if (!model.TriggerGameplayId.HasValue)
            return ToolResult.Fail(nameof(NormalizedItemValidator), ErrorCodes.NormalizedItemValidator.ValidationError, $"Trigger_Special Model must have a TriggerGameplayId");

        if (model.Shapes.Any(s => s.Role != ShapeRoleV3.Trigger_Special))
            return ToolResult.Fail(nameof(NormalizedItemValidator), ErrorCodes.NormalizedItemValidator.ValidationError, $"Trigger_Special Model must not contain non-trigger_special shapes");
        return ToolResult.Success(nameof(NormalizedItemValidator));
    }
    static ToolResult<None> ValidateMeshRef(MeshRef meshRef, NormalizedItemV3 item)
    {
        if (!item.MeshPool.TryGetValue(meshRef.MeshKey, out var mesh))
            return ToolResult.Fail(nameof(NormalizedItemValidator), ErrorCodes.NormalizedItemValidator.ValidationError, $"Mesh key {meshRef.MeshKey} does not exist in mesh pool");
        if (meshRef.Properties == MeshPropertiesV3.None)
            return ToolResult.Fail(nameof(NormalizedItemValidator), ErrorCodes.NormalizedItemValidator.ValidationError, $"Mesh properties {meshRef.Properties} are invalid");
        return ToolResult.Success(nameof(NormalizedItemValidator));
    }
    static ToolResult<None> ValidateShapeRef(ShapeRef shapeRef, NormalizedItemV3 item)
    {
        if (!item.ShapePool.TryGetValue(shapeRef.ShapeKey, out var shape))
            return ToolResult.Fail(nameof(NormalizedItemValidator), ErrorCodes.NormalizedItemValidator.ValidationError, $"Shape key {shapeRef.ShapeKey} does not exist in shape pool");
        return ToolResult.Success(nameof(NormalizedItemValidator));
    }
    static ToolResult<None> ValidateLightRef(LightRef lightRef, NormalizedItemV3 item)
    {
        if (!item.LightPool.TryGetValue(lightRef.LightKey, out var light))
            return ToolResult.Fail(nameof(NormalizedItemValidator), ErrorCodes.NormalizedItemValidator.ValidationError, $"Light key {lightRef.LightKey} does not exist in light pool");
        return ToolResult.Success(nameof(NormalizedItemValidator));
    }
    static ToolResult<None> ValidateVariant(NormalizedVariantV3 variant, NormalizedItemV3 item)
    {
        if(!item.ModelPool.TryGetValue(variant.ModelKey, out var variantModel))
            return ToolResult.Fail(nameof(NormalizedItemValidator), ErrorCodes.NormalizedItemValidator.ValidationError, $"Model key {variant.ModelKey} does not exist in model pool");
        return ValidateModel(variantModel, item, []);
    }
    static ToolResult<None> CheckRelativeMovingParentIndex(NormalizedModelV3 model, NormalizedItemV3 item, int relativeMovingParentIndex)
    {
        if(relativeMovingParentIndex == -1)
            return ToolResult.Success(nameof(NormalizedItemValidator));

        var hasParent = item.ModelPool.TryGetValue(relativeMovingParentIndex, out var parentModel);
        if (!hasParent)
            return ToolResult.Fail(nameof(NormalizedItemValidator), ErrorCodes.NormalizedItemValidator.ValidationError, $"Relative moving parent index {relativeMovingParentIndex} does not exist in model pool");
        if(parentModel == model)
            return ToolResult.Fail(nameof(NormalizedItemValidator), ErrorCodes.NormalizedItemValidator.ValidationError, $"Relative moving parent index {relativeMovingParentIndex} refers to the same model");
        if(parentModel!.Type != ModelTypeV3.Dynamic)
            return ToolResult.Fail(nameof(NormalizedItemValidator), ErrorCodes.NormalizedItemValidator.ValidationError, $"Relative moving parent index {relativeMovingParentIndex} refers to a non-dynamic model");

        return ToolResult.Success(nameof(NormalizedItemValidator));
    }
}
