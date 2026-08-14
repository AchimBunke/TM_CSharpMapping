using GBX.NET;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.Plug;
using System.Numerics;
using TM_GenericMapping.Common;
using TM_GenericMapping.Messaging;
using TmEssentials;
using static GBX.NET.Engines.Plug.CPlugSurface;

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

    MovingItemCreatorSettings _settings;
    MeshBuilder _meshBuilder;
    MeshExtractor _meshExtractor;
    public MovingItemCreator() : this(new MovingItemCreatorSettings())
    {
    }
    public MovingItemCreator(MovingItemCreatorSettings settings) : this(new(), new(), settings)
    {
    }
    public MovingItemCreator(MeshExtractor meshExtractor, MeshBuilder meshBuilder, MovingItemCreatorSettings settings)
    {
        _settings = settings;
        _meshBuilder = meshBuilder;
        _meshExtractor = meshExtractor;
    }
    

    public ToolResult<CGameItemModel> CreateMovingItem(CGameItemModel sourceItem, MeshBuilder.BuildSettings? buildOptions = null)
    {
        var extractResult = _meshExtractor.ExtractMesh(sourceItem);
        if(!extractResult.IsSuccess)
            return ToolResult.Fail(nameof(MovingItemCreator), ErrorCodes.MovingItemCreator.MeshExtractionFailed, extractResult);

        var movingItemResult = _meshBuilder.BuildGeneralItem(extractResult.Value, buildOptions ?? MeshBuilder.BuildSettings.DefaultFromMesh(extractResult.Value));
        if(movingItemResult.IsFailure)
            return ToolResult.Fail(nameof(MovingItemCreator), ErrorCodes.MovingItemCreator.MeshBuildingFailed, movingItemResult);

        var movingItem = movingItemResult.Value;

        ReplaceMaterialLinks(movingItem);
        if (_settings.MovingItemAnimationTemplate != null && _settings.MergeOptions.HasFlag(MergeOptions.UseTemplateAnimations))
            CopyAnimations(_settings.MovingItemAnimationTemplate, movingItem);
        ChangeAnimations(movingItem);

        CopyItemData(movingItem, sourceItem, _settings.MovingItemAnimationTemplate);

        return ToolResult.Success(movingItem, nameof(MovingItemCreator));

    }
    void CopyItemData(CGameItemModel movingItem, CGameItemModel sourceItem, CGameItemModel template)
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

        if (_settings.MergeOptions.HasFlag(MergeOptions.UseTemplateIcon))
        {
            ChunkSafeItemOperations.SetIcon(movingItem, template.Icon, template.IconWebP);
        }
        if(_settings.MergeOptions.HasFlag(MergeOptions.UseTemplatePlacement))
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
        if (_settings.MaterialLinkReplacements.Count == 0)
            return;
        var dynaModel = ItemExtensions.TryGetDynaObjectModel(item, out var model);
        foreach(var mat in model.Mesh.CustomMaterials)
        {
            if (_settings.MaterialLinkReplacements.TryGetValue(mat.MaterialUserInst.Link, out var replacement))
                mat.MaterialUserInst.Link = replacement;
        }
    }
    void CopyAnimations(CGameItemModel from, CGameItemModel to)
    {
        if(!ItemExtensions.TryGetNPlugDyna_SKinematicConstraint(from, out var kinematicSource)||
            !ItemExtensions.TryGetNPlugDyna_SKinematicConstraint(to, out var kinematicTarget))
            return;
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
    void ChangeAnimations(CGameItemModel movingItem)
    {
        if (_settings.TranslationAnimationCount == null && _settings.RotationAnimationCount == null)
            return;
        ItemExtensions.TryGetNPlugDyna_SKinematicConstraint(movingItem, out var kinematic);
        if(_settings.TranslationAnimationCount != null)
        {
            int requiredNumSubFuncs = _settings.TranslationAnimationCount.Value;
            var subFuncs = kinematic.TransAnimFunc.SubFuncs.Take(requiredNumSubFuncs).ToList();
            for (int i = subFuncs.Count; i < requiredNumSubFuncs; ++i)
            {
                subFuncs.Add(new NPlugDyna_SKinematicConstraint.SubAnimFunc()
                {
                    Ease = NPlugDyna_SKinematicConstraint.AnimEase.Constant,
                    Duration = TimeInt32.Zero,
                    Reverse = false,
                });
            }
            kinematic.TransAnimFunc.SubFuncs = subFuncs.ToArray();
        }
        if (_settings.RotationAnimationCount != null)
        {
            int requiredNumSubFuncs = _settings.RotationAnimationCount.Value;
            var subFuncs = kinematic.RotAnimFunc.SubFuncs.Take(requiredNumSubFuncs).ToList();
            for (int i = subFuncs.Count; i < requiredNumSubFuncs; ++i)
            {
                subFuncs.Add(new NPlugDyna_SKinematicConstraint.SubAnimFunc()
                {
                    Ease = NPlugDyna_SKinematicConstraint.AnimEase.Constant,
                    Duration = TimeInt32.Zero,
                    Reverse = false,
                });
            }
            kinematic.RotAnimFunc.SubFuncs = subFuncs.ToArray();
        }
    }
}
