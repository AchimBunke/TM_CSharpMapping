using Assimp;
using Assimp.Configs;
using TM_GenericMapping.Items.FbxGbxConverter;
using TM_GenericMapping.Messaging;
using static GBX.NET.Engines.Game.CGameCtnCollection;

namespace TM_GenericMapping.Items.FbxGbxConversion;

internal static class FbxSceneReader
{
    public static ToolResult<Scene> ParseFbx(Stream fbxStream)
    {
        using var context = new AssimpContext();
        context.SetConfig(new FloatPropertyConfig("AI_CONFIG_GLOBAL_SCALE_FACTOR_KEY", 1f));
        try
        {
            if (fbxStream.CanSeek)
                fbxStream.Seek(0, SeekOrigin.Begin);

            //
            var scene = context.ImportFileFromStream(fbxStream,
                //PostProcessSteps.Triangulate |
                //PostProcessSteps.JoinIdenticalVertices
                PostProcessSteps.CalculateTangentSpace
                //PostProcessSteps.OptimizeMeshes
                //PostProcessSteps.GenerateNormals

                //PostProcessSteps.FlipWindingOrder
                //PostProcessSteps.ValidateDataStructure
                );


            return ToolResult.Success(scene, nameof(FbxSceneReader));
        }
        catch (Exception e)
        {
            return ToolResult.Fail<Scene>(nameof(FbxSceneReader), ErrorCodes.FbxGbxConverter.FbxParsingError, e);
        }
    }

    public static Assimp.Matrix4x4 GetGlobalTransform(Node node)
    {
        Assimp.Matrix4x4 transform = node.Transform;
        Node current = node.Parent;

        while (current != null)
        {
            transform = transform * current.Transform;
            current = current.Parent;
        }

        // apply cm -> m conversion exactly once, after the full hierarchy is combined
        var unitScale = Assimp.Matrix4x4.FromScaling(new Vector3D(0.01f, 0.01f, 0.01f));
        transform = transform * unitScale;

        return transform;
    }

    public static List<(Node node, string NodeName, Assimp.Matrix4x4 GlobalTransform)> CollectNodes(Scene scene, Node node)
    {
        var result = new List<(Node, string, Assimp.Matrix4x4)>
        {
            (node, node.Name, GetGlobalTransform(node))
        };

        foreach (var child in node.Children)
            result.AddRange(CollectNodes(scene, child));

        return result;
    }
    public static List<(Mesh Mesh, string NodeName, Assimp.Matrix4x4 GlobalTransform)> GetMeshInstances(Scene scene, List<(Node node, string NodeName, Assimp.Matrix4x4 GlobalTransform)> nodes)
    {
        var result = new List<(Mesh, string, Assimp.Matrix4x4)>();

        foreach (var (node, name, transform) in nodes)
        {
            if (node.MeshCount == 0)
                continue;

            foreach (int meshIndex in node.MeshIndices)
            {
                var mesh = scene.Meshes[meshIndex];
                result.Add((mesh, name, transform));
            }
        }

        return result;
    }

    public static List<(Assimp.Light Light, string NodeName, Assimp.Matrix4x4 GlobalTransform)> GetLightInstances(Scene scene, List<(Node node, string NodeName, Assimp.Matrix4x4 GlobalTransform)> nodes)
    {
        var result = new List<(Assimp.Light, string, Assimp.Matrix4x4)>();
        if (!scene.HasLights)
            return result;

        foreach (var (node, name, transform) in nodes)
        {
            Light light;
            if ((light = scene.Lights.FirstOrDefault(l => l.Name == node.Name)) == null)
                continue;
            result.Add((light, node.Name, transform));
        }
        return result;
    }
}
