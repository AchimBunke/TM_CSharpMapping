#define FbxGbxDebugLod
using Assimp;
using EarcutDotNet;
using GBX.NET;
using GBX.NET.Engines.Plug;
using GBX.NET.Engines.Scene;
using System.Net.Sockets;
using System.Numerics;
using System.Xml.Linq;
using TM_GenericMapping.Common;
using TM_GenericMapping.Items.FbxGbxConversion.Serialization;
using TM_GenericMapping.Items.FbxGbxConverter;
using TM_GenericMapping.Messaging;
using TmEssentials;
using static GBX.NET.Engines.GameData.CGameItemModel;
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
    public static ToolResult<List<NodeDef>> ExtractMeshNodes(Scene scene, FbxGbxConversionInput config)
    {
        List<NodeDef> nodeDefs = [];
        var nodes = FbxSceneReader.CollectNodes(scene, scene.RootNode);
        foreach (var node in nodes)
        {
            var meshConfigResult = FindMeshConfigForMesh(node.NodeName, config);

            if (meshConfigResult.IsFailure)
                continue;

            var meshConfig = meshConfigResult.Value;

            if (meshConfig.MeshFlags.HasFlag(MeshFlags.Skip))
                continue;

            nodeDefs.Add(new NodeDef { Node = node.node, NodeConfig = meshConfig, GlobalTransform = node.GlobalTransform });
        }
        return ToolResult.Success(nodeDefs, nameof(FbxGbxConverter));
    }

    public static ToolResult<List<SocketDef>> ExtractSockets(Scene scene, FbxGbxConversionInput config)
    {
        List<SocketDef> socketDefs = new List<SocketDef>();

        var nodes = FbxSceneReader.CollectNodes(scene, scene.RootNode);

        foreach (var (node, nodeName, transform) in nodes)
        {
            var meshConfigResult = FindMeshConfigForMesh(nodeName, config);
            if (meshConfigResult.IsFailure)
                continue;

            var meshConfig = meshConfigResult.Value;
            if (!meshConfig.MeshFlags.HasFlag(MeshFlags.Socket))
                continue;
            var spawnModel = ConvertSocket(node, transform, meshConfig, config.ItemConfig.Scale, config);
            socketDefs.Add(new SocketDef() { GlobalTransform = transform, WaypointSpawnModel = spawnModel });
        }
        return ToolResult.Success(socketDefs, nameof(FbxGbxConverter));
    }

    public static ToolResult<List<MeshGroup>> GroupNodes(List<NodeDef> nodes, List<SocketDef> sockets, FbxGbxConversionInput config)
    {
        var lods = config.ItemConfig.LodParameters?.MaxLodDistances ?? [];
#if FbxGbxDebugLod
        lods = [100, 200, 400];
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
                group.MeshGroup.GameplayMainDir = nodeDef.NodeConfig.GameplayMainDir;
            }
            group.MeshGroup.RelativeMovingParentIndex = 
                string.IsNullOrEmpty(group.RelativeMovingParentGroupId) ? null : calculatedGroups.Where(g=>g.MeshGroup.GroupType == GroupType.DynaObject).ToList().FindIndex(g => g.OriginalGroupId == group.RelativeMovingParentGroupId);
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
        Dictionary<int, float> groupToLightmapSize = [];
        foreach (var node in nodes)
        {
            if (node.NodeConfig.MeshFlags.HasMeshData())
            {
                if (node.Node.MeshCount == 0)
                    continue;
                
             
                List<NormalizedMesh> normalizedSubmeshes = [];
                foreach (int meshIndex in node.Node.MeshIndices)
                {
                    var mesh = scene.Meshes[meshIndex];
                    var normMesh = FbxMeshConverter.ConvertMesh(mesh, materials[mesh.MaterialIndex], node.GlobalTransform, node.NodeConfig, config.ItemConfig.Scale);
                    normMesh.GroupIndex = node.GroupIndex;
                    normMesh.LODMask = node.LodMask;

                    normalizedSubmeshes.Add(normMesh);
                }
           
                var mergedMeshes = normalizedSubmeshes.GroupBy(m => m.Material).Select(g =>
                {
                    var merged = MergeMeshes(g);
                    merged.PreLightGenerator = MeshBuilder.CreatePreLightGeneratorFromMeshData(merged);
                    return merged;
                }).ToList();

                normalizedMeshes.AddRange(mergedMeshes);
            }
        }
        if(nodes.Any(n=>n.NodeConfig.LightmapSize.HasValue))
        {
            float maxLightmapSize = nodes.Where(n => n.NodeConfig.LightmapSize.HasValue).Max(n => n.NodeConfig.LightmapSize.Value);
            foreach(var mesh in normalizedMeshes)
            {
                if(mesh.PreLightGenerator != null)
                {
                    mesh.PreLightGenerator.U02 = maxLightmapSize;
                }
            }
        }
        return ToolResult.Success(normalizedMeshes, nameof(FbxGbxConverter));
    }




    public static Assimp.Matrix4x4 CoordinateConversionMatrix = new Assimp.Matrix4x4(
      1, 0, 0, 0,
      0, 1, 0, 0,
      0, 0, 1, 0,
      0, 0, 0, 1
        );

    static NormalizedMesh ConvertMesh(Assimp.Mesh mesh, MaterialDef material, Assimp.Matrix4x4 globalTransform, MeshConfig meshConfig, float meshScale)
    {
        var normalizedMesh = new NormalizedMesh();

        var scaleMatrix = Assimp.Matrix4x4.FromScaling(new Vector3D(meshScale, meshScale, meshScale));

        var normalMatrix = ComputeNormalMatrix(globalTransform);
        //normalMatrix.Inverse();
        //normalMatrix.Transpose();



        normalizedMesh.Positions = TransformVectors(mesh.Vertices, globalTransform, scaleMatrix, false).ToArray();

        var indices = MeshOperations.Triangulate(mesh).ToArray();
        //var indices = mesh.GetIndices();

        bool isMirrored = GetDeterminant3x3(globalTransform) < 0f;
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

            normalizedMesh.TangentUs = TransformVectors(mesh.Tangents, globalTransform, Assimp.Matrix4x4.Identity, true).ToArray();
            normalizedMesh.TangentVs = TransformVectors(mesh.BiTangents, globalTransform, Assimp.Matrix4x4.Identity, true).ToArray();
        }

     
        if (material.DMaterial is null || material.DMaterial.HasLightmapUvlayer)
        {
            int lightmapChannelIndex = 1;
            if (mesh.TextureCoordinateChannelCount > lightmapChannelIndex)
            {
                normalizedMesh.LightmapCoords = mesh.TextureCoordinateChannels[lightmapChannelIndex].Select(tc => new Vec2(tc.X, tc.Y)).ToArray();
                normalizedMesh.PreLightGenerator = MeshBuilder.CreatePreLightGeneratorFromMeshData(normalizedMesh);
                if(meshConfig.LightmapSize.HasValue)
                    normalizedMesh.PreLightGenerator.U02 = meshConfig.LightmapSize.Value;
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



    static CPlugSpawnModel ConvertSocket(Assimp.Node node, Assimp.Matrix4x4 globalTransform, MeshConfig meshConfig, float meshScale, FbxGbxConversionInput config)
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

    private static NormalizedMesh MergeMeshes(IEnumerable<NormalizedMesh> meshes)
    {
        var list = meshes.ToList();

        var result = new NormalizedMesh
        {
            Material = list[0].Material,
            GroupIndex = list[0].GroupIndex,
            LODMask = list[0].LODMask,
            Properties = list[0].Properties,
            SmoothingGroup = list[0].SmoothingGroup,
            SurfaceMaterialIds = list[0].SurfaceMaterialIds,
            Name = list[0].Name,
            Type = list[0].Type
        };

        var positions = new List<Vec3>();
        var normals = new List<Vec3>();
        var texCoords = new List<Vec2>();
        var lightmapCoords = new List<Vec2>();
        var tangentsU = new List<Vec3>();
        var tangentsV = new List<Vec3>();
        var colors = new List<int>();
        var indices = new List<int>();

        int vertexOffset = 0;

        foreach (var mesh in list)
        {
            positions.AddRange(mesh.Positions);
            normals.AddRange(mesh.Normals);

            if (mesh.TexCoords != null)
                texCoords.AddRange(mesh.TexCoords);

            if (mesh.LightmapCoords != null)
                lightmapCoords.AddRange(mesh.LightmapCoords);

            if (mesh.TangentUs != null)
                tangentsU.AddRange(mesh.TangentUs);

            if (mesh.TangentVs != null)
                tangentsV.AddRange(mesh.TangentVs);

            if (mesh.Colors != null)
                colors.AddRange(mesh.Colors);

            foreach (var index in mesh.Indices)
                indices.Add(index + vertexOffset);

            vertexOffset += mesh.Positions.Length;
        }

        result.Positions = positions.ToArray();
        result.Normals = normals.ToArray();
        result.Indices = indices.ToArray();

        result.TexCoords = texCoords.Count > 0
            ? texCoords.ToArray()
            : null;

        result.LightmapCoords = lightmapCoords.Count > 0
            ? lightmapCoords.ToArray()
            : null;

        result.TangentUs = tangentsU.Count > 0
            ? tangentsU.ToArray()
            : null;

        result.TangentVs = tangentsV.Count > 0
            ? tangentsV.ToArray()
            : null;

        result.Colors = colors.Count > 0
            ? colors.ToArray()
            : null;

        DeduplicateVertices(result);
        return result;
    }

    private static void DeduplicateVertices(NormalizedMesh mesh)
    {
        var vertexMap = new Dictionary<VertexKey, int>();

        var positions = new List<Vec3>();
        var normals = new List<Vec3>();
        var texCoords = mesh.TexCoords != null ? new List<Vec2>() : null;
        var lightmapCoords = mesh.LightmapCoords != null ? new List<Vec2>() : null;
        var tangentUs = mesh.TangentUs != null ? new List<Vec3>() : null;
        var tangentVs = mesh.TangentVs != null ? new List<Vec3>() : null;
        var colors = mesh.Colors != null ? new List<int>() : null;

        var newIndices = new int[mesh.Indices.Length];

        for (int i = 0; i < mesh.Indices.Length; i++)
        {
            int oldIndex = mesh.Indices[i];

            var key = new VertexKey(
                mesh.Positions[oldIndex],
                mesh.Normals[oldIndex],
                mesh.TexCoords?[oldIndex],
                mesh.LightmapCoords?[oldIndex],
                mesh.TangentUs?[oldIndex],
                mesh.TangentVs?[oldIndex],
                mesh.Colors?[oldIndex]
            );

            if (!vertexMap.TryGetValue(key, out int newIndex))
            {
                newIndex = positions.Count;
                vertexMap.Add(key, newIndex);

                positions.Add(mesh.Positions[oldIndex]);
                normals.Add(mesh.Normals[oldIndex]);

                texCoords?.Add(mesh.TexCoords![oldIndex]);
                lightmapCoords?.Add(mesh.LightmapCoords![oldIndex]);
                tangentUs?.Add(mesh.TangentUs![oldIndex]);
                tangentVs?.Add(mesh.TangentVs![oldIndex]);
                colors?.Add(mesh.Colors![oldIndex]);
            }

            newIndices[i] = newIndex;
        }

        mesh.Positions = positions.ToArray();
        mesh.Normals = normals.ToArray();
        mesh.TexCoords = texCoords?.ToArray();
        mesh.LightmapCoords = lightmapCoords?.ToArray();
        mesh.TangentUs = tangentUs?.ToArray();
        mesh.TangentVs = tangentVs?.ToArray();
        mesh.Colors = colors?.ToArray();
        mesh.Indices = newIndices;
    }

    private readonly record struct VertexKey(
    Vec3 Position,
    Vec3 Normal,
    Vec2? TexCoord,
    Vec2? LightmapCoord,
    Vec3? TangentU,
    Vec3? TangentV,
    int? Color);

   

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

    // -------------------------------------
    // inverse conversion
    // -------------------------------------


    public static ToolResult<None> RebuildMeshes(Scene scene, NormalizedItem item, ItemConfig config, Dictionary<CPlugMaterialUserInst, int> materialIndices)
    {
        SortedSet<float> maxLodDistances = new SortedSet<float>();
        for (int i = 0; i < item.Groups.Length; i++)
        {
            var group = item.Groups[i];
            foreach (var lod in group.LODDistances) { maxLodDistances.Add(lod); }
        }

        int movingGroupIdCounter = 0;
        Dictionary<MeshGroup, MovingGroupConfig> groupToMovingGroup = new Dictionary<MeshGroup, MovingGroupConfig>();
        for (int i = 0; i < item.Groups.Length; i++)
        {
            var group = item.Groups[i];
            switch (group.GroupType)
            {
                case GroupType.StaticObject:
                    break;
                case GroupType.DynaObject:
                    {
                        var movingGroup = new MovingGroupConfig()
                        {
                            AnchorPosition = group.Position,
                            MovingGroupId = $"movingGroup_{movingGroupIdCounter++}",
                            KinematicMovement = MovingGroupConfig.FromKinematicConstraint(group.KinematicConstraint),
                            KinematicModelConfig = MovingGroupConfig.FromInstanceParams(group.DynaObjectModelParams),
                            //relative moving groups later once all groups registered
                        };
                        groupToMovingGroup[group] = movingGroup;
                        config.MovingGroups.Add(movingGroup);
                    }
                    break;
                case GroupType.Trigger_Special:
                    break;
                case GroupType.Trigger_Waypoint:
                    {
                        var waypointConfig = new Waypoint
                        {
                            Type = (EWaypointType)group.WaypointType!,
                            NoRespawn = group.WaypointNoRespawn.HasValue ? group.WaypointNoRespawn.Value : false,
                        };
                        config.Waypoint = waypointConfig;
                    }
                    break;
            }

            foreach (var m in item.Meshes)
            {
                if (m.GroupIndex != i) continue;

                var meshConfig = RebuildMeshConfig(m, group, maxLodDistances.ToList(), groupToMovingGroup.TryGetValue(group, out var movingGroup) ? movingGroup : null);
                RebuildMesh(scene, m, group, materialIndices);

            }
        }

        // fix relative moving groups
        for (int i = 0; i < item.Groups.Length; i++)
        {
            var group = item.Groups[i];
            if (groupToMovingGroup.TryGetValue(group, out var movingGroup))
            {
                if (group.RelativeMovingParentIndex.HasValue)
                {
                    var parentGroup = item.Groups[group.RelativeMovingParentIndex.Value];
                    if (groupToMovingGroup.TryGetValue(parentGroup, out var parentMovingGroup))
                    {
                        movingGroup.ParentMovingGroupId = parentMovingGroup.MovingGroupId;
                    }
                }
            }
        }

        return ToolResult.Success(nameof(FbxGbxConverter));
    }

    static List<int> MapLocalLodsToGlobal(
        List<float> globalMaxLODDistances,
        List<float> localMaxLODDistances,
        List<int> localLods)
    {
        var result = new List<int>();

        foreach (int localLod in localLods)
        {
            float localMin = localLod == 0
                ? 0f
                : localMaxLODDistances[localLod - 1];

            float localMax = localLod < localMaxLODDistances.Count
                ? localMaxLODDistances[localLod]
                : float.PositiveInfinity;

            for (int globalLod = 0; globalLod <= globalMaxLODDistances.Count; globalLod++)
            {
                float globalMin = globalLod == 0
                    ? 0f
                    : globalMaxLODDistances[globalLod - 1];

                float globalMax = globalLod < globalMaxLODDistances.Count
                    ? globalMaxLODDistances[globalLod]
                    : float.PositiveInfinity;

                // Global interval is completely inside this local interval.
                if (globalMin >= localMin && globalMax <= localMax)
                    result.Add(globalLod);
            }
        }

        return result;
    }

    static MeshConfig RebuildMeshConfig(
        NormalizedMesh normalizedMesh,
        MeshGroup meshGroup,
        List<float> maxLODDistances,
        MovingGroupConfig? movingGroupConfig)
    {
        var meshConfig = new MeshConfig()
        {
            Name = normalizedMesh.Name,
            LightmapSize = normalizedMesh.PreLightGenerator?.U02,
        };

        if (!normalizedMesh.Properties.HasFlag(MeshProperties.Collidable))
            meshConfig.MeshFlags |= MeshFlags.NonCollidable;


        if (!normalizedMesh.Properties.HasFlag(MeshProperties.Visible))
            meshConfig.MeshFlags |= MeshFlags.Invisible;

        if (!normalizedMesh.Properties.HasFlag(MeshProperties.Enabled))
            meshConfig.MeshFlags |= MeshFlags.Skip;


        if (meshGroup.GroupType == GroupType.DynaObject)
            meshConfig.MeshFlags |= MeshFlags.Moving;

        if (meshGroup.GroupType == GroupType.Trigger_Waypoint)
            meshConfig.MeshFlags |= MeshFlags.TriggerWaypoint;

        if (meshGroup.GroupType == GroupType.Trigger_Special)
            meshConfig.MeshFlags |= MeshFlags.TriggerEffect;


        if (!normalizedMesh.Properties.HasFlag(MeshProperties.LOD))
            meshConfig.Lods = [];
        else
        {
            meshConfig.Lods = MapLocalLodsToGlobal(maxLODDistances, meshGroup.LODDistances.ToList(), LODUtils.ToLodIndexes(normalizedMesh.LODMask, meshGroup.LODDistances));
        }

        meshConfig.TriggerEffect = meshGroup.GroupType == GroupType.Trigger_Special ? meshGroup.TriggerGameplayId : null;
        meshConfig.WaypointType = meshGroup.GroupType == GroupType.Trigger_Waypoint ? meshGroup.WaypointType : null;
        meshConfig.GameplayMainDir = meshGroup.GameplayMainDir;
        if(movingGroupConfig != null)
        {
            meshConfig.MovingGroup = movingGroupConfig.MovingGroupId;
        }

        return meshConfig;
    }

    static void RebuildMesh(Scene scene, NormalizedMesh normalizedMesh, MeshGroup meshGroup, Dictionary<CPlugMaterialUserInst, int> materialIndices)
    {
        var mesh = new Assimp.Mesh(normalizedMesh.Name, PrimitiveType.Triangle);

        var globalTransform = CreateGlobalTransform(meshGroup);


        var invGlobal = globalTransform;
        invGlobal.Inverse();

        var invCoordinate = CoordinateConversionMatrix;
        invCoordinate.Inverse();

        var scaleMatrix = Assimp.Matrix4x4.FromScaling(
            new Vector3D(100, 100, 100));

        var invScale = scaleMatrix;
        invScale.Inverse();
        // ------------------------------------------------------------
        // Positions
        //
        // Forward:
        // CoordinateConversionMatrix * scaleMatrix * globalTransform * p
        //
        // Reverse:
        // invGlobal * invScale * invCoordinate * p
        // ------------------------------------------------------------

        foreach (var p in normalizedMesh.Positions)
        {
            var v = new Vector3D(p.X, p.Y, p.Z);

            v = invCoordinate * v;
            v = invScale * v;
            v = invGlobal * v;

            mesh.Vertices.Add(v);
        }

        // ------------------------------------------------------------
        // Normals
        //
        // Forward:
        // CoordinateConversionMatrix * normalMatrix * globalNormal
        //
        // Do NOT apply scale to normals.
        // ------------------------------------------------------------

        if (normalizedMesh.Normals.Length > 0)
        {
            var normalMatrix = ComputeNormalMatrix(globalTransform);
            normalMatrix.Inverse();

            foreach (var n in normalizedMesh.Normals)
            {
                var v = new Vector3D(n.X, n.Y, n.Z);

                v = invCoordinate * v;
                v = normalMatrix * v;

                v.Normalize();
                mesh.Normals.Add(v);
            }
        }

        // ------------------------------------------------------------
        // UV0
        // ------------------------------------------------------------

        if (normalizedMesh.TexCoords is not null)
        {
            mesh.TextureCoordinateChannels[0] = normalizedMesh.TexCoords
                .Select(uv => new Vector3D(uv.X, uv.Y, 0))
                .ToList();

            mesh.UVComponentCount[0] = 2;
        }

        // ------------------------------------------------------------
        // UV1 / Lightmap
        // ------------------------------------------------------------

        if (normalizedMesh.LightmapCoords is not null)
        {
            mesh.TextureCoordinateChannels[1] = normalizedMesh.LightmapCoords
                .Select(uv => new Vector3D(uv.X, uv.Y, 0))
                .ToList();

            mesh.UVComponentCount[1] = 2;
        }

        // ------------------------------------------------------------
        // Colors
        // ------------------------------------------------------------

        if (normalizedMesh.Colors is not null)
        {
            mesh.VertexColorChannels[0] = normalizedMesh.Colors
                .Select(argb =>
                {
                    var c = new GBX.NET.Color(argb);

                    return new Color4D(
                        c.R / 255f,
                        c.G / 255f,
                        c.B / 255f,
                        c.A / 255f);
                })
                .ToList();
        }

        // ------------------------------------------------------------
        // Tangent U/V
        //
        // Forward:
        // CoordinateConversionMatrix * Identity * globalTransform * tangent
        // ------------------------------------------------------------

        if (normalizedMesh.TangentUs is not null)
        {
            foreach (var t in normalizedMesh.TangentUs)
            {
                var v = new Vector3D(t.X, t.Y, t.Z);

                v = invCoordinate * v;
                v = invGlobal * v;

                v.Normalize();
                mesh.Tangents.Add(v);
            }
        }

        if (normalizedMesh.TangentVs is not null)
        {
            foreach (var t in normalizedMesh.TangentVs)
            {
                var v = new Vector3D(t.X, t.Y, t.Z);

                v = invCoordinate * v;
                v = invGlobal * v;

                v.Normalize();
                mesh.BiTangents.Add(v);
            }
        }

        // ------------------------------------------------------------
        // Indices
        // ------------------------------------------------------------

        for (int i = 0; i < normalizedMesh.Indices.Length; i += 3)
        {
            mesh.Faces.Add(new Face(
                [
                normalizedMesh.Indices[i],
                normalizedMesh.Indices[i + 1],
                normalizedMesh.Indices[i + 2]
                ]));
        }


        mesh.MaterialIndex = materialIndices.TryGetValue(normalizedMesh.Material, out var index) ? index : 0;
        scene.Meshes.Add(mesh);
        int meshIndex = scene.Meshes.Count - 1;

        var node = new Node(mesh.Name.Replace(" ", "_"), scene.RootNode);
        node.MeshIndices.Add(meshIndex);
        scene.RootNode.Children.Add(node);


    }
    static Assimp.Matrix4x4 CreateGlobalTransform(MeshGroup meshGroup)
    {
        var rotation = Assimp.Matrix4x4.FromEulerAnglesXYZ(
            meshGroup.Rotation.X,
            meshGroup.Rotation.Y,
            meshGroup.Rotation.Z);

        var translation = Assimp.Matrix4x4.FromTranslation(
            new Vector3D(
                meshGroup.Position.X,
                meshGroup.Position.Y,
                meshGroup.Position.Z));

        return translation * rotation;
    }
}



internal static class MeshOperations
{
    /// <summary>
    /// Rebuilds a full triangle index list for an Assimp Mesh, using our own
    /// per-face earcut triangulation instead of Assimp's built-in Triangulate postprocess step.
    /// Assumes the mesh was imported WITHOUT PostProcessSteps.Triangulate,
    /// so mesh.Faces may still contain n-gons (Face.IndexCount > 3).
    /// </summary>
    public static List<int> Triangulate(Assimp.Mesh mesh)
    {
        var globalIndices = new List<int>(mesh.FaceCount * 3); // rough capacity guess
        foreach (Face face in mesh.Faces)
        {
            if (face.IndexCount < 3)
                continue; // degenerate line/point face, skip

            if (face.IndexCount == 3)
            {
                // Already a triangle — no need to run it through earcut
                globalIndices.Add(face.Indices[0]);
                globalIndices.Add(face.Indices[1]);
                globalIndices.Add(face.Indices[2]);
                continue;
            }

            if (face.IndexCount == 4)
            {
                int i0 = face.Indices[0], i1 = face.Indices[1], i2 = face.Indices[2], i3 = face.Indices[3];

                Vector3D n0 = mesh.Normals[i0];
                Vector3D n1 = mesh.Normals[i1];
                Vector3D n2 = mesh.Normals[i2];
                Vector3D n3 = mesh.Normals[i3];
                n0.Normalize();
                n1.Normalize();
                n2.Normalize();
                n3.Normalize();

                // Dot product of the two corners each diagonal would connect.
                // Higher dot = more similar normals = smoother interpolation along that seam.
                float dot02 = Vector3D.Dot(n0, n2);
                float dot13 = Vector3D.Dot(n1, n3);

                // Pick the diagonal whose two endpoints have the MOST similar normals —
                // that's the seam that will interpolate most smoothly, minimizing visible discontinuity.
                if (dot02 >= dot13)
                {
                    globalIndices.Add(i0);
                    globalIndices.Add(i1);
                    globalIndices.Add(i2);
                    globalIndices.Add(i0);
                    globalIndices.Add(i2);
                    globalIndices.Add(i3);
                }
                else
                {
                    globalIndices.Add(i0);
                    globalIndices.Add(i1);
                    globalIndices.Add(i3);
                    globalIndices.Add(i1);
                    globalIndices.Add(i2);
                    globalIndices.Add(i3);
                }
                continue;
            }

            // Gather this face's vertex positions in polygon order
            var faceVerts = new Vector3[face.IndexCount];
            for (int i = 0; i < face.IndexCount; i++)
            {
                int idx = face.Indices[i];
                Vector3D v = mesh.Vertices[idx];
                faceVerts[i] = new Vector3(v.X, v.Y, v.Z);
            }

            var localTris = FaceTriangulator.Triangulate(faceVerts.AsSpan());

            // Map local (0..N-1) indices back to this face's actual mesh-vertex indices
            foreach (int localIdx in localTris)
                globalIndices.Add(face.Indices[localIdx]);
        }
        return globalIndices;
    }
}
