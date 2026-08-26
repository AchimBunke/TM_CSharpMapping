namespace TM_GenericMapping.Items.FbxGbxConversion.Serialization;

using GBX.NET;
using GBX.NET.Engines.Game;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.Plug;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using TM_GenericMapping.Common;
using TM_GenericMapping.Templating;
using TmEssentials;
using static GBX.NET.Engines.GameData.CGameItemModel;
using static GBX.NET.Engines.Meta.NPlugDyna_SKinematicConstraint;

[Flags]
public enum ItemConversionOptions
{
    None = 0,
    MeshConfigFromObjectNames = 1 << 0,
    IgnoreMeshesWithInvalidMaterials = 1 << 1,
}
public class ItemConfig
{
    public string Type { get; init; } = "StaticObject";
    public string Collection { get; init; } = "Stadium";
    public string AuthorName { get; set; } = "";

    public string? Name { get; set; } = null;
    public string? Description { get; set; } = null;


    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Waypoint? Waypoint
    {
        get; set;
    }


    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PlacementConfig? PlacementParams
    {
        get; set;
    }

    public float Scale { get; set; } = 1.0f;

    public List<MaterialConfig> MaterialConfiguration { get; set; } = new List<MaterialConfig>();

    public List<MeshConfig> MeshConfiguration { get; set; } = new List<MeshConfig>();

    public List<LightConfig> Lights { get; set; } = new List<LightConfig>();

    public List<MovingGroupConfig> MovingGroups { get; set; } = new List<MovingGroupConfig>();


    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LodParameters? LodParameters
    {
        get; set;
    }

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
    public EWaypointType Type { get; set; } = EWaypointType.Checkpoint;
    public bool NoRespawn
    {
        get; set;
    }
    public Vec3? DefaultGravitySpawn { get; set; } = new Vec3(0, -1, 0);
    public float? TorqueX { get; set; } = 0;
    public int? TorqueDuration { get; set; } = 0;
}


public class LodParameters
{
    /// <summary>
    /// Mismatch between distance value and actual ingame distance (value = 100 => 200 units ingame)
    /// </summary>
    public List<float> MaxLodDistances { get; set; } = [];
}

public class MovingGroupConfig
{
    public string MovingGroupId
    {
        get; set;
    } = string.Empty;
    public string? ParentMovingGroupId { get; set; } = null;
    /// <summary>
    /// Trackmania space so blenderSpace => (X, Z, -Y)
    /// </summary>
    public Vec3? AnchorPosition { get; set; } = null;
    public KinematicMovement KinematicMovement
    {
        get; set;
    } = new KinematicMovement();
    public KinematicModelConfig KinematicModelConfig
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
    public static NPlugDyna_SKinematicConstraint ToKinematicConstaraint(KinematicMovement kinematicMovementConfig)
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
                NbSubTexturePerLine = kinematicMovementConfig.MovingTexture?.SubTexturePerLine ?? 0,
                NbSubTexturePerColumn = kinematicMovementConfig.MovingTexture?.SubTexturePerColumn ?? 0,
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
}
public class KinematicMovement
{
    public EAxis TransAxis
    {
        get; set;
    }
    public float TransMin
    {
        get; set;
    }
    public float TransMax
    {
        get; set;
    }
    public EAxis RotAxis
    {
        get; set;
    }
    public float AngleMinDeg
    {
        get; set;
    }
    public float AngleMaxDeg
    {
        get; set;
    }

  
    public List<SubAnimFunc> TranslationAnims { get; set; } = [];
    public List<SubAnimFunc> RotationAnims { get; set; } = [];

    public MovingTextureConfig? MovingTexture{ get; set; }


}

public class MovingTextureConfig
{
    /// <summary>
    /// Number of subtextures.. if anims contains texture ids that are not in the range of 0 to SubTextures-1, the last texture will be used instead. (e.g. SubTextures = 4, anims contains texture id 5, then texture id 3 will be used)
    /// </summary>
    public int SubTextures { get; set; }
    /// <summary>
    /// Number of subtextures per line in the texture atlas. (e.g. 4x4 atlas, SubTexturePerLine = 4, SubTexturePerColumn = 4)
    /// </summary>
    public int SubTexturePerLine { get; set; }
    /// <summary>
    /// Number of subtextures per column in the texture atlas. (e.g. 4x4 atlas, SubTexturePerLine = 4, SubTexturePerColumn = 4)
    /// </summary>
    public int SubTexturePerColumn { get; set; }
    /// <summary>
    /// If true, the texture indexes are counted from top to bottom, otherwise from bottom to top. 
    /// </summary>
    public bool TopToBottom { get; set; }

    public List<TextureAnim> TextureAnims { get; set; } = [];

}
public class TextureAnim
{
    public uint Duration { get; set; }
    public int TextureID { get; set; }
}

public class KinematicModelConfig
{
    public bool CastStaticShadow { get; set; }
    public bool IsKinematic { get; set; } = true;
    public float PeriodicSc { get; set; } = 1;
    public float PeriodicScMax { get; set; } = -1;
    public float Phase01 { get; set; } = -1;
    public float Phase01Max { get; set; } = -1;
    public int TextureId { get; set; } = 0;
}

public class SubAnimFunc
{
    public AnimEase Ease
    {
        get; set;
    }
    public bool Reverse
    {
        get; set;
    }
    public uint Duration
    {
        get; set;
    }
}

public class LightConfig
{
    public string Name
    {
        get; set;
    } = string.Empty;
    public LightType Type
    {
        get; set;
    }
    public System.Drawing.Color Color
    {
        get; set;
    }
    public float Intensity
    {
        get; set;
    }
    public float Distance
    {
        get; set;
    }
    public bool NightOnly
    {
        get; set;
    }
    public float PointEmissionRadius
    {
        get; set;
    }
    public float PointEmissionLength
    {
        get; set;
    }
    public float SpotInnerAngle { get; set; } = 40;
    public float SpotOuterAngle { get; set; } = 60;
    public float SpotEmissionSizeX
    {
        get; set;
    }
    public float SpotEmissionSizeY
    {
        get; set;
    }
}

public class PlacementConfig
{
    public bool YawOnly { get; set; }
    public bool NotOnObject {  get; set; }
    public bool AutoRotation
    {
        get; set;
    }
    public bool SwitchPivotManually
    {
        get; set;
    }
    public Vec3? CubeCenter
    {
        get; set;
    }
    public float CubeSize
    {
        get; set;
    }
    public float GridSnapHStep
    {
        get; set;
    }
    public float GridSnapVStep
    {
        get; set;
    }
    public float GridSnapHOffset
    {
        get; set;
    }
    public float GridSnapVOffset
    {
        get; set;
    }
    public float FlyVStep
    {
        get; set;
    }
    public float FlyVOffset
    {
        get; set;
    }
    public float PivotSnapDistance
    {
        get; set;
    } = 0;
    public List<Vec3>? PivotPositions { get; set; } = [];
    public List<Quat>? PivotRotations { get; set; } = [];

    public PlacementClass? PlacementClass{ get; set; }

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
                        var plc = new NPlugItemPlacement_SClass.PatchLayout();
                        plc.ItemCount = pl.ItemCount;
                        plc.ItemSpacing = pl.ItemSpacing;
                        plc.FillAlign = pl.FillAlign;
                        plc.FillDir = pl.FillDir;
                        plc.NormedPos = pl.NormedPos;
                        plc.OnlyOnGroups = pl.OnlyOnGroups?.Select(gid => gid.ToString()).ToArray() ?? Array.Empty<string>();
                        plc.Altitude = pl.Altitude;
                        return plc;
                    }).ToArray() ?? Array.Empty<NPlugItemPlacement_SClass.PatchLayout>();

            }
        }
        return placementParamsTemplate;
    }
}

public class PlacementClass
{
    public string? SizeGroup { get; set; } = null;
    public List<ItemPlacementUtils.PlacementPatchGroups>? CompatibleGroupsIds { get; set; } = [];
    public bool AlwaysUp { get; set; } = false;
    public bool AlignToInterior { get; set; } = false;
    public bool AlignToWorldDir { get; set; } = false;
    public Vec3? WorldDir { get; set; } = null;
    public List<PlacementPatchLayout>? PatchLayouts { get; set; } = [];
    public List<int>? GroupCurPatchLayouts { get; set; } = [];
}

public class PlacementPatchLayout
{
    /// <summary>
    /// 0 for automatic count depending on spacing
    /// </summary>
    public int ItemCount { get; set; }
    /// <summary>
    /// Space between items
    /// </summary>
    public float ItemSpacing { get; set; }
    public int FillAlign { get; set; }
    public int FillDir { get; set; } = 1;
    /// <summary>
    /// Horizontal Position along patch width
    /// </summary>
    public float NormedPos { get; set; } = 0.5f;
    /// <summary>
    /// For which patchgroups this config applies
    /// </summary>
    public List<ItemPlacementUtils.PlacementPatchGroups>? OnlyOnGroups { get; set; } = [];
    /// <summary>
    /// Height over patch
    /// </summary>
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