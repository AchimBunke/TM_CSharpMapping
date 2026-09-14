using GBX.NET.Engines.Plug;
using System.Numerics;
using TM_GenericMapping.Items.FbxGbxConversion.Importing;
using TM_GenericMapping.Items.FbxGbxConversion.Serialization;
using TM_GenericMapping.Items.MeshCompilation;
using TM_GenericMapping.Messaging;
using Quaternion = System.Numerics.Quaternion;

namespace TM_GenericMapping.Items.FbxGbxConversion;

internal class LightDefV3
{
    public required NormalizedLightV3 Light { get; set; }
    public required LightConfig LightConfig { get; set; }
    public Vector3 Position { get; set; }
    public Quaternion Rotation { get; set; }
    public int GroupIndex { get; set; } = -1;
}

internal class FbxLightConverterV3
{
    public static ToolResult<List<LightDefV3>> ExtractLights(ImportedScene scene, FbxGbxConversionInput config)
    {
        List<LightDefV3> lights = new List<LightDefV3>();

        foreach (var light in scene.Lights)
        {
            if (!TryFindConfigForLight(light.NodeName, config, out var lightConfig))
                return ToolResult.Fail(nameof(FbxLightConverterV3), ErrorCodes.FbxGbxConverter.MissingLightConfig, light.NodeName);

            var (normalizedLight, position, rotation) = ConvertLight(light, lightConfig!, config.ItemConfig.Scale);
            lights.Add(new LightDefV3 { Light = normalizedLight, LightConfig = lightConfig!, Position = position, Rotation = rotation });
        }
        return ToolResult.Success(lights, nameof(FbxLightConverterV3));
    }

    static (NormalizedLightV3 Light, Vector3 Position, Quaternion Rotation) ConvertLight(ImportedLight light, LightConfig lightConfig, float scale)
    {
        var normalizedLight = new NormalizedLightV3();
        MeshCompilation.LightType lightType = light.Type switch
        {
            ImportedLightType.Directional => MeshCompilation.LightType.Point,
            ImportedLightType.Point => MeshCompilation.LightType.Point,
            ImportedLightType.Spot => MeshCompilation.LightType.Spot,
            _ => MeshCompilation.LightType.Point,
        };
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
        translation *= scale;

        var localDirection = light.Direction;

        var directionQ = FromTo(-Vector3.UnitZ, localDirection);

        var rotation = nodeRotation * directionQ;
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
