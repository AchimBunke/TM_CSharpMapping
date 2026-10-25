using GBX.NET;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.Plug;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using TM_GenericMapping.Common;
using TM_GenericMapping.Templating;
using TmEssentials;
using static GBX.NET.Engines.GameData.CGameItemModel;
using static GBX.NET.Engines.Meta.NPlugDyna_SKinematicConstraint;
using static TM_GenericMapping.Items.ItemPlacementUtils;

namespace TM_GenericMapping.Items.FbxGbxConversion.Serialization;

[Flags]
public enum ItemConversionOptions
{
    None = 0,
    MeshConfigFromObjectNames = 1 << 0,
    IgnoreMeshesWithInvalidMaterials = 1 << 1,
    SkipStaticItemGrouping = 1 << 2,
}
public class ItemConfig
{
    [JsonPropertyName("Version")] 
    public int Version { get; set; } = 1;


    [JsonPropertyName("Type")]
    public string Type { get; init; } = "StaticObject";

    [JsonPropertyName("Collection")]
    public string Collection { get; init; } = "Stadium";

    [JsonPropertyName("AuthorName")]
    public string AuthorName { get; set; } = "";

    [JsonPropertyName("Name")]
    public string? Name { get; set; } = null;

    [JsonPropertyName("Description")]
    public string? Description { get; set; } = null;


    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("Waypoint")]
    public Waypoint? Waypoint
    {
        get; set;
    }


    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("Placement")]
    public PlacementConfig? Placement
    {
        get; set;
    }

    [JsonPropertyName("Scale")]
    public float Scale { get; set; } = 1.0f;

    [JsonPropertyName("Materials")]
    public List<MaterialConfig> Materials { get; set; } = new List<MaterialConfig>();

    [JsonPropertyName("Meshes")]
    public List<MeshConfig> Meshes { get; set; } = new List<MeshConfig>();

    [JsonPropertyName("Lights")]
    public List<LightConfig> Lights { get; set; } = new List<LightConfig>();

    [JsonPropertyName("MovingGroups")]
    public List<MovingGroupConfig> MovingGroups { get; set; } = new List<MovingGroupConfig>();


    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("LodParameters")]
    public LodParameters? LodParameters
    {
        get; set;
    }

    [JsonPropertyName("ConversionOptions")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ItemConversionOptions ConversionOptions
    {
        get; set;
    }




    public static ItemConfig Deserialize(string filePath)
    {
        using var itemInfoStream = File.OpenRead(filePath);
        return Deserialize(itemInfoStream);
    }
    public static ItemConfig Deserialize(Stream itemInfoStream)
    {
        return JsonSerializer.Deserialize<ItemConfig>(itemInfoStream, serializerOptions) ?? throw new InvalidOperationException("Failed to deserialize ItemInfoJson.");
    }
    public static void Serialize(ItemConfig item, string filePath)
    {
        using var itemInfoStream = File.Create(filePath);
        Serialize(item, itemInfoStream);
    }
    public static void Serialize(ItemConfig item, Stream itemInfoStream)
    {
        JsonSerializer.Serialize(itemInfoStream, item, serializerOptions);
    }

    static JsonSerializerOptions serializerOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        Converters = {
            new Vec3Converter(),
            new JsonStringEnumConverter(),
            new ColorJsonConverter(),
            new PlacementParameterConverter(),
            new PatchLayoutConverter(),
            new NPlugItemPlacement_SClassConverter(),
            new NPlugDyna_SKinematicConstraintConverter(),
            new AnimFuncConverter(),
            new SubAnimFuncConverter(),
            new AnimFuncNatConverter(),
            new TransSubTextureInConverter(),
            new NPlugDynaObjectModel_SInstanceParamsConverter()

        }
    };
}


public class Waypoint
{
    [JsonPropertyName("Type")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public EWaypointType Type { get; set; } = EWaypointType.Checkpoint;

    [JsonPropertyName("NoRespawn")]
    public bool NoRespawn
    {
        get; set;
    }
    [JsonPropertyName("DefaultGravitySpawn")]
    public Vec3? DefaultGravitySpawn { get; set; } = new Vec3(0, -1, 0);

    [JsonPropertyName("TorqueX")]
    public float? TorqueX { get; set; } = 0;

    [JsonPropertyName("TorqueDuration")]
    public int? TorqueDuration { get; set; } = 0;
}


public class LodParameters
{
    /// <summary>
    /// Mismatch between distance value and actual ingame distance (value = 100 => 200 units ingame)
    /// </summary>
    [JsonPropertyName("MaxLodDistances")]
    public List<float> MaxLodDistances { get; set; } = [];
}

public class MovingGroupConfig
{
    [JsonPropertyName("MovingGroupId")]
    public string MovingGroupId
    {
        get; set;
    } = string.Empty;

    [JsonPropertyName("ParentMovingGroupId")]
    public string? ParentMovingGroupId { get; set; } = null;

    [JsonPropertyName("RotationAnchorNode")]
    public string? RotationAnchorNode { get; set; } = null;

    [JsonPropertyName("KinematicMovement")]
    public KinematicMovement KinematicMovement
    {
        get; set;
    } = new KinematicMovement();

    [JsonPropertyName("KinematicModel")]
    public KinematicModelConfig KinematicModel
    {
        get; set;
    } = new KinematicModelConfig();

    public static NPlugDynaObjectModel_SInstanceParams ToInstanceParams(KinematicModelConfig kinematicModelConfig)
    {
        var instanceParams = new NPlugDynaObjectModel_SInstanceParams
        {
            Version = 2,
            PeriodSc = kinematicModelConfig.PeriodicSc,
            TextureId = kinematicModelConfig.TextureId,
            IsKinematic = kinematicModelConfig.IsKinematic,
            PeriodScMax = kinematicModelConfig.PeriodicScMax,
            Phase01 = kinematicModelConfig.Phase01,
            Phase01Max = kinematicModelConfig.Phase01Max,
            CastStaticShadow = kinematicModelConfig.CastStaticShadow,
        };
        return instanceParams;
    }
    public static KinematicModelConfig FromInstanceParams(NPlugDynaObjectModel_SInstanceParams instanceParams)
    {
        var kinematicModelConfig = new KinematicModelConfig
        {
            PeriodicSc = instanceParams.PeriodSc,
            TextureId = instanceParams.TextureId,
            IsKinematic = instanceParams.IsKinematic,
            PeriodicScMax = instanceParams.PeriodScMax,
            Phase01 = instanceParams.Phase01,
            Phase01Max = instanceParams.Phase01Max,
            CastStaticShadow = instanceParams.CastStaticShadow,
        };
        return kinematicModelConfig;
    }
    public static NPlugDyna_SKinematicConstraint ToKinematicConstraint(KinematicMovement kinematicMovementConfig)
    {
        var instanceParams = new NPlugDyna_SKinematicConstraint
        {
            Version = 0,
            TransAxis = kinematicMovementConfig.TransAxis,
            TransMin = kinematicMovementConfig.TransMin,
            TransMax = kinematicMovementConfig.TransMax,
            TransAnimFunc = new NPlugDyna_SKinematicConstraint.AnimFunc()
            {
                IsDuration = true,
                SubFuncs = kinematicMovementConfig.TranslationAnims.Select(sf =>
                {
                    return new NPlugDyna_SKinematicConstraint.SubAnimFunc
                    {
                        Ease = sf.Ease,
                        Reverse = sf.Reverse,
                        Duration = TimeInt32.FromMilliseconds(sf.Duration),
                    };
                }).ToArray()
            },

            RotAxis = kinematicMovementConfig.RotAxis,
            AngleMinDeg = kinematicMovementConfig.AngleMinDeg,
            AngleMaxDeg = kinematicMovementConfig.AngleMaxDeg,
            RotAnimFunc = new NPlugDyna_SKinematicConstraint.AnimFunc()
            {
                IsDuration = true,
                SubFuncs = kinematicMovementConfig.RotationAnims.Select(sf =>
                {
                    return new NPlugDyna_SKinematicConstraint.SubAnimFunc
                    {
                        Ease = sf.Ease,
                        Reverse = sf.Reverse,
                        Duration = TimeInt32.FromMilliseconds(sf.Duration),
                    };
                }).ToArray()
            },

            ShaderTcType = kinematicMovementConfig.MovingTexture != null ? NPlugDyna_SKinematicConstraint.EShaderTcType.TransSubTexture : NPlugDyna_SKinematicConstraint.EShaderTcType.None,
            ShaderTcVersion = 0,
            ShaderTcDataTransSub = new TransSubTextureIn()
            {
                NbSubTexture = kinematicMovementConfig.MovingTexture?.SubTextures ?? 0,
                NbSubTexturePerLine = kinematicMovementConfig.MovingTexture?.SubTexturesPerLine ?? 0,
                NbSubTexturePerColumn = kinematicMovementConfig.MovingTexture?.SubTexturesPerColumn ?? 0,
                TopToBottom = kinematicMovementConfig.MovingTexture?.TopToBottom ?? false
            },
            ShaderTcAnimFunc = kinematicMovementConfig.MovingTexture?.TextureAnims.Select(sf =>
            {
                return new NPlugDyna_SKinematicConstraint.AnimFuncNat
                {
                    Duration = TimeInt32.FromMilliseconds(sf.Duration),
                    TextureId = sf.TextureID,
                };
            }).ToArray() ?? Array.Empty<NPlugDyna_SKinematicConstraint.AnimFuncNat>(),
            SubVersion = 3,
        };
        return instanceParams;
    }
    public static KinematicMovement FromKinematicConstraint(NPlugDyna_SKinematicConstraint kinematicConstraint)
    {
        var kinematicMovement = new KinematicMovement()
        {
            TransAxis = kinematicConstraint.TransAxis,
            TransMin = kinematicConstraint.TransMin,
            TransMax = kinematicConstraint.TransMax,
            TranslationAnims = kinematicConstraint.TransAnimFunc?.SubFuncs?.Select(sf =>
            {
                return new SubAnimFunc
                {
                    Ease = sf.Ease,
                    Reverse = sf.Reverse,
                    Duration = (uint)sf.Duration.Milliseconds,
                };
            }).ToList() ?? new List<SubAnimFunc>(),

            RotAxis = kinematicConstraint.RotAxis,
            AngleMinDeg = kinematicConstraint.AngleMinDeg,
            AngleMaxDeg = kinematicConstraint.AngleMaxDeg,
            RotationAnims = kinematicConstraint.RotAnimFunc?.SubFuncs?.Select(sf =>
            {
                return new SubAnimFunc
                {
                    Ease = sf.Ease,
                    Reverse = sf.Reverse,
                    Duration = (uint)sf.Duration.Milliseconds,
                };
            }).ToList() ?? new List<SubAnimFunc>(),

            MovingTexture = kinematicConstraint.ShaderTcType == NPlugDyna_SKinematicConstraint.EShaderTcType.TransSubTexture ? new MovingTextureConfig
            {
                SubTextures = kinematicConstraint.ShaderTcDataTransSub.NbSubTexture,
                SubTexturesPerLine = kinematicConstraint.ShaderTcDataTransSub.NbSubTexturePerLine,
                SubTexturesPerColumn = kinematicConstraint.ShaderTcDataTransSub.NbSubTexturePerColumn,
                TopToBottom = kinematicConstraint.ShaderTcDataTransSub.TopToBottom,
                TextureAnims = kinematicConstraint.ShaderTcAnimFunc?.Select(sf => new TextureAnim
                {
                    Duration = (uint)sf.Duration.Milliseconds,
                    TextureID = sf.TextureId,
                }).ToList() ?? new List<TextureAnim>(),
            } : null,
        };

        return kinematicMovement;
    }
}
public class KinematicMovement
{
    [JsonPropertyName("TransAxis")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public EAxis TransAxis
    {
        get; set;
    }

    [JsonPropertyName("TransMin")]
    public float TransMin
    {
        get; set;
    }

    [JsonPropertyName("TransMax")]
    public float TransMax
    {
        get; set;
    }

    [JsonPropertyName("RotAxis")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public EAxis RotAxis
    {
        get; set;
    }

    [JsonPropertyName("AngleMinDeg")]
    public float AngleMinDeg
    {
        get; set;
    }

    [JsonPropertyName("AngleMaxDeg")]
    public float AngleMaxDeg
    {
        get; set;
    }

    [JsonPropertyName("TranslationAnims")]
    public List<SubAnimFunc> TranslationAnims { get; set; } = [];

    [JsonPropertyName("RotationAnims")]
    public List<SubAnimFunc> RotationAnims { get; set; } = [];

    [JsonPropertyName("MovingTexture")]
    public MovingTextureConfig? MovingTexture{ get; set; }


}

public class MovingTextureConfig
{
    /// <summary>
    /// Number of subtextures.. if anims contains texture ids that are not in the range of 0 to SubTextures-1, the last texture will be used instead. (e.g. SubTextures = 4, anims contains texture id 5, then texture id 3 will be used)
    /// </summary>
    [JsonPropertyName("SubTextures")]
    public int SubTextures { get; set; }

    /// <summary>
    /// Number of subtextures per line in the texture atlas. (e.g. 4x4 atlas, SubTexturePerLine = 4, SubTexturePerColumn = 4)
    /// </summary>
    [JsonPropertyName("SubTexturesPerLine")]
    public int SubTexturesPerLine { get; set; }

    /// <summary>
    /// Number of subtextures per column in the texture atlas. (e.g. 4x4 atlas, SubTexturePerLine = 4, SubTexturePerColumn = 4)
    /// </summary>
    [JsonPropertyName("SubTexturesPerColumn")]
    public int SubTexturesPerColumn { get; set; }

    /// <summary>
    /// If true, the texture indexes are counted from top to bottom, otherwise from bottom to top. 
    /// </summary>
    [JsonPropertyName("TopToBottom")]
    public bool TopToBottom { get; set; }

    [JsonPropertyName("TextureAnims")]
    public List<TextureAnim> TextureAnims { get; set; } = [];

}
public class TextureAnim
{
    [JsonPropertyName("Duration")]
    public uint Duration { get; set; }

    [JsonPropertyName("TextureID")]
    public int TextureID { get; set; }
}

public class KinematicModelConfig
{
    [JsonPropertyName("CastStaticShadow")]
    public bool CastStaticShadow { get; set; }

    [JsonPropertyName("IsKinematic")]
    public bool IsKinematic { get; set; } = true;

    [JsonPropertyName("PeriodicSc")]
    public float PeriodicSc { get; set; } = 1;

    [JsonPropertyName("PeriodicScMax")]
    public float PeriodicScMax { get; set; } = -1;

    [JsonPropertyName("Phase01")]
    public float Phase01 { get; set; } = -1;

    [JsonPropertyName("Phase01Max")]
    public float Phase01Max { get; set; } = -1;

    [JsonPropertyName("TextureId")]
    public int TextureId { get; set; } = 0;
}

public class SubAnimFunc
{
    [JsonPropertyName("Ease")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AnimEase Ease
    {
        get; set;
    }

    [JsonPropertyName("Reverse")]
    public bool Reverse
    {
        get; set;
    }

    [JsonPropertyName("Duration")]
    public uint Duration
    {
        get; set;
    }
}

public class LightConfig
{
    [JsonPropertyName("Name")]
    public string Name
    {
        get; set;
    } = string.Empty;

    [JsonPropertyName("Type")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public MeshCompilation.LightType? Type
    {
        get; set;
    }

    [JsonPropertyName("Color")]
    public System.Drawing.Color Color
    {
        get; set;
    }

    [JsonPropertyName("Intensity")]
    public float Intensity
    {
        get; set;
    }

    [JsonPropertyName("Distance")]
    public float Distance
    {
        get; set;
    }

    [JsonPropertyName("NightOnly")]
    public bool NightOnly
    {
        get; set;
    }

    [JsonPropertyName("PointEmissionRadius")]
    public float PointEmissionRadius
    {
        get; set;
    }

    [JsonPropertyName("PointEmissionLength")]
    public float PointEmissionLength
    {
        get; set;
    }

    [JsonPropertyName("SpotInnerAngle")]
    public float SpotInnerAngle { get; set; } = 40;

    [JsonPropertyName("SpotOuterAngle")]
    public float SpotOuterAngle { get; set; } = 60;

    [JsonPropertyName("SpotEmissionRadius")]
    public float SpotEmissionRadius
    {
        get; set;
    }

    [JsonPropertyName("SpotEmissionLength")]
    public float SpotEmissionLength
    {
        get; set;
    }
}

public class PlacementConfig
{
    [JsonPropertyName("YawOnly")]
    public bool YawOnly { get; set; }

    [JsonPropertyName("NotOnObject")]
    public bool NotOnObject { get; set; }

    [JsonPropertyName("AutoRotation")]
    public bool AutoRotation { get; set; }

    [JsonPropertyName("SwitchPivotManually")]
    public bool SwitchPivotManually { get; set; }

    [JsonPropertyName("CubeCenter")]
    public Vec3? CubeCenter { get; set; }

    [JsonPropertyName("CubeSize")]
    public float CubeSize { get; set; }

    [JsonPropertyName("GridSnapHStep")]
    public float GridSnapHStep { get; set; }

    [JsonPropertyName("GridSnapVStep")]
    public float GridSnapVStep { get; set; }

    [JsonPropertyName("GridSnapHOffset")]
    public float GridSnapHOffset { get; set; }

    [JsonPropertyName("GridSnapVOffset")]
    public float GridSnapVOffset { get; set; }

    [JsonPropertyName("FlyVStep")]
    public float FlyVStep { get; set; }

    [JsonPropertyName("FlyVOffset")]
    public float FlyVOffset { get; set; }

    [JsonPropertyName("PivotSnapDistance")]
    public float PivotSnapDistance { get; set; } = 0;

    [JsonPropertyName("PivotPositions")]
    public List<Vec3>? PivotPositions { get; set; } = [];

    [JsonPropertyName("PivotRotations")]
    public List<Quat>? PivotRotations { get; set; } = [];

    [JsonPropertyName("MagnetPositions")]
    public List<Vec3> MagnetPositions { get; set; } = [];
    
    [JsonPropertyName("MagnetRotations")]
    public List<Quat> MagnetRotations { get; set; } = [];


    [JsonPropertyName("PlacementClass")]
    public PlacementClass? PlacementClass { get; set; }


    public static CGameItemPlacementParam ToPlacementParam(PlacementConfig? placementConfig)
    {
        var placementParamsTemplate = placementConfig?.PlacementClass == null ?
             GbxTemplateLibrary.CreatePlacementParamTemplate() :
             GbxTemplateLibrary.CreatePlacementParamTemplateWithPlacementClass();

        if (placementConfig != null)
        {
            var placementParams = placementParamsTemplate.Value;

            placementParams.YawOnly = placementConfig.YawOnly;
            placementParams.NotOnObject = placementConfig.NotOnObject;
            placementParams.AutoRotation = placementConfig.AutoRotation;
            placementParams.SwitchPivotManually = placementConfig.SwitchPivotManually;
            placementParams.CubeCenter = placementConfig.CubeCenter ?? Vec3.Zero;
            placementParams.CubeSize = placementConfig.CubeSize;
            placementParams.GridSnapHStep = placementConfig.GridSnapHStep;
            placementParams.GridSnapVStep = placementConfig.GridSnapVStep;
            placementParams.GridSnapHOffset = placementConfig.GridSnapHOffset;
            placementParams.GridSnapVOffset = placementConfig.GridSnapVOffset;
            placementParams.FlyVStep = placementConfig.FlyVStep;
            placementParams.FlyVOffset = placementConfig.FlyVOffset;
            placementParams.PivotSnapDistance = placementConfig.PivotSnapDistance;
            placementParams.PivotPositions = placementConfig.PivotPositions?.ToArray() ?? Array.Empty<Vec3>();
            placementParams.PivotRotations = placementConfig.PivotRotations?.ToArray() ?? Array.Empty<Quat>();
            ItemPlacementUtils.SetItemPlacementMagnets(placementParams, placementConfig.MagnetPositions.Zip(placementConfig.MagnetRotations, (pos, rot) => new MagnetLoc { Position = pos, PitchYawRoll = rot.ToPitchYawRoll() * MathUtils.Rad2Deg }).ToList());

            if (placementConfig.PlacementClass != null)
            {
                var pClass = placementParams.PlacementClass!;

                pClass.SizeGroup = placementConfig.PlacementClass.SizeGroup;
                pClass.CompatibleGroupsIds = placementConfig.PlacementClass.CompatibleGroupsIds?.Select(gid => gid.ToString()).ToArray() ?? Array.Empty<string>();
                pClass.AlwaysUp = placementConfig.PlacementClass.AlwaysUp;
                pClass.AlignToInterior = placementConfig.PlacementClass.AlignToInterior;
                pClass.AlignToWorldDir = placementConfig.PlacementClass.AlignToWorldDir;
                pClass.WorldDir = placementConfig.PlacementClass.WorldDir ?? new Vec3(0, 0, 1);
                pClass.GroupCurPatchLayouts = placementConfig.PlacementClass.GroupCurPatchLayouts?.ToArray() ?? Array.Empty<int>();
                pClass.PatchLayouts = placementConfig.PlacementClass.PatchLayouts?.
                    Select(pl =>
                    {
                        var plc = new NPlugItemPlacement_SClass.PatchLayout
                        {
                            ItemCount = pl.ItemCount,
                            ItemSpacing = pl.ItemSpacing,
                            FillAlign = (int)pl.FillAlign,
                            FillDir = (int)pl.FillDir,
                            U01 = pl.FillOffsetV,
                            U02 = pl.FillOffsetU,
                            NormedPos = pl.NormedPos,
                            OnlyOnGroups = pl.OnlyOnGroups?.ToArray() ?? Array.Empty<string>(),
                            Altitude = pl.Altitude
                        };
                        return plc;
                    }).ToArray() ?? Array.Empty<NPlugItemPlacement_SClass.PatchLayout>();

            }
        }
        return placementParamsTemplate;
    }
    public static PlacementConfig FromPlacementParam(CGameItemPlacementParam placementParam)
    {
        var placementConfig = new PlacementConfig
        {
            YawOnly = placementParam.YawOnly,
            NotOnObject = placementParam.NotOnObject,
            AutoRotation = placementParam.AutoRotation,
            SwitchPivotManually = placementParam.SwitchPivotManually,
            CubeCenter = placementParam.CubeCenter,
            CubeSize = placementParam.CubeSize,
            GridSnapHStep = placementParam.GridSnapHStep,
            GridSnapVStep = placementParam.GridSnapVStep,
            GridSnapHOffset = placementParam.GridSnapHOffset,
            GridSnapVOffset = placementParam.GridSnapVOffset,
            FlyVStep = placementParam.FlyVStep,
            FlyVOffset = placementParam.FlyVOffset,
            PivotSnapDistance = placementParam.PivotSnapDistance,
            PivotPositions = placementParam.PivotPositions?.ToList() ?? new List<Vec3>(),
            PivotRotations = placementParam.PivotRotations?.ToList() ?? new List<Quat>(),
            MagnetPositions = ItemPlacementUtils.GetItemPlacementMagnets(placementParam).Select(m => m.Position).ToList(),
            MagnetRotations = ItemPlacementUtils.GetItemPlacementMagnets(placementParam).Select(m => Quaternion.CreateFromYawPitchRollDegrees(m.PitchYawRoll.Y, m.PitchYawRoll.X, m.PitchYawRoll.Z).ToQuat()).ToList(),
        };
        if (placementParam.PlacementClass != null)
        {
            var pClass = new PlacementClass
            {
                SizeGroup = placementParam.PlacementClass.SizeGroup,
                CompatibleGroupsIds = placementParam.PlacementClass.CompatibleGroupsIds?.ToList() ?? new List<string>(),
                AlwaysUp = placementParam.PlacementClass.AlwaysUp,
                AlignToInterior = placementParam.PlacementClass.AlignToInterior,
                AlignToWorldDir = placementParam.PlacementClass.AlignToWorldDir,
                WorldDir = placementParam.PlacementClass.WorldDir,
                GroupCurPatchLayouts = placementParam.PlacementClass.GroupCurPatchLayouts?.ToList() ?? new List<int>(),
                PatchLayouts = placementParam.PlacementClass.PatchLayouts?.Select(pl =>
                {
                    return new PlacementPatchLayout
                    {
                        ItemCount = pl.ItemCount,
                        ItemSpacing = pl.ItemSpacing,
                        FillAlign = (FillAlign)pl.FillAlign,
                        FillDir = (FillDir)pl.FillDir,
                        FillOffsetU = pl.U02,
                        FillOffsetV = pl.U01,
                        NormedPos = pl.NormedPos,
                        OnlyOnGroups = pl.OnlyOnGroups?.ToList() ?? new List<string>(),
                        Altitude = pl.Altitude
                    };
                }).ToList() ?? new List<PlacementPatchLayout>(),
            };
            placementConfig.PlacementClass = pClass;
        }
        return placementConfig;
    }
}


public class PlacementClass
{
    [JsonPropertyName("SizeGroup")]
    public string? SizeGroup { get; set; } = null;

    [JsonPropertyName("CompatibleGroupsIds")]
    public List<string>? CompatibleGroupsIds { get; set; } = [];

    [JsonPropertyName("AlwaysUp")]
    public bool AlwaysUp { get; set; } = false;

    [JsonPropertyName("AlignToInterior")]
    public bool AlignToInterior { get; set; } = false;

    [JsonPropertyName("AlignToWorldDir")]
    public bool AlignToWorldDir { get; set; } = false;

    [JsonPropertyName("WorldDir")]
    public Vec3? WorldDir { get; set; } = null;

    [JsonPropertyName("PatchLayouts")]
    public List<PlacementPatchLayout>? PatchLayouts { get; set; } = [];

    [JsonPropertyName("GroupCurPatchLayouts")]
    public List<int>? GroupCurPatchLayouts { get; set; } = [];
}

public class PlacementPatchLayout
{
    /// <summary>0 for automatic count depending on spacing</summary>
    [JsonPropertyName("ItemCount")]
    public int ItemCount { get; set; }

    /// <summary>Space between items</summary>
    [JsonPropertyName("ItemSpacing")]
    public float ItemSpacing { get; set; }

    [JsonPropertyName("FillAlign")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public FillAlign FillAlign { get; set; } = FillAlign.Center;


    [JsonPropertyName("FillDir")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public FillDir FillDir { get; set; } = FillDir.U;

    /// <summary>
    /// Horizontal offset for fill alignment
    /// </summary>
    [JsonPropertyName("FillOffsetU")]
    public float FillOffsetU { get; set; } = 0;

    /// <summary>
    /// Offset for fill alignment in back/front direction (perpendicular to patch plane)
    /// </summary>
    [JsonPropertyName("FillOffsetV")]
    public float FillOffsetV { get; set; } = 0;


    /// <summary>Horizontal position along patch width</summary>
    [JsonPropertyName("NormedPos")]
    public float NormedPos { get; set; } = 0.5f;



    /// <summary>For which patch groups this config applies</summary>
    [JsonPropertyName("OnlyOnGroups")]
    public List<string>? OnlyOnGroups { get; set; } = [];

    /// <summary>Height over patch</summary>
    [JsonPropertyName("Altitude")]
    public float Altitude { get; set; } = 0;
}

//
// Converter
//

public class Vec3Converter : JsonConverter<Vec3>
{
    public override Vec3 Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        var parts = reader.GetString()!.Split(' ');

        return new Vec3(
            float.Parse(parts[0], CultureInfo.InvariantCulture),
            float.Parse(parts[1], CultureInfo.InvariantCulture),
            float.Parse(parts[2], CultureInfo.InvariantCulture)
        );
    }

    public override void Write(Utf8JsonWriter writer, Vec3 value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(
            $"{value.X.ToString(CultureInfo.InvariantCulture)} " +
            $"{value.Y.ToString(CultureInfo.InvariantCulture)} " +
            $"{value.Z.ToString(CultureInfo.InvariantCulture)}"
        );
    }
}

public sealed class ColorJsonConverter : JsonConverter<System.Drawing.Color>
{
    public override System.Drawing.Color Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        var value = reader.GetString();

        return value != null
            ? ColorUtils.FromRGBHex(value)
            : System.Drawing.Color.Black;
    }

    public override void Write(
        Utf8JsonWriter writer,
        System.Drawing.Color value,
        JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToRGBHex());
    }
}

public sealed class PlacementParameterConverter : JsonConverter<CGameItemPlacementParam>
{
    public override CGameItemPlacementParam? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        JsonElement root = document.RootElement;

        var value = new CGameItemPlacementParam
        {
            YawOnly = root.TryGetProperty("YawOnly", out var yawOnly)
                && yawOnly.GetBoolean(),

            NotOnObject = root.TryGetProperty("NotOnObject", out var notOnObject)
                && notOnObject.GetBoolean(),

            AutoRotation = root.TryGetProperty("AutoRotation", out var autoRotation)
                && autoRotation.GetBoolean(),

            SwitchPivotManually = root.TryGetProperty("SwitchPivotManually", out var switchPivotManually)
                && switchPivotManually.GetBoolean(),

            CubeSize = root.GetProperty("CubeSize").GetSingle(),
            GridSnapHStep = root.GetProperty("GridSnapHStep").GetSingle(),
            GridSnapVStep = root.GetProperty("GridSnapVStep").GetSingle(),
            GridSnapHOffset = root.GetProperty("GridSnapHOffset").GetSingle(),
            GridSnapVOffset = root.GetProperty("GridSnapVOffset").GetSingle(),
            FlyVStep = root.GetProperty("FlyVStep").GetSingle(),
            FlyVOffset = root.GetProperty("FlyVOffset").GetSingle(),
            PivotSnapDistance = root.GetProperty("PivotSnapDistance").GetSingle()
        };

        if (root.TryGetProperty("CubeCenter", out var cubeCenter))
        {
            value.CubeCenter =
                cubeCenter.Deserialize<Vec3>(options);
        }

        if (root.TryGetProperty("PivotPositions", out var pivotPositions))
        {
            value.PivotPositions =
                pivotPositions.Deserialize<Vec3[]>(options);
        }

        if (root.TryGetProperty("PivotRotations", out var pivotRotations))
        {
            value.PivotRotations =
                pivotRotations.Deserialize<Quat[]>(options);
        }

        if (root.TryGetProperty("PlacementClass", out var placementClass))
        {
            value.PlacementClass =
                placementClass.Deserialize<NPlugItemPlacement_SClass>(options);
        }

        return value;
    }

    public override void Write(Utf8JsonWriter writer, CGameItemPlacementParam value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        writer.WriteBoolean("YawOnly", value.YawOnly);
        writer.WriteBoolean("NotOnObject", value.NotOnObject);
        writer.WriteBoolean("AutoRotation", value.AutoRotation);
        writer.WriteBoolean("SwitchPivotManually", value.SwitchPivotManually);

        writer.WritePropertyName("CubeCenter");
        JsonSerializer.Serialize(writer, value.CubeCenter, options);

        writer.WriteNumber("CubeSize", value.CubeSize);
        writer.WriteNumber("GridSnapHStep", value.GridSnapHStep);
        writer.WriteNumber("GridSnapVStep", value.GridSnapVStep);
        writer.WriteNumber("GridSnapHOffset", value.GridSnapHOffset);
        writer.WriteNumber("GridSnapVOffset", value.GridSnapVOffset);
        writer.WriteNumber("FlyVStep", value.FlyVStep);
        writer.WriteNumber("FlyVOffset", value.FlyVOffset);
        writer.WriteNumber("PivotSnapDistance", value.PivotSnapDistance);

        writer.WritePropertyName("PivotPositions");
        JsonSerializer.Serialize(writer, value.PivotPositions, options);

        writer.WritePropertyName("PivotRotations");
        JsonSerializer.Serialize(writer, value.PivotRotations, options);

        writer.WritePropertyName("PlacementClass");
        JsonSerializer.Serialize(writer, value.PlacementClass, options);

        writer.WriteEndObject();
    }
}
public sealed class PatchLayoutConverter : JsonConverter<NPlugItemPlacement_SClass.PatchLayout>
{
    public override void Write(
        Utf8JsonWriter writer,
        NPlugItemPlacement_SClass.PatchLayout value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        writer.WriteNumber("ItemCount", value.ItemCount);
        writer.WriteNumber("ItemSpacing", value.ItemSpacing);
        writer.WriteNumber("FillAlign", value.FillAlign);
        writer.WriteNumber("FillDir", value.FillDir);
        writer.WriteNumber("NormedPos", value.NormedPos);
        writer.WriteNumber("U01", value.U01);

        writer.WritePropertyName("OnlyOnGroups");
        JsonSerializer.Serialize(writer, value.OnlyOnGroups, options);

        writer.WriteNumber("Altitude", value.Altitude);
        writer.WriteNumber("U02", value.U02);

        writer.WriteEndObject();
    }

    public override NPlugItemPlacement_SClass.PatchLayout Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        JsonElement root = document.RootElement;

        var value = new NPlugItemPlacement_SClass.PatchLayout
        {
            ItemCount = root.GetProperty("ItemCount").GetInt32(),
            ItemSpacing = root.GetProperty("ItemSpacing").GetSingle(),
            FillAlign = root.GetProperty("FillAlign").GetInt32(),
            FillDir = root.GetProperty("FillDir").GetInt32(),
            NormedPos = root.GetProperty("NormedPos").GetSingle(),
            U01 = root.GetProperty("U01").GetSingle(),
            Altitude = root.GetProperty("Altitude").GetSingle(),
            U02 = root.GetProperty("U02").GetSingle()
        };

        if (root.TryGetProperty("OnlyOnGroups", out var onlyOnGroups))
        {
            value.OnlyOnGroups =
                onlyOnGroups.Deserialize<string[]>(options);
        }

        return value;
    }
}
public sealed class NPlugItemPlacement_SClassConverter
    : JsonConverter<NPlugItemPlacement_SClass>
{
    public override void Write(
        Utf8JsonWriter writer,
        NPlugItemPlacement_SClass value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        writer.WritePropertyName("SizeGroup");
        writer.WriteStringValue(value.SizeGroup);

        writer.WritePropertyName("CompatibleGroupsIds");
        JsonSerializer.Serialize(writer, value.CompatibleGroupsIds, options);

        writer.WriteBoolean("AlwaysUp", value.AlwaysUp);
        writer.WriteBoolean("AlignToInterior", value.AlignToInterior);
        writer.WriteBoolean("AlignToWorldDir", value.AlignToWorldDir);

        writer.WritePropertyName("WorldDir");
        JsonSerializer.Serialize(writer, value.WorldDir, options);

        writer.WritePropertyName("PatchLayouts");
        JsonSerializer.Serialize(writer, value.PatchLayouts, options);

        writer.WritePropertyName("GroupCurPatchLayouts");
        JsonSerializer.Serialize(writer, value.GroupCurPatchLayouts, options);

        writer.WriteEndObject();
    }

    public override NPlugItemPlacement_SClass Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        JsonElement root = document.RootElement;

        var value = new NPlugItemPlacement_SClass
        {
            SizeGroup =
                root.TryGetProperty("SizeGroup", out var sizeGroup)
                    ? sizeGroup.GetString()
                    : null,

            CompatibleGroupsIds =
                root.TryGetProperty("CompatibleGroupsIds", out var compatibleGroupsIds)
                    ? compatibleGroupsIds.Deserialize<string[]>(options)
                    : null,

            AlwaysUp =
                root.TryGetProperty("AlwaysUp", out var alwaysUp)
                    && alwaysUp.GetBoolean(),

            AlignToInterior =
                root.TryGetProperty("AlignToInterior", out var alignToInterior)
                    && alignToInterior.GetBoolean(),

            AlignToWorldDir =
                root.TryGetProperty("AlignToWorldDir", out var alignToWorldDir)
                    && alignToWorldDir.GetBoolean(),

            WorldDir =
                root.TryGetProperty("WorldDir", out var worldDir)
                    ? worldDir.Deserialize<Vec3>(options)
                    : default,

            PatchLayouts =
                root.TryGetProperty("PatchLayouts", out var patchLayouts)
                    ? patchLayouts.Deserialize<
                        NPlugItemPlacement_SClass.PatchLayout[]>(options)
                    : null,

            GroupCurPatchLayouts =
                root.TryGetProperty("GroupCurPatchLayouts", out var groupCurPatchLayouts)
                    ? groupCurPatchLayouts.Deserialize<int[]>(options)
                    : null
        };

        return value;
    }
}

public sealed class NPlugDyna_SKinematicConstraintConverter
    : JsonConverter<NPlugDyna_SKinematicConstraint>
{
    public override void Write(
        Utf8JsonWriter writer,
        NPlugDyna_SKinematicConstraint value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        writer.WriteNumber("SubVersion", value.SubVersion);

        writer.WritePropertyName("TransAnimFunc");
        JsonSerializer.Serialize(writer, value.TransAnimFunc, options);

        writer.WritePropertyName("RotAnimFunc");
        JsonSerializer.Serialize(writer, value.RotAnimFunc, options);

        writer.WriteString("ShaderTcType", value.ShaderTcType.ToString());

        writer.WriteNumber("ShaderTcVersion", value.ShaderTcVersion);

        writer.WritePropertyName("ShaderTcAnimFunc");
        JsonSerializer.Serialize(writer, value.ShaderTcAnimFunc, options);

        writer.WritePropertyName("ShaderTcDataTransSub");
        JsonSerializer.Serialize(writer, value.ShaderTcDataTransSub, options);

        writer.WriteString("TransAxis", value.TransAxis.ToString());
        writer.WriteNumber("TransMin", value.TransMin);
        writer.WriteNumber("TransMax", value.TransMax);

        writer.WriteString("RotAxis", value.RotAxis.ToString());
        writer.WriteNumber("AngleMinDeg", value.AngleMinDeg);
        writer.WriteNumber("AngleMaxDeg", value.AngleMaxDeg);

        writer.WriteEndObject();
    }

    public override NPlugDyna_SKinematicConstraint Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;

        var value = new NPlugDyna_SKinematicConstraint
        {
            SubVersion = root.GetProperty("SubVersion").GetInt32(),

            TransAnimFunc = Deserialize<
                NPlugDyna_SKinematicConstraint.AnimFunc>(
                root, "TransAnimFunc", options),

            RotAnimFunc = Deserialize<
                NPlugDyna_SKinematicConstraint.AnimFunc>(
                root, "RotAnimFunc", options),

            ShaderTcType = Enum.Parse<
                NPlugDyna_SKinematicConstraint.EShaderTcType>(
                root.GetProperty("ShaderTcType").GetString()!),

            ShaderTcVersion =
                root.GetProperty("ShaderTcVersion").GetInt32(),

            ShaderTcAnimFunc = Deserialize<
                NPlugDyna_SKinematicConstraint.AnimFuncNat[]>(
                root, "ShaderTcAnimFunc", options),

            ShaderTcDataTransSub = Deserialize<
                NPlugDyna_SKinematicConstraint.TransSubTextureIn>(
                root, "ShaderTcDataTransSub", options),

            TransAxis = Enum.Parse<
                NPlugDyna_SKinematicConstraint.EAxis>(
                root.GetProperty("TransAxis").GetString()!),

            TransMin =
                root.GetProperty("TransMin").GetSingle(),

            TransMax =
                root.GetProperty("TransMax").GetSingle(),

            RotAxis = Enum.Parse<
                NPlugDyna_SKinematicConstraint.EAxis>(
                root.GetProperty("RotAxis").GetString()!),

            AngleMinDeg =
                root.GetProperty("AngleMinDeg").GetSingle(),

            AngleMaxDeg =
                root.GetProperty("AngleMaxDeg").GetSingle()
        };

        return value;
    }

    private static T? Deserialize<T>(
        JsonElement root,
        string property,
        JsonSerializerOptions options)
    {
        if (!root.TryGetProperty(property, out var element) ||
            element.ValueKind == JsonValueKind.Null)
        {
            return default;
        }

        return element.Deserialize<T>(options);
    }
}


// ============================================================
// AnimFunc
// ============================================================

public sealed class AnimFuncConverter
    : JsonConverter<NPlugDyna_SKinematicConstraint.AnimFunc>
{
    public override void Write(
        Utf8JsonWriter writer,
        NPlugDyna_SKinematicConstraint.AnimFunc value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        writer.WriteBoolean("IsDuration", value.IsDuration);

        writer.WritePropertyName("SubFuncs");
        JsonSerializer.Serialize(writer, value.SubFuncs, options);

        writer.WriteEndObject();
    }

    public override NPlugDyna_SKinematicConstraint.AnimFunc Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;

        return new NPlugDyna_SKinematicConstraint.AnimFunc
        {
            IsDuration =
                root.GetProperty("IsDuration").GetBoolean(),

            SubFuncs = root.TryGetProperty("SubFuncs", out var subFuncs)
                ? subFuncs.Deserialize<
                    NPlugDyna_SKinematicConstraint.SubAnimFunc[]>(options)
                : null
        };
    }
}


// ============================================================
// SubAnimFunc
// ============================================================

public sealed class SubAnimFuncConverter
    : JsonConverter<NPlugDyna_SKinematicConstraint.SubAnimFunc>
{
    public override void Write(
        Utf8JsonWriter writer,
        NPlugDyna_SKinematicConstraint.SubAnimFunc value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        writer.WriteString("Ease", value.Ease.ToString());

        writer.WriteBoolean("Reverse", value.Reverse);

        writer.WritePropertyName("Duration");
        JsonSerializer.Serialize(writer, value.Duration, options);

        writer.WriteEndObject();
    }

    public override NPlugDyna_SKinematicConstraint.SubAnimFunc Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;

        return new NPlugDyna_SKinematicConstraint.SubAnimFunc
        {
            Ease = Enum.Parse<
                NPlugDyna_SKinematicConstraint.AnimEase>(
                root.GetProperty("Ease").GetString()!),

            Reverse =
                root.GetProperty("Reverse").GetBoolean(),

            Duration = root.TryGetProperty("Duration", out var duration)
                ? duration.Deserialize<TimeInt32>(options)
                : default
        };
    }
}


// ============================================================
// AnimFuncNat
// ============================================================

public sealed class AnimFuncNatConverter
    : JsonConverter<NPlugDyna_SKinematicConstraint.AnimFuncNat>
{
    public override void Write(
        Utf8JsonWriter writer,
        NPlugDyna_SKinematicConstraint.AnimFuncNat value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        writer.WritePropertyName("Duration");
        JsonSerializer.Serialize(writer, value.Duration, options);

        writer.WriteNumber("TextureId", value.TextureId);

        writer.WriteEndObject();
    }

    public override NPlugDyna_SKinematicConstraint.AnimFuncNat Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;

        return new NPlugDyna_SKinematicConstraint.AnimFuncNat
        {
            Duration = root.TryGetProperty("Duration", out var duration)
                ? duration.Deserialize<TimeInt32>(options)
                : default,

            TextureId =
                root.GetProperty("TextureId").GetInt32()
        };
    }
}


// ============================================================
// TransSubTextureIn
// ============================================================

public sealed class TransSubTextureInConverter
    : JsonConverter<NPlugDyna_SKinematicConstraint.TransSubTextureIn>
{
    public override void Write(
        Utf8JsonWriter writer,
        NPlugDyna_SKinematicConstraint.TransSubTextureIn value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        writer.WriteNumber("NbSubTexture", value.NbSubTexture);
        writer.WriteNumber("NbSubTexturePerLine", value.NbSubTexturePerLine);
        writer.WriteNumber("NbSubTexturePerColumn", value.NbSubTexturePerColumn);
        writer.WriteBoolean("TopToBottom", value.TopToBottom);

        writer.WriteEndObject();
    }

    public override NPlugDyna_SKinematicConstraint.TransSubTextureIn Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;

        return new NPlugDyna_SKinematicConstraint.TransSubTextureIn
        {
            NbSubTexture =
                root.GetProperty("NbSubTexture").GetInt32(),

            NbSubTexturePerLine =
                root.GetProperty("NbSubTexturePerLine").GetInt32(),

            NbSubTexturePerColumn =
                root.GetProperty("NbSubTexturePerColumn").GetInt32(),

            TopToBottom =
                root.GetProperty("TopToBottom").GetBoolean()
        };
    }
}

public sealed class NPlugDynaObjectModel_SInstanceParamsConverter
    : JsonConverter<NPlugDynaObjectModel_SInstanceParams>
{
    public override void Write(
        Utf8JsonWriter writer,
        NPlugDynaObjectModel_SInstanceParams value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        writer.WriteNumber("PeriodSc", value.PeriodSc);
        writer.WriteNumber("TextureId", value.TextureId);
        writer.WriteBoolean("IsKinematic", value.IsKinematic);
        writer.WriteNumber("PeriodScMax", value.PeriodScMax);
        writer.WriteNumber("Phase01", value.Phase01);
        writer.WriteNumber("Phase01Max", value.Phase01Max);
        writer.WriteBoolean("CastStaticShadow", value.CastStaticShadow);

        writer.WriteEndObject();
    }

    public override NPlugDynaObjectModel_SInstanceParams Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;

        var value = new NPlugDynaObjectModel_SInstanceParams
        {
            PeriodSc =
                root.GetProperty("PeriodSc").GetSingle(),

            TextureId =
                root.GetProperty("TextureId").GetInt32(),

            IsKinematic =
                root.GetProperty("IsKinematic").GetBoolean(),

            PeriodScMax =
                root.TryGetProperty("PeriodScMax", out var periodScMax)
                    ? periodScMax.GetSingle()
                    : 0f,

            Phase01 =
                root.TryGetProperty("Phase01", out var phase01)
                    ? phase01.GetSingle()
                    : 0f,

            Phase01Max =
                root.TryGetProperty("Phase01Max", out var phase01Max)
                    ? phase01Max.GetSingle()
                    : 0f,

            CastStaticShadow =
                root.TryGetProperty("CastStaticShadow", out var castStaticShadow)
                    && castStaticShadow.GetBoolean()
        };

        return value;
    }
}