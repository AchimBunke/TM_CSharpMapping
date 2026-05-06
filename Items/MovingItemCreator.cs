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


    public struct MovingItemCreatorSettings
    {
        public MovingItemCreatorSettings() { }

        public int? RotationAnimationCount { get; init; } = null;
        public int? TranslationAnimationCount { get; init; } = null;
        public CGameItemModel? MovingItemAnimationTemplate { get; init; } = null;
        public IReadOnlyDictionary<string, string> MaterialLinkReplacements { get; init; } = new Dictionary<string, string>();
    }

    MovingItemCreatorSettings _settings;
    MeshBuilder _meshBuilder;
    MeshExtractor _meshExtractor;
    public MovingItemCreator() : this(new(), new(),new MovingItemCreatorSettings())
    {
    }
    public MovingItemCreator(MeshExtractor meshExtractor, MeshBuilder meshBuilder, MovingItemCreatorSettings settings)
    {
        _settings = settings;
        _meshBuilder = meshBuilder;
        _meshExtractor = meshExtractor;
    }
    

    public ToolResult<CGameItemModel> CreateMovingItem(CGameItemModel sourceItem)
    {
        var extractResult = _meshExtractor.ExtractMesh(sourceItem);
        if(!extractResult.IsSuccess)
            return ToolResult.Fail(nameof(MovingItemCreator), ErrorCodes.MovingItemCreator.MeshExtractionFailed, extractResult);

        var movingItem = _meshBuilder.BuildMovingItem(extractResult.Value);

        ReplaceMaterialLinks(movingItem);
        if (_settings.MovingItemAnimationTemplate != null)
            CopyAnimations(_settings.MovingItemAnimationTemplate, movingItem);
        ChangeAnimations(movingItem);

        movingItem.Icon = sourceItem.Icon;
        movingItem.IconWebP = sourceItem.IconWebP;
        movingItem.DefaultPlacement = sourceItem.DefaultPlacement;
        movingItem.GroundPoint = sourceItem.GroundPoint;
        movingItem.Name = sourceItem.Name + " Moving";
        movingItem.OrbitalCenterHeightFromGround = sourceItem.OrbitalCenterHeightFromGround;
        movingItem.OrbitalPreviewAngle = sourceItem.OrbitalPreviewAngle;
        movingItem.OrbitalRadiusBase = sourceItem.OrbitalRadiusBase;
        //movingItem.Node.PainterGroundMargin = item.Node.PainterGroundMargin;

        return ToolResult.Success(movingItem, nameof(MovingItemCreator));

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
        ItemExtensions.TryGetNPlugDyna_SKinematicConstraint(from, out var kinematicSource);
        ItemExtensions.TryGetNPlugDyna_SKinematicConstraint(to, out var kinematicTarget);
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

        kinematicTarget.RotAnimFunc = ItemExtensions.DeepCloneObject(kinematicSource.RotAnimFunc);
        kinematicTarget.TransAnimFunc = ItemExtensions.DeepCloneObject(kinematicSource.TransAnimFunc);

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
