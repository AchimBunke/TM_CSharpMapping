using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;
using TM_GenericMapping.Common;
using TM_GenericMapping.Items.MeshCompilation;
using TM_GenericMapping.Messaging;
using TmEssentials;

namespace TM_GenericMapping.Items;

public class MovingItemCreator
{

    [Flags]
    public enum MergeOptions
    {
        None = 0,

        UseTemplateAnimations = 1 << 0,
        UseTemplateIcon = 1 << 1,
        UseTemplatePlacement = 1 << 2,

    }

    public struct MovingItemCreatorSettings
    {
        public MovingItemCreatorSettings() { }

        public int? RotationAnimationCount { get; init; } = null;
        public int? TranslationAnimationCount { get; init; } = null;
        public CGameItemModel? MovingItemAnimationTemplate { get; init; } = null;
        public IReadOnlyDictionary<string, string> MaterialLinkReplacements { get; init; } = new Dictionary<string, string>();
        public MergeOptions MergeOptions { get; init; } = MergeOptions.None;
    }

    public MovingItemCreatorSettings Settings { get; set; }
    ItemCompiler _itemCompiler;
    ItemParser _itemParser;
    CompileOptions _compileOptions;
    public MovingItemCreator() : this(new MovingItemCreatorSettings())
    {
    }
    public MovingItemCreator(MovingItemCreatorSettings settings) : this(new(), new(), settings)
    {
    }
    public MovingItemCreator(ItemParser itemParser, ItemCompiler itemCompiler, MovingItemCreatorSettings settings)
    {
        Settings = settings;
        _itemCompiler = itemCompiler;
        _itemParser = itemParser;
        _compileOptions = new()
        {
            Optimization = ItemCompilerOptimization.None,
            Target = ItemModel.Prefab,
        };
    }
    

    public ToolResult<CGameItemModel> CreateMovingItem(CGameItemModel sourceItem, BuildSettings? buildOptions = null)
    {
        var parseResult = _itemParser.Parse(sourceItem);
        if(!parseResult.IsSuccess)
            return ToolResult.Fail(nameof(MovingItemCreator), ErrorCodes.MovingItemCreator.MeshExtractionFailed, parseResult);


        var buildSettings = buildOptions ?? CreateDefaultBuildOptions(parseResult.Value, BuildSettings.DefaultFromItem(parseResult.Value));
        var movingItemResult = _itemCompiler.Compile(parseResult.Value, buildSettings, _compileOptions);

        if(movingItemResult.IsFailure)
            return ToolResult.Fail(nameof(MovingItemCreator), ErrorCodes.MovingItemCreator.MeshBuildingFailed, movingItemResult);

        var movingItem = movingItemResult.Value;

        ReplaceMaterialLinks(movingItem);
        if (Settings.MovingItemAnimationTemplate != null && Settings.MergeOptions.HasFlag(MergeOptions.UseTemplateAnimations))
            CopyAnimations(Settings.MovingItemAnimationTemplate, movingItem);
        ChangeAnimations(movingItem);

        CopyItemData(movingItem, sourceItem, Settings.MovingItemAnimationTemplate);

        return ToolResult.Success(movingItem, nameof(MovingItemCreator));

    }
    BuildSettings CreateDefaultBuildOptions(NormalizedItem normItem, BuildSettings defaultOptions)
    {
        if (defaultOptions.Clusters.Any(c => c.Value.Type == ModelType.Dynamic))
            return defaultOptions;

        var staticGroups = defaultOptions.Clusters.Where(gs => gs.Value.Type == ModelType.Static).ToList();
        for (int i = 0; i < staticGroups.Count; i++)
        {
            var group = staticGroups[i];
            group.Value.Type = ModelType.Dynamic;

            defaultOptions.Clusters[group.Key] = group.Value;

            var groupInstances = defaultOptions.Instances.Where(i => i.Value.ClusterKey == group.Key).ToArray();
            bool meshCollidable = groupInstances.Any(i => i.Value.Collidable && i.Value.Kind == RefKind.Mesh);
            bool hasDynaShape = groupInstances.Any(i =>i.Value.ShapeRoleOverride.HasValue && i.Value.ShapeRoleOverride.Value == ShapeRole.Dynamic);
            foreach (var instance in groupInstances)
            {
                if(instance.Value.ShapeRoleOverride.HasValue && 
                    instance.Value.ShapeRoleOverride == ShapeRole.Static &&
                    !hasDynaShape &&
                    !meshCollidable)
                {
                    var dynaInstance = new InstanceSettings()
                    {
                        ClusterKey = instance.Value.ClusterKey,
                        Enabled = instance.Value.Enabled,
                        Visible = instance.Value.Visible,
                        SmoothingGroupOverride = instance.Value.SmoothingGroupOverride,
                        LightmapSizeOverride = instance.Value.LightmapSizeOverride,
                        Collidable = instance.Value.Collidable,
                        Kind = instance.Value.Kind,
                        LightTypeOverride = instance.Value.LightTypeOverride,
                        LODMaskOverride = instance.Value.LODMaskOverride,
                        ShapeRoleOverride = ShapeRole.Dynamic,
                    };
                    var entRef = defaultOptions.EntityClusterAssignments.FirstOrDefault(ec => ec.Value == group.Key);
                    var model = FindModel(normItem, entRef.Key);
                    var shapeKey = model.Shapes.First(s => s.Id == instance.Key).ShapeKey;

                    var dynaShapeRef = new ShapeRef()
                    {
                        Id = Guid.NewGuid(),
                        Role = ShapeRole.Dynamic,
                        ShapeKey = shapeKey,
                    };
                    model.Shapes.Add(dynaShapeRef);
                    defaultOptions.Instances.Add(dynaShapeRef.Id, dynaInstance);
                }
            }

        }

        return defaultOptions;
    }
    NormalizedModel? FindModel(NormalizedItem item, Guid entRefId)
    {
        NormalizedModel? SearchModel(NormalizedModel model, Guid entRefId)
        {
            var found = model.Children.FirstOrDefault(c => c.Id == entRefId, null);
            if (found != null)
                return item.ModelPool[found.ModelKey];
            foreach(var child in model.Children)
            {
                var result = SearchModel(item.ModelPool[child.ModelKey], entRefId);
                if (result != null)
                    return result;
            }
            return null;
        }
        return SearchModel(item.Model, entRefId);
    }
    void CopyItemData(CGameItemModel movingItem, CGameItemModel sourceItem, CGameItemModel? template)
    {

        ChunkSafeItemOperations.SetIcon(movingItem, sourceItem.Icon, sourceItem.IconWebP);
        movingItem.DefaultPlacement = sourceItem.DefaultPlacement;
        movingItem.GroundPoint = sourceItem.GroundPoint;
        movingItem.Name = sourceItem.Name + " Moving";
        movingItem.OrbitalCenterHeightFromGround = sourceItem.OrbitalCenterHeightFromGround;
        movingItem.OrbitalPreviewAngle = sourceItem.OrbitalPreviewAngle;
        movingItem.OrbitalRadiusBase = sourceItem.OrbitalRadiusBase;

        if (template == null)
            return;

        if (Settings.MergeOptions.HasFlag(MergeOptions.UseTemplateIcon))
        {
            ChunkSafeItemOperations.SetIcon(movingItem, template.Icon, template.IconWebP);
        }
        if(Settings.MergeOptions.HasFlag(MergeOptions.UseTemplatePlacement))
        {
            movingItem.DefaultPlacement = template.DefaultPlacement;
            movingItem.GroundPoint = template.GroundPoint;
            movingItem.OrbitalCenterHeightFromGround = template.OrbitalCenterHeightFromGround;
            movingItem.OrbitalPreviewAngle = template.OrbitalPreviewAngle;
            movingItem.OrbitalRadiusBase = template.OrbitalRadiusBase;
        }
    }
    void ReplaceMaterialLinks(CGameItemModel item)
    {
        if (Settings.MaterialLinkReplacements.Count == 0)
            return;
        var hasDynaModel = ItemExtensions.TryGetDynaObjectModel(item, out var model);
        if (!hasDynaModel)
            return;
        foreach (var mat in model!.Mesh!.CustomMaterials ?? [])
        {
            if(mat.MaterialUserInst!.Link == null)
                continue;
            if (Settings.MaterialLinkReplacements.TryGetValue(mat.MaterialUserInst.Link, out var replacement))
                mat.MaterialUserInst.Link = replacement;
        }
    }
    void CopyAnimations(CGameItemModel from, CGameItemModel to)
    {
        if(!ItemExtensions.TryGetNPlugDyna_SKinematicConstraint(from, out var kinematicSource)||
            !ItemExtensions.TryGetAllNPlugDyna_SKinematicConstraints(to, out var kinematicTargets))
            return;
        foreach (var kinematicTarget in kinematicTargets)
        {
            kinematicTarget.AngleMaxDeg = kinematicSource.AngleMaxDeg;
            kinematicTarget.AngleMinDeg = kinematicSource.AngleMinDeg;
            kinematicTarget.RotAxis = kinematicSource.RotAxis;
            kinematicTarget.TransAxis = kinematicSource.TransAxis;
            kinematicTarget.TransMax = kinematicSource.TransMax;
            kinematicTarget.TransMin = kinematicSource.TransMin;
            kinematicTarget.ShaderTcAnimFunc = kinematicSource.ShaderTcAnimFunc;
            kinematicTarget.ShaderTcDataTransSub = kinematicSource.ShaderTcDataTransSub;
            kinematicTarget.ShaderTcType = kinematicSource.ShaderTcType;
            kinematicTarget.ShaderTcVersion = kinematicSource.ShaderTcVersion;

            kinematicTarget.RotAnimFunc = ObjectCloner.DeepCloneObject(kinematicSource.RotAnimFunc);
            kinematicTarget.TransAnimFunc = ObjectCloner.DeepCloneObject(kinematicSource.TransAnimFunc);
        }

    }
    void ChangeAnimations(CGameItemModel movingItem)
    {
        if (Settings.TranslationAnimationCount == null && Settings.RotationAnimationCount == null)
            return;
        ItemExtensions.TryGetAllNPlugDyna_SKinematicConstraints(movingItem, out var kinematicConstraints);
        foreach (var constraint in kinematicConstraints)
        {
            if (Settings.TranslationAnimationCount != null && constraint.TransAnimFunc != null)
            {
                int requiredNumSubFuncs = Settings.TranslationAnimationCount.Value;
                var subFuncs = constraint.TransAnimFunc.SubFuncs?.Take(requiredNumSubFuncs).ToList() ?? [];
                for (int i = subFuncs.Count; i < requiredNumSubFuncs; ++i)
                {
                    subFuncs.Add(new NPlugDyna_SKinematicConstraint.SubAnimFunc()
                    {
                        Ease = NPlugDyna_SKinematicConstraint.AnimEase.Constant,
                        Duration = TimeInt32.Zero,
                        Reverse = false,
                    });
                }
                constraint.TransAnimFunc.SubFuncs = subFuncs.ToArray();
            }
            if (Settings.RotationAnimationCount != null && constraint?.RotAnimFunc != null)
            {
                int requiredNumSubFuncs = Settings.RotationAnimationCount.Value;
                var subFuncs = constraint.RotAnimFunc.SubFuncs?.Take(requiredNumSubFuncs).ToList() ?? [];
                for (int i = subFuncs.Count; i < requiredNumSubFuncs; ++i)
                {
                    subFuncs.Add(new NPlugDyna_SKinematicConstraint.SubAnimFunc()
                    {
                        Ease = NPlugDyna_SKinematicConstraint.AnimEase.Constant,
                        Duration = TimeInt32.Zero,
                        Reverse = false,
                    });
                }
                constraint.RotAnimFunc.SubFuncs = subFuncs.ToArray();
            }
        }
       
    }
}
