using Assimp;
using GBX.NET.Engines.Plug;
using System.Numerics;
using TM_GenericMapping.Items.FbxGbxConversion.Serialization;
using TM_GenericMapping.Messaging;
using Quaternion = System.Numerics.Quaternion;

namespace TM_GenericMapping.Items.FbxGbxConversion;

internal class LightDef
{
    public NormalizedLight Light { get; set; }
    public LightConfig LightConfig { get; set; }
}
internal class FbxLightConverter
{
    public static ToolResult<List<LightDef>> ExtractLights(Scene scene, FbxGbxConversionInput config)
    {
        List<LightDef> lights = new List<LightDef>();

        var nodes = FbxSceneReader.CollectNodes(scene, scene.RootNode);
        var lightInstances = FbxSceneReader.GetLightInstances(scene, nodes);

        foreach (var (light, nodeName, globalTransform) in lightInstances)
        {
            if (!TryFindConfigForLight(light.Name, config, out var lightConfig))
                return ToolResult.Fail(nameof(FbxLightConverter), ErrorCodes.FbxGbxConverter.MissingLightConfig, light.Name);

            var normalizedLight = ConvertLight(light, lightConfig!, globalTransform, config.ItemConfig.Scale);
            lights.Add(new LightDef { Light = normalizedLight, LightConfig = lightConfig! });
        }
        return ToolResult.Success(lights, nameof(FbxLightConverter));
    }
    static NormalizedLight ConvertLight(Assimp.Light light, LightConfig lightConfig, Assimp.Matrix4x4 globalTransform, float scale)
    {
        var normalizedLight = new NormalizedLight();
        LightType lightType = light.LightType switch
        {
            Assimp.LightSourceType.Directional => LightType.Point,
            Assimp.LightSourceType.Point => LightType.Point,
            Assimp.LightSourceType.Spot => LightType.Spot,
            Assimp.LightSourceType.Area => LightType.Area,
            _ => LightType.Point,
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
        normalizedLight.Type = lightType;

        var convertedTransform = FbxMeshConverter.CoordinateConversionMatrix * globalTransform;

        convertedTransform.Decompose(out _, out var nodeRotation, out var translation);
        translation *= scale;

        var nodeQ = new System.Numerics.Quaternion(
            nodeRotation.X,
            nodeRotation.Y,
            nodeRotation.Z,
            nodeRotation.W);
        var localDirection = new Vector3(
            light.Direction.X,
            light.Direction.Y,
            light.Direction.Z);

        var directionQ = FromTo(
            -Vector3.UnitZ,
            localDirection);

        normalizedLight.Rotation = nodeQ * directionQ;
        normalizedLight.Position = new System.Numerics.Vector3(translation.X, translation.Y, translation.Z);

        return normalizedLight;
    }

    static bool TryFindConfigForLight(string lightName, FbxGbxConversionInput config, out LightConfig? lightConfig)
    {
        lightConfig = config.ItemConfig.Lights.FirstOrDefault(l => l.Name == lightName, null);
        return lightConfig is not null;
    }

    public static void GroupLights(List<LightDef> lights, List<MeshGroup> meshGroups)
    {
        // TODO: Implement grouping logic based on meshGroups and l
        foreach(var l in lights)
        {
            l.Light.GroupIndex = 0;
        }
    }
    static Quaternion FromTo(Vector3 from, Vector3 to)
    {
        from = Vector3.Normalize(from);
        to = Vector3.Normalize(to);

        float dot = Vector3.Dot(from, to);

        if (dot > 0.999999f)
            return Quaternion.Identity;

        if (dot < -0.999999f)
            return Quaternion.CreateFromAxisAngle(
                Vector3.UnitY,
                MathF.PI);

        Vector3 axis = Vector3.Normalize(
            Vector3.Cross(from, to));

        float angle = MathF.Acos(dot);

        return Quaternion.CreateFromAxisAngle(axis, angle);
    }
}
