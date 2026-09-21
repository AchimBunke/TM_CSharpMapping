using GBX.NET;
using GBX.NET.Engines.Plug;
using System.Numerics;
using TM_GenericMapping.Common;
using TM_GenericMapping.Items.FbxGbxConversion.Importing;
using TM_GenericMapping.Items.FbxGbxConversion.Serialization;
using TM_GenericMapping.Items.MeshCompilation;
using TM_GenericMapping.Messaging;
using TmEssentials;

namespace TM_GenericMapping.Items.FbxGbxConversion;

internal class NodeDef
{
    public required ImportedNode Node { get; set; }
    public Matrix4x4 GlobalTransform { get; set; }
    public required MeshConfig NodeConfig { get; set; }

    public int GroupIndex { get; set; } = -1;
    public int LodMask { get; set; } = 1;
}

internal class SocketDef
{
    public Matrix4x4 GlobalTransform { get; set; }
    public CPlugSpawnModel? WaypointSpawnModel { get; set; }
}

internal class FbxMeshConverter
{
    public static ToolResult<List<NodeDef>> ExtractMeshNodes(ImportedScene scene, FbxGbxConversionInput config)
    {
        List<NodeDef> nodeDefs = [];
        var nodes = scene.CollectNodes();
        foreach (var node in nodes)
        {
            var meshConfigResult = FindMeshConfigForMesh(node.Name, config);

            if (meshConfigResult.IsFailure)
                continue;

            var meshConfig = meshConfigResult.Value;

            if (meshConfig.MeshFlags.HasFlag(MeshFlags.Skip))
                continue;

            nodeDefs.Add(new NodeDef { Node = node, NodeConfig = meshConfig, GlobalTransform = node.GlobalTransform });
        }
        return ToolResult.Success(nodeDefs, nameof(FbxGbxConverter));
    }

    public static ToolResult<List<SocketDef>> ExtractSockets(ImportedScene scene, FbxGbxConversionInput config)
    {
        List<SocketDef> socketDefs = new List<SocketDef>();

        var nodes = scene.CollectNodes();

        foreach (var node in nodes)
        {
            var meshConfigResult = FindMeshConfigForMesh(node.Name, config);
            if (meshConfigResult.IsFailure)
                continue;

            var meshConfig = meshConfigResult.Value;
            if (!meshConfig.MeshFlags.HasFlag(MeshFlags.Socket))
                continue;
            var spawnModel = ConvertSocket(node, node.GlobalTransform, meshConfig, config);
            socketDefs.Add(new SocketDef() { GlobalTransform = node.GlobalTransform, WaypointSpawnModel = spawnModel });
        }
        return ToolResult.Success(socketDefs, nameof(FbxGbxConverter));
    }

    /// <summary>
    /// Groups mesh nodes into <see cref="NodeDefGroup"/> buckets (static/dyna/trigger/waypoint, LOD-split)
    /// and computes a per-group anchor transform (position + rotation) that will become the group's
    /// EntityRef.Position/Rotation. Node geometry is later expressed relative to this anchor instead of world space.
    /// </summary>
    public static ToolResult<List<NodeDefGroup>> GroupNodes(List<NodeDef> nodes, List<NodeDef> allSceneNodes, List<SocketDef> sockets, FbxGbxConversionInput config)
    {
        var lods = config.ItemConfig.LodParameters?.MaxLodDistances ?? [];

#if (false)
        lods = [100, 200, 300];
#endif
        int maxReferencedLod = nodes
            .Where(n => n.NodeConfig.Lods is { Count: > 0 })
            .SelectMany(n => n.NodeConfig.Lods)
            .DefaultIfEmpty(-1)
            .Max();

        if (maxReferencedLod > lods.Count)
            return ToolResult.Fail(nameof(FbxGbxConverter), ErrorCodes.FbxGbxConverter.MissingLodDistanceConfig,
                $"Mesh config references LOD index {maxReferencedLod}, but ItemConfig.LodParameters.MaxLodDistances only defines {lods.Count} distance(s).");

        var grouper = new NodeGrouper(lods, config.ItemConfig);
        var groups = grouper.Group(nodes);

        for (int i = 0; i < groups.Count; ++i)
        {
            var group = groups[i];
            foreach (var nodeAssignment in group.Nodes)
            {
                var nodeDef = nodeAssignment.NodeDef;
                nodeAssignment.GroupIndexOverride = i;
                nodeAssignment.LODMask = LODUtils.LodMaskFromLods(nodeAssignment.LodIndices.ToArray());
            }
        }

        for (int i = 0; i < groups.Count; ++i)
        {
            var group = groups[i];
            group.RelativeMovingParentGroupId = string.IsNullOrEmpty(group.RelativeMovingParentGroupId)
                ? null
                : group.RelativeMovingParentGroupId;
            if (!string.IsNullOrEmpty(group.RotationAnchorNode))
                group.AnchorPosition = allSceneNodes.FirstOrDefault(n => n.NodeConfig.Name == group.RotationAnchorNode)!.GlobalTransform.Translation;
            else
                group.AnchorPosition = Vector3.Zero;
        }

        if (sockets.Count > 0)
        {
            foreach (var group in groups)
            {
                if (group.Type != ModelType.Trigger_Waypoint)
                    continue;
                if (group.WaypointType == GBX.NET.Engines.GameData.CGameItemModel.EWaypointType.Finish)
                    continue;
                group.WaypointSpawnModel = new CPlugSpawnModelHolder { SpawnModel = sockets[0].WaypointSpawnModel! };
            }
        }

        return ToolResult.Success(groups, nameof(FbxGbxConverter));
    }

    /// <summary>
    /// Computes the group's anchor transform (as a decomposed position/rotation) used to express all of its
    /// nodes' meshes/lights in local space. Uses the moving-group's configured AnchorPosition when present,
    /// otherwise the first node's global transform position with no rotation offset (rotation hierarchy is
    /// supported by the new item model, so we don't need to bake it into vertices).
    /// </summary>
    public static (System.Numerics.Vector3 Position, System.Numerics.Quaternion Rotation) ComputeGroupAnchor(NodeDefGroup group)
    {
        if (group.AnchorPosition != Vector3.Zero)
            return (group.AnchorPosition, System.Numerics.Quaternion.Identity);

        if (group.Nodes.Count == 0)
            return (Vector3.Zero, System.Numerics.Quaternion.Identity);

        var first = group.Nodes[0].NodeDef.GlobalTransform;
        Matrix4x4.Decompose(first, out _, out var rot, out var translation);
        return (translation, rot);
    }

    public static Matrix4x4 AnchorToMatrix(System.Numerics.Vector3 position, System.Numerics.Quaternion rotation)
    {
        var rotMatrix = Matrix4x4.CreateFromQuaternion(rotation);
        rotMatrix.Translation = position;
        return rotMatrix;
    }

    public static ToolResult<None> ExtractMeshes(
        ImportedScene scene,
        List<NodeDefGroup> groups,
        List<MaterialDef> materials,
        List<NodeDef> nodes,
        FbxGbxConversionInput config,
        NormalizedItem item,
        Dictionary<int, int> groupIndexToModelKey,
        Dictionary<int, System.Numerics.Vector3> groupIndexToAnchorPosition,
        Dictionary<int, System.Numerics.Quaternion> groupIndexToAnchorRotation)
    {
        // Maps a source mesh (by scene mesh index + resulting local transform + material) to an already
        // created MeshPool entry, so that meshes shared between multiple nodes (or multiple mesh slots of
        // the same node) are only converted/stored once. We intentionally do NOT merge submeshes by
        // material here (unlike the old converter), since merging would combine otherwise-shareable meshes
        // into a single unique blob and break reuse detection.
        // The local transform is quantized before being used as a key: it is derived via Matrix4x4.Decompose
        // and Matrix4x4.Invert, so two occurrences of the same shape that only differ by their node's world
        // position/rotation (and therefore should produce mathematically identical local geometry once
        // anchor-relative) can still differ by floating-point rounding noise. Comparing raw bits would make
        // the cache miss in that case and defeat mesh reuse/instancing.
        Dictionary<(int SourceMeshIndex, QuantizedMatrix LocalTransform, CPlugMaterialUserInst? Material), int> meshCache = new();
        Dictionary<int, List<(int MeshKey, NodeLodAssignment NodeAssignment)>> meshRefsByGroup = new();

        foreach(var group in groups)
        {
            foreach (var nodeAssignment in group.Nodes)
            {
                var node = nodeAssignment.NodeDef;

                if (!node.NodeConfig.MeshFlags.HasMeshData())
                    continue;
                if (node.Node.MeshIndices.Count == 0)
                    continue;
                if (nodeAssignment.GroupIndexOverride < 0)
                    continue;

                var anchorMatrix = AnchorToMatrix(groupIndexToAnchorPosition[nodeAssignment.GroupIndexOverride], groupIndexToAnchorRotation[nodeAssignment.GroupIndexOverride]);
                Matrix4x4.Invert(anchorMatrix, out var invAnchor);
                var localTransform = node.GlobalTransform * invAnchor;

                if (!meshRefsByGroup.TryGetValue(nodeAssignment.GroupIndexOverride, out var groupList))
                    meshRefsByGroup[nodeAssignment.GroupIndexOverride] = groupList = new();

                foreach (int meshIndex in node.Node.MeshIndices)
                {
                    var mesh = scene.Meshes[meshIndex];
                    var material = materials[mesh.MaterialIndex];
                    var cacheKey = (meshIndex, QuantizedMatrix.From(localTransform), material.MaterialInstance);

                    if (!meshCache.TryGetValue(cacheKey, out var meshKey))
                    {
                        var normMesh = ConvertMesh(mesh, material, localTransform, node.NodeConfig);
                        meshKey = item.MeshPool.Count == 0 ? 0 : item.MeshPool.Keys.Max() + 1;
                        item.MeshPool[meshKey] = normMesh;
                        meshCache[cacheKey] = meshKey;
                    }

                    groupList.Add((meshKey, nodeAssignment));
                }
            }
        }
       

        foreach (var (groupIndex, meshRefs) in meshRefsByGroup)
        {
            var modelKey = groupIndexToModelKey[groupIndex];
            var model = item.ModelPool[modelKey];
            foreach (var (meshKey, nodeAssignment) in meshRefs)
            {
                var mesh = item.MeshPool[meshKey];
                var meshRef = new MeshRef
                {
                    MeshKey = meshKey,
                    LODMask = nodeAssignment.LODMask,
                    Properties = ComputeMeshProperties(nodeAssignment.NodeDef.NodeConfig),
                    PreLightGenerator = GbxItemUtils.ComputePreLightGenFromMeshData(mesh),
                };
                if(nodeAssignment.NodeDef.NodeConfig.LightmapSize.HasValue)
                    GbxItemUtils.SetLightmapSizeLengthMeters(meshRef.PreLightGenerator!, nodeAssignment.NodeDef.NodeConfig.LightmapSize.Value);
                model.Meshes.Add(meshRef);
            }
        }

        return ToolResult.Success(None.Value, nameof(FbxGbxConverter));
    }

    static MeshProperties ComputeMeshProperties(MeshConfig meshConfig)
    {
        var properties = MeshProperties.Enabled;
        if (!meshConfig.MeshFlags.HasFlag(MeshFlags.NonCollidable))
            properties |= MeshProperties.Collidable;
        if (!meshConfig.MeshFlags.HasFlag(MeshFlags.Invisible))
            properties |= MeshProperties.Visible;
        if (meshConfig.Lods.Count > 0)
            properties |= MeshProperties.LOD;
        return properties;
    }

    public static Matrix4x4 CoordinateConversionMatrix = Matrix4x4.Identity;

    static NormalizedMesh ConvertMesh(ImportedMesh mesh, MaterialDef material, Matrix4x4 localTransform, MeshConfig meshConfig)
    {
        var normalizedMesh = new NormalizedMesh();

        var scaleMatrix = Matrix4x4.CreateScale(1);

        var normalMatrix = ComputeNormalMatrix(localTransform);

        normalizedMesh.Positions = TransformVectors(mesh.Positions, localTransform, scaleMatrix, false).ToArray();

        var indices = (int[])mesh.Indices.Clone();

        bool isMirrored = GetDeterminant3x3(localTransform) < 0f;
        if (isMirrored)
            indices = FlipWinding(indices);
        normalizedMesh.Indices = indices;

        normalizedMesh.Normals = TransformVectors(mesh.Normals, normalMatrix, Matrix4x4.Identity, true).ToArray();

        if (material.DMaterial is null || material.DMaterial.HasTexUvLayer)
        {
            int texChannelIndex = 0;
            if (mesh.TextureCoordinateChannels.Length > texChannelIndex)
                normalizedMesh.TexCoords = mesh.TextureCoordinateChannels[texChannelIndex].Select(tc => new Vec2(tc.X, tc.Y)).ToArray();

            if (mesh.Tangents is not null)
                normalizedMesh.TangentUs = TransformVectors(mesh.Tangents, localTransform, Matrix4x4.Identity, true).ToArray();
            if (mesh.BiTangents is not null)
                normalizedMesh.TangentVs = TransformVectors(mesh.BiTangents, localTransform, Matrix4x4.Identity, true).ToArray();
        }

        if (material.DMaterial is null || material.DMaterial.HasLightmapUvlayer)
        {
            int lightmapChannelIndex = 1;
            if (mesh.TextureCoordinateChannels.Length > lightmapChannelIndex)
                normalizedMesh.LightmapCoords = mesh.TextureCoordinateChannels[lightmapChannelIndex].Select(tc => new Vec2(tc.X, tc.Y)).ToArray();
        }

        if (material.DMaterial is null || material.DMaterial.HasColor0)
        {
            if (mesh.VertexColorChannels.Length == 1)
                normalizedMesh.Colors = mesh.VertexColorChannels[0].Select(c =>
                    new GBX.NET.Color(c.X * 255f, c.Y * 255f, c.Z * 255f, c.W * 255f).ToArgb()).ToArray();
            else
                normalizedMesh.Colors = Enumerable.Repeat(-1, mesh.Positions.Length).ToArray();
        }

        normalizedMesh.Material = material.MaterialInstance;
        normalizedMesh.Name = mesh.Name;

        DeduplicateVertices(normalizedMesh);

        return normalizedMesh;
    }

    static CPlugSpawnModel ConvertSocket(ImportedNode node, Matrix4x4 globalTransform, MeshConfig meshConfig, FbxGbxConversionInput config)
    {
        var spawnModel = GbxItemUtils.CreateSpawnModel();

        var scaleMatrix = Matrix4x4.CreateScale(1);

        Matrix4x4.Decompose(globalTransform, out _, out var nodeRotation, out var translation);
        nodeRotation = Quaternion.CreateFromXRotationDegrees(90) * nodeRotation; // fix fbx rotation for socket

        spawnModel.Loc = Iso4Utils.IsoFromTransform(translation, nodeRotation);

        if (config.ItemConfig.Waypoint is null)
            return spawnModel;

        if (config.ItemConfig.Waypoint.TorqueX.HasValue)
            spawnModel.TorqueX = config.ItemConfig.Waypoint.TorqueX.Value;
        if (config.ItemConfig.Waypoint.DefaultGravitySpawn.HasValue)
            spawnModel.DefaultGravitySpawn = config.ItemConfig.Waypoint.DefaultGravitySpawn.Value;
        if (config.ItemConfig.Waypoint.TorqueDuration.HasValue)
            spawnModel.TorqueDuration = TimeInt32.FromMilliseconds(config.ItemConfig.Waypoint.TorqueDuration.Value);
        return spawnModel;
    }

    static IEnumerable<Vec3> TransformVectors(IEnumerable<Vector3> vectors, Matrix4x4 m1, Matrix4x4 m2, bool normalize)
    {
        return vectors
                .Select(n => Vector3.Transform(n, m1))
                .Select(n => Vector3.Transform(n, m2 * CoordinateConversionMatrix))
                .Select(v => normalize ? new Vec3(v.X, v.Y, v.Z).GetNormalized() : new Vec3(v.X, v.Y, v.Z));
    }

    internal static ToolResult<MeshConfig> FindMeshConfigForMesh(string meshName, FbxGbxConversionInput config)
    {
        var meshConfig = config.ItemConfig.MeshConfiguration.FirstOrDefault(m => m!.Name == meshName, null);
        bool configFromMeshName = config.ItemConfig.ConversionOptions.HasFlag(ItemConversionOptions.MeshConfigFromObjectNames);
        if (meshConfig is null && !configFromMeshName)
            return ToolResult.Fail(nameof(FbxGbxConverter), ErrorCodes.FbxGbxConverter.MissingMeshConfig, meshName);

        if (config.ItemConfig.ConversionOptions.HasFlag(ItemConversionOptions.MeshConfigFromObjectNames))
            meshConfig = MeshConfigFromMeshName(meshConfig, meshName, config);

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
        const string moving = "_moving_";

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
        if (meshName.Contains(single))
            meshConfig.MeshFlags |= MeshFlags.SingleMesh;

        if (meshName.Contains(moving))
            meshConfig.MeshFlags |= MeshFlags.Moving;

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
            Name = list[0].Name,
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

        result.TexCoords = texCoords.Count > 0 ? texCoords.ToArray() : null;
        result.LightmapCoords = lightmapCoords.Count > 0 ? lightmapCoords.ToArray() : null;
        result.TangentUs = tangentsU.Count > 0 ? tangentsU.ToArray() : null;
        result.TangentVs = tangentsV.Count > 0 ? tangentsV.ToArray() : null;
        result.Colors = colors.Count > 0 ? colors.ToArray() : null;

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

    /// <summary>
    /// A <see cref="Matrix4x4"/> quantized to a fixed decimal precision so it can be used as a reliable
    /// dictionary key. Transforms derived via decomposition/inversion (as used for anchor-relative local
    /// transforms) are mathematically equal for repeated instances of the same shape but can differ by tiny
    /// floating-point rounding noise, which would otherwise defeat exact-equality-based mesh reuse.
    /// </summary>
    private readonly record struct QuantizedMatrix(
        long M11, long M12, long M13, long M14,
        long M21, long M22, long M23, long M24,
        long M31, long M32, long M33, long M34,
        long M41, long M42, long M43, long M44)
    {
        private const float Precision = 100000f; // 1e-5 tolerance

        public static QuantizedMatrix From(Matrix4x4 m) => new(
            Quantize(m.M11), Quantize(m.M12), Quantize(m.M13), Quantize(m.M14),
            Quantize(m.M21), Quantize(m.M22), Quantize(m.M23), Quantize(m.M24),
            Quantize(m.M31), Quantize(m.M32), Quantize(m.M33), Quantize(m.M34),
            Quantize(m.M41), Quantize(m.M42), Quantize(m.M43), Quantize(m.M44));

        private static long Quantize(float value) => (long)MathF.Round(value * Precision);
    }

    static float GetDeterminant3x3(Matrix4x4 m)
    {
        return m.M11 * (m.M22 * m.M33 - m.M23 * m.M32)
             - m.M12 * (m.M21 * m.M33 - m.M23 * m.M31)
             + m.M13 * (m.M21 * m.M32 - m.M22 * m.M31);
    }

    static int[] FlipWinding(int[] indices)
    {
        var result = new int[indices.Length];
        for (int i = 0; i < indices.Length; i += 3)
        {
            result[i] = indices[i];
            result[i + 1] = indices[i + 2];
            result[i + 2] = indices[i + 1];
        }
        return result;
    }

    static Matrix4x4 ComputeNormalMatrix(Matrix4x4 m)
    {
        float a1 = m.M11, a2 = m.M12, a3 = m.M13;
        float b1 = m.M21, b2 = m.M22, b3 = m.M23;
        float c1 = m.M31, c2 = m.M32, c3 = m.M33;

        float det = a1 * (b2 * c3 - b3 * c2)
                  - a2 * (b1 * c3 - b3 * c1)
                  + a3 * (b1 * c2 - b2 * c1);

        if (MathF.Abs(det) < 1e-8f)
            return Matrix4x4.Identity;

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

        var result = Matrix4x4.Identity;
        result.M11 = i11; result.M12 = i12; result.M13 = i13;
        result.M21 = i21; result.M22 = i22; result.M23 = i23;
        result.M31 = i31; result.M32 = i32; result.M33 = i33;
        return result;
    }
}
