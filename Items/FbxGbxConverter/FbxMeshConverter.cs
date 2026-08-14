#define FbxGbxDebugLod
using Assimp;
using GBX.NET;
using GBX.NET.Engines.Plug;
using GBX.NET.Engines.Scene;
using System.Net.Sockets;
using System.Xml.Linq;
using TM_GenericMapping.Items.FbxGbxConversion.Serialization;
using TM_GenericMapping.Messaging;
using TmEssentials;
using static GBX.NET.Engines.Plug.CPlugSurface;

namespace TM_GenericMapping.Items.FbxGbxConversion;

internal class NodeDef
{
    public Assimp.Node Node { get; set; }
    public Assimp.Matrix4x4 GlobalTransform { get; set; }
    public MeshConfig NodeConfig { get; set; }

    public int GroupIndex { get; set; } = -1;
    public int LodMask { get; set; } = 1;

}
//internal class MeshDef
//{
//    public Assimp.Mesh AssimpMesh { get; set; }
//    public Assimp.Matrix4x4 GlobalTransform { get; set; }

//    public NormalizedMesh? Mesh { get; set; }
//    public MeshConfig MeshConfig { get; set; }

//}
internal class SocketDef
{
    public Assimp.Matrix4x4 GlobalTransform { get; set; }
    public CPlugSpawnModel? WaypointSpawnModel { get; set; }
}
internal class FbxMeshConverter
{
    public static Assimp.Matrix4x4 CoordinateConversionMatrix = new Assimp.Matrix4x4(
      1, 0, 0, 0,
      0, 1, 0, 0,
      0, 0, 1, 0,
      0, 0, 0, 1
        );

    public static NormalizedMesh ConvertMesh(Assimp.Mesh mesh, MaterialDef material, Assimp.Matrix4x4 globalTransform, MeshConfig meshConfig, float meshScale)
    {
        var normalizedMesh = new NormalizedMesh();

        bool hasNormals = mesh.HasNormals;
        if (!hasNormals) 
        {
            int i = 0;
        }
        var scaleMatrix = Assimp.Matrix4x4.FromScaling(new Vector3D(meshScale, meshScale, meshScale));
        var normalMatrix = ComputeNormalMatrix(globalTransform);
        //normalMatrix.Inverse();
        //normalMatrix.Transpose();

        bool isMirrored = GetDeterminant3x3(globalTransform) < 0f;

        normalizedMesh.Positions = TransformVectors(mesh.Vertices, globalTransform, scaleMatrix, false).ToArray();

        var indices = mesh.GetIndices();
        if (isMirrored)
            indices = FlipWinding(indices);
        normalizedMesh.Indices = indices;

        normalizedMesh.Normals = TransformVectors(mesh.Normals, normalMatrix, Assimp.Matrix4x4.Identity, true).ToArray();


        if (material.DMaterial is null || material.DMaterial.HasTexUvLayer)
        {
            int texChannelIndex = 0;
            if (mesh.TextureCoordinateChannelCount > texChannelIndex)
            {
                normalizedMesh.TexCoords = mesh.TextureCoordinateChannels[texChannelIndex].Select(tc => new Vec2(tc.X, tc.Y)).ToArray();
            }

            //normalizedMesh.TangentUs = mesh.Tangents.Select(v=>new Vec3(v.X, v.Y, v.Z)).ToArray();
            //normalizedMesh.TangentVs = mesh.BiTangents.Select(v=>new Vec3(v.X, v.Y, v.Z)).ToArray();

            normalizedMesh.TangentUs = TransformVectors(mesh.Tangents, globalTransform, Assimp.Matrix4x4.Identity, true).ToArray();
            normalizedMesh.TangentVs = TransformVectors(mesh.BiTangents, globalTransform, Assimp.Matrix4x4.Identity, true).ToArray();
        }
     
        if (material.DMaterial is null || material.DMaterial.HasLightmapUvlayer)
        {
            int lightmapChannelIndex = 1;
            if (mesh.TextureCoordinateChannelCount > lightmapChannelIndex)
            {
                normalizedMesh.LightmapCoords = mesh.TextureCoordinateChannels[lightmapChannelIndex].Select(tc => new Vec2(tc.X, tc.Y)).ToArray();
                var neg = normalizedMesh.LightmapCoords.Any(uv => uv.X < 0);
                if (neg)
                {
                    int i = 0;
                }
                normalizedMesh.PreLightGenerator = MeshBuilder.CreatePreLightGeneratorFromMeshData(normalizedMesh);
            }
        }

        if (material.DMaterial is null || material.DMaterial.HasColor0)
        {
            if (mesh.VertexColorChannelCount == 1)
                normalizedMesh.Colors = mesh.VertexColorChannels[0].Select(c =>
                new GBX.NET.Color(c.R * 255f,
                c.G * 255f,
                c.B * 255f,
                c.A * 255f).ToArgb()).ToArray();
            else
                normalizedMesh.Colors = Enumerable.Repeat(-1, mesh.VertexCount).ToArray();

        }



        normalizedMesh.Material = material.MaterialInstance;

        ApplyMeshConfig(normalizedMesh, mesh, meshConfig);


        return normalizedMesh;
    }
    public static CPlugSpawnModel ConvertSocket(Assimp.Node node, Assimp.Matrix4x4 globalTransform, MeshConfig meshConfig, float meshScale, FbxGbxConversionInput config)
    {
        var spawnModel = MeshBuilder.CreateSpawnModel();

        var scaleMatrix = Assimp.Matrix4x4.FromScaling(new Vector3D(meshScale, meshScale, meshScale));

        var convertedTransform = FbxMeshConverter.CoordinateConversionMatrix * globalTransform;
        convertedTransform.Decompose(out _, out var nodeRotation, out var translation);
        var pos = TransformVectors([translation], globalTransform, scaleMatrix, false).First();

        spawnModel.Loc = MeshBuilder.IsoFromTransform(pos, new System.Numerics.Quaternion(nodeRotation.X, nodeRotation.Y, nodeRotation.Z, nodeRotation.W));
        if(config.ItemConfig.Waypoint.TorqueX.HasValue)
            spawnModel.TorqueX = config.ItemConfig.Waypoint.TorqueX.Value;
        if (config.ItemConfig.Waypoint.DefaultGravitySpawn.HasValue)
            spawnModel.DefaultGravitySpawn = config.ItemConfig.Waypoint.DefaultGravitySpawn.Value;
        if (config.ItemConfig.Waypoint.TorqueDuration.HasValue)
            spawnModel.TorqueDuration = TimeInt32.FromMilliseconds(config.ItemConfig.Waypoint.TorqueDuration.Value);
        return spawnModel;
    }
    static void ApplyMeshConfig(NormalizedMesh normalizedMesh, Assimp.Mesh mesh, MeshConfig meshConfig)
    {
        normalizedMesh.Name = mesh.Name;

        var properties = MeshProperties.Enabled;
        var type = MeshType.Mesh;

        if (!meshConfig.MeshFlags.HasFlag(MeshFlags.NonCollidable))
            properties |= MeshProperties.Collidable;

        if (!meshConfig.MeshFlags.HasFlag(MeshFlags.Invisible))
            properties |= MeshProperties.Visible;

        if(meshConfig.MeshFlags.HasFlag(MeshFlags.TriggerWaypoint))
            type = MeshType.Trigger_Waypoint;

        if (meshConfig.MeshFlags.HasFlag(MeshFlags.TriggerEffect))
            type = MeshType.Trigger_Special;


        if (meshConfig.Lods.Count > 0)
            properties |= MeshProperties.LOD;

        normalizedMesh.Properties = properties;
        normalizedMesh.Type = type;
    }

    static IEnumerable<Vec3> TransformVectors(IEnumerable<Vector3D> vectors, Assimp.Matrix4x4 m1, Assimp.Matrix4x4 m2, bool normalize)
    {
        return vectors
                .Select(n => m1 * n)
                .Select(n => CoordinateConversionMatrix * m2 * n)
                .Select(v => normalize ? new Vec3(v.X, v.Y, v.Z).GetNormalized() : new Vec3(v.X, v.Y, v.Z));
    }

    public static ToolResult<List<NodeDef>> ExtractNodes(Scene scene, FbxGbxConversionInput config)
    {
        List<NodeDef> nodeDefs = [];
        var nodes = FbxSceneReader.CollectNodes(scene, scene.RootNode);
        foreach(var node in nodes)
        {
            var meshConfigResult = FindMeshConfigForMesh(node.NodeName, config);

            if (meshConfigResult.IsFailure)
                return ToolResult.Fail(meshConfigResult);

            var meshConfig = meshConfigResult.Value;

            if (meshConfig.MeshFlags.HasFlag(MeshFlags.Skip))
                continue;

            nodeDefs.Add(new NodeDef { Node = node.node, NodeConfig = meshConfig, GlobalTransform = node.GlobalTransform });
        }
        return ToolResult.Success(nodeDefs, nameof(FbxGbxConverter));
    }

    //public static ToolResult<List<MeshDef>> ExtractMeshes(Scene scene, List<MaterialDef> materials, FbxGbxConversionInput config)
    //{
    //    List<MeshDef> normalizedMeshes = [];

    //    var nodes = FbxSceneReader.CollectNodes(scene, scene.RootNode);
        //var meshInstances = FbxSceneReader.GetMeshInstances(scene, nodes);

    //    foreach (var (mesh, nodeName, globalTransform) in meshInstances)
    //    {
    //        var meshConfigResult = FindMeshConfigForMesh(nodeName, config);

    //        if (meshConfigResult.IsFailure)
    //            return ToolResult.Fail(meshConfigResult);

    //        var meshConfig = meshConfigResult.Value;

    //        if (meshConfig.MeshFlags.HasFlag(MeshFlags.Skip))
    //            continue;

    //        if (meshConfig.MeshFlags.HasMeshData())
    //        {
    //            var normMesh = FbxMeshConverter.ConvertMesh(mesh, materials[mesh.MaterialIndex], globalTransform, meshConfig, config.ItemConfig.Scale);

    //            normalizedMeshes.Add(new MeshDef { Mesh = normMesh, MeshConfig = meshConfig, AssimpMesh = mesh, GlobalTransform = globalTransform });
    //        }
    //        else
    //            normalizedMeshes.Add(new MeshDef { Mesh = null, MeshConfig = meshConfig, AssimpMesh = mesh, GlobalTransform = globalTransform });
    //    }
    //    return ToolResult.Success(normalizedMeshes, nameof(FbxGbxConverter));
    //}

    public static ToolResult<List<SocketDef>> ExtractSockets(Scene scene, FbxGbxConversionInput config)
    {
        List<SocketDef> socketDefs = new List<SocketDef>();

        var nodes = FbxSceneReader.CollectNodes(scene, scene.RootNode);

        foreach (var (node, nodeName, transform) in nodes)
        {
            var meshConfigResult = FindMeshConfigForMesh(nodeName, config);
            if (meshConfigResult.IsFailure)
                return ToolResult.Fail(meshConfigResult);

            var meshConfig = meshConfigResult.Value;
            if (!meshConfig.MeshFlags.HasFlag(MeshFlags.Socket))
                continue;
            var spawnModel = ConvertSocket(node, transform, meshConfig, config.ItemConfig.Scale, config);
            socketDefs.Add(new SocketDef() { GlobalTransform = transform, WaypointSpawnModel = spawnModel });
        }
        return ToolResult.Success(socketDefs, nameof(FbxGbxConverter));
    }


    static ToolResult<MeshConfig> FindMeshConfigForMesh(string meshName, FbxGbxConversionInput config)
    {
        var meshConfig = config.ItemConfig.MeshConfiguration.FirstOrDefault(m => m.Name == meshName, null);
        bool configFromMeshName = config.ItemConfig.ConversionOptions.HasFlag(ItemConversionOptions.MeshConfigFromObjectNames);
        if (meshConfig is null && !configFromMeshName)
            return ToolResult.Fail(nameof(FbxGbxConverter), ErrorCodes.FbxGbxConverter.MissingMeshConfig, meshName);

        if (config.ItemConfig.ConversionOptions.HasFlag(ItemConversionOptions.MeshConfigFromObjectNames))
        {
            meshConfig = MeshConfigFromMeshName(meshConfig, meshName, config);
        }
        return ToolResult.Success(meshConfig!, nameof(FbxGbxConverter));
    }

    static MeshConfig MeshConfigFromMeshName(MeshConfig? meshConfig, string meshName, FbxGbxConversionInput config)
    {
        const string lod0 = "_Lod0";
        const string lod1 = "_Lod1";
        const string socket = "_socket_";
        const string trigger = "_trigger_";
        const string notVisible = "_notvisible_";
        const string notCollidable = "_notcollidable_";
        const string skip = "_skip_";
        const string single = "_single_";
        // pivot handled on item config level

        meshConfig ??= new MeshConfig() { Name = meshName, MeshFlags = MeshFlags.None };

        if (meshName.Contains(lod0))
            meshConfig.Lods.Add(0);

        if (meshName.Contains(lod1))
            meshConfig.Lods.Add(1);

        if (meshName.Contains(socket))
            meshConfig.MeshFlags |= MeshFlags.Socket;

        if (meshName.Contains(trigger))
        {
            meshConfig.MeshFlags |= MeshFlags.TriggerWaypoint;
            meshConfig.MeshFlags |= MeshFlags.NonCollidable;
            meshConfig.MeshFlags |= MeshFlags.Invisible;
            meshConfig.WaypointType = config.ItemConfig.Waypoint?.Type;
        }
        if(meshName.Contains(single))
            meshConfig.MeshFlags |= MeshFlags.SingleMesh;

        if (meshName.Contains(notVisible))
            meshConfig.MeshFlags |= MeshFlags.Invisible;

        if (meshName.Contains(notCollidable))
            meshConfig.MeshFlags |= MeshFlags.NonCollidable;

        if (meshName.Contains(skip))
            meshConfig.MeshFlags |= MeshFlags.Skip;

        return meshConfig;
    }


    public static ToolResult<List<MeshGroup>> GroupNodes(List<NodeDef> nodes, List<SocketDef> sockets, FbxGbxConversionInput config)
    {
        var lods = config.ItemConfig.LodParameters?.MaxLodDistances ?? [];
#if FbxGbxDebugLod
        lods = [100,200,400];
#endif
        var grouper = new NodeGrouper(lods, config.ItemConfig);
        var calculatedGroups = grouper.Group(nodes);
        var groups = calculatedGroups.Select(g =>
        {
            g.MeshGroup.LODDistances = g.LodDistances.ToArray();
            return g.MeshGroup;
        }).ToList();

        for (int i = 0; i < groups.Count; ++i)
        {
            var group = calculatedGroups[i];
            foreach (var nodeAssignment in group.Nodes)
            {
                var nodeDef = nodeAssignment.NodeDef;
                nodeDef.GroupIndex = i;
                nodeDef.LodMask = LODUtils.LodMaskFromLods(nodeAssignment.LodIndices.ToArray());
            }
        }
        if (sockets.Count > 0) 
        {
            foreach (var group in groups)
            {
                if (group.GroupType != GroupType.Trigger_Waypoint)
                    continue;
                if (group.WaypointType == GBX.NET.Engines.GameData.CGameItemModel.EWaypointType.Finish)
                    continue;
                group.WaypointSpawnModel = sockets[0].WaypointSpawnModel;
            }
        }
        return ToolResult.Success(groups, nameof(FbxGbxConverter));
    }

    public static ToolResult<List<NormalizedMesh>> ExtractMeshes(Scene scene, List<MeshGroup> groups, List<MaterialDef> materials, List<NodeDef> nodes, FbxGbxConversionInput config)
    {
        List<NormalizedMesh> normalizedMeshes = new List<NormalizedMesh>();
        foreach (var node in nodes)
        {
            if (node.NodeConfig.MeshFlags.HasMeshData())
            {
                if (node.Node.MeshCount == 0)
                    continue;

                foreach (int meshIndex in node.Node.MeshIndices)
                {
                    var mesh = scene.Meshes[meshIndex];
                    var normMesh = FbxMeshConverter.ConvertMesh(mesh, materials[mesh.MaterialIndex], node.GlobalTransform, node.NodeConfig, config.ItemConfig.Scale);
                    normMesh.GroupIndex = node.GroupIndex;
                    normMesh.LODMask = node.LodMask;

                    normalizedMeshes.Add(normMesh);
                }
            }
        }
        return ToolResult.Success(normalizedMeshes, nameof(FbxGbxConverter));
    }


    static float GetDeterminant3x3(Assimp.Matrix4x4 m)
    {
        // Assimp.Matrix4x4 fields are A1..D4 (row-major: A=row1, B=row2, C=row3)
        return m.A1 * (m.B2 * m.C3 - m.B3 * m.C2)
             - m.A2 * (m.B1 * m.C3 - m.B3 * m.C1)
             + m.A3 * (m.B1 * m.C2 - m.B2 * m.C1);
    }

    static int[] FlipWinding(int[] indices)
    {
        var result = new int[indices.Length];
        for (int i = 0; i < indices.Length; i += 3)
        {
            result[i] = indices[i];
            result[i + 1] = indices[i + 2]; // swap 1 and 2
            result[i + 2] = indices[i + 1];
        }
        return result;
    }

    static Assimp.Matrix4x4 ComputeNormalMatrix(Assimp.Matrix4x4 m)
    {
        float a1 = m.A1, a2 = m.A2, a3 = m.A3;
        float b1 = m.B1, b2 = m.B2, b3 = m.B3;
        float c1 = m.C1, c2 = m.C2, c3 = m.C3;

        float det = a1 * (b2 * c3 - b3 * c2)
                  - a2 * (b1 * c3 - b3 * c1)
                  + a3 * (b1 * c2 - b2 * c1);

        if (MathF.Abs(det) < 1e-8f)
            return Assimp.Matrix4x4.Identity;

        float invDet = 1f / det;

        float i11 = (b2 * c3 - b3 * c2) * invDet;
        float i12 = -(b1 * c3 - b3 * c1) * invDet;
        float i13 = (b1 * c2 - b2 * c1) * invDet;
        float i21 = -(a2 * c3 - a3 * c2) * invDet;
        float i22 = (a1 * c3 - a3 * c1) * invDet;
        float i23 = -(a1 * c2 - a2 * c1) * invDet;
        float i31 = (a2 * b3 - a3 * b2) * invDet;
        float i32 = -(a1 * b3 - a3 * b1) * invDet;
        float i33 = (a1 * b2 - a2 * b1) * invDet;

        var result = Assimp.Matrix4x4.Identity;
        // i11..i33 is already inverse-transpose — write it straight through, no re-transpose
        result.A1 = i11; result.A2 = i12; result.A3 = i13;
        result.B1 = i21; result.B2 = i22; result.B3 = i23;
        result.C1 = i31; result.C2 = i32; result.C3 = i33;
        return result;
    }
}
