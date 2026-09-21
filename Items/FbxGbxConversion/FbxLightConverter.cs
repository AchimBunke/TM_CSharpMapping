using GBX.NET.Engines.Plug;
using System.Numerics;
using TM_GenericMapping.Common;
using TM_GenericMapping.Items.FbxGbxConversion.Importing;
using TM_GenericMapping.Items.FbxGbxConversion.Serialization;
using TM_GenericMapping.Items.MeshCompilation;
using TM_GenericMapping.Messaging;
using Quaternion = System.Numerics.Quaternion;

namespace TM_GenericMapping.Items.FbxGbxConversion;

internal class LightDef
{
    public required NormalizedLight Light { get; set; }
    public required LightConfig LightConfig { get; set; }
    public Vector3 Position { get; set; }
    public Quaternion Rotation { get; set; }
    public int GroupIndex { get; set; } = -1;
}

internal class FbxLightConverter
{
    public static ToolResult<List<LightDef>> ExtractLights(ImportedScene scene, FbxGbxConversionInput config)
    {
        List<LightDef> lights = new List<LightDef>();

        foreach (var light in scene.Lights)
        {
            if (!TryFindConfigForLight(light.NodeName, config, out var lightConfig))
                return ToolResult.Fail(nameof(FbxLightConverter), ErrorCodes.FbxGbxConverter.MissingLightConfig, light.NodeName);

            var (normalizedLight, position, rotation) = ConvertLight(light, lightConfig!);
            lights.Add(new LightDef { Light = normalizedLight, LightConfig = lightConfig!, Position = position, Rotation = rotation });
        }
        return ToolResult.Success(lights, nameof(FbxLightConverter));
    }

    static (NormalizedLight Light, Vector3 Position, Quaternion Rotation) ConvertLight(ImportedLight light, LightConfig lightConfig)
    {
        var normalizedLight = new NormalizedLight();
        MeshCompilation.LightType lightType = light.Type switch
        {
            ImportedLightType.Directional => MeshCompilation.LightType.Point,
            ImportedLightType.Point => MeshCompilation.LightType.Point,
            ImportedLightType.Spot => MeshCompilation.LightType.Spot,
            ImportedLightType.Area => MeshCompilation.LightType.Area,
            _ => MeshCompilation.LightType.Point,
        };
        if(lightConfig.Type.HasValue)
            lightType = lightConfig.Type.Value;
        var lightUserModel = new CPlugLightUserModel
        {
            Intensity = lightConfig.Intensity,
            Distance = lightConfig.Distance,
            NightOnly = lightConfig.NightOnly,
            PointEmissionRadius = lightConfig.PointEmissionRadius,
            PointEmissionLength = lightConfig.PointEmissionLength,
            SpotInnerAngle = lightConfig.SpotInnerAngle,
            SpotOuterAngle = lightConfig.SpotOuterAngle,
            SpotEmissionSizeX = lightConfig.SpotEmissionSizeX,
            SpotEmissionSizeY = lightConfig.SpotEmissionSizeY,
            Color = new GBX.NET.Vec3(lightConfig.Color.R / 255f, lightConfig.Color.G / 255f, lightConfig.Color.B / 255f)
        };
        var c = lightUserModel.CreateChunk<CPlugLightUserModel.Chunk090F9000>();
        c.Version = 1;
        c.U01 = (int)lightType;

        normalizedLight.LightModel = lightUserModel;
        normalizedLight.Name = lightConfig.Name;

        Matrix4x4.Decompose(light.GlobalTransform, out _, out var nodeRotation, out var translation);

        var rotation = nodeRotation;
        var position = translation;

        return (normalizedLight, position, rotation);
    }


    static bool TryFindConfigForLight(string lightName, FbxGbxConversionInput config, out LightConfig? lightConfig)
    {
        lightConfig = config.ItemConfig.Lights.FirstOrDefault(l => l!.Name == lightName, null);
        return lightConfig is not null;
    }

    static Quaternion FromTo(Vector3 from, Vector3 to)
    {
        from = Vector3.Normalize(from);
        to = Vector3.Normalize(to);

        float dot = Vector3.Dot(from, to);

        if (dot > 0.999999f)
            return Quaternion.Identity;

        if (dot < -0.999999f)
            return Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI);

        Vector3 axis = Vector3.Normalize(Vector3.Cross(from, to));
        float angle = MathF.Acos(dot);

        return Quaternion.CreateFromAxisAngle(axis, angle);
    }
}
