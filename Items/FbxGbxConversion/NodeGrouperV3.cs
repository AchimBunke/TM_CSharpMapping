using GBX.NET;
using GBX.NET.Engines.Meta;
using GBX.NET.Serialization.Chunking;
using Silk.NET.Assimp;
using System.Numerics;
using TM_GenericMapping.Items.FbxGbxConversion.Serialization;
using TM_GenericMapping.Items.MeshCompilation;
using static GBX.NET.Engines.GameData.CGameItemModel;

namespace TM_GenericMapping.Items.FbxGbxConversion;

internal class MovingParameterV3
{
    public required NPlugDyna_SKinematicConstraint KinematicConstraint { get; set; }
    public required NPlugDynaObjectModel_SInstanceParams InstanceParams { get; set; }
    public string? ParentMovingGroupId { get; set; } = null;
    public Vec3? AnchorPosition { get; set; } = null;
}

/// <summary>
/// One node's contribution to a specific output group: which of the
/// group's LOD slots it fills. A node may appear in several assignments
/// across different groups if its Lods span more than 4 entries.
/// </summary>
internal class NodeLodAssignmentV3
{
    public required NodeDefV3 NodeDef { get; set; }
    public int GroupIndexOverride { get; set; }
    public int LODMask { get; set; }

    /// <summary>Subset of Mesh.Lods used by this particular group, ascending.</summary>
    public List<int> LodIndices { get; set; } = new();

}

internal class NodeDefGroupV3
{
    public required string GroupKey { get; set; }

    public ModelTypeV3 Type { get; set; }

    /// <summary>
    /// Finite distance thresholds for this group (max 3). Slot count =
    /// LodDistances.Count + 1, the last slot always implicitly extends to infinity.
    /// Empty means "no LOD switching in this group".
    /// </summary>
    public List<float> LodDistances { get; set; } = new();
    public string? RelativeMovingParentGroupId { get; set; } = null;
    public string? OriginalGroupId { get; set; } = null;

    public NPlugDyna_SKinematicConstraint? KinematicConstraint { get; set; }
    public NPlugDynaObjectModel_SInstanceParams? DynaObjectModelParams { get; set; }
    public LegacyGameplayId? TriggerGameplayId { get; set; }
    public Vec3? GameplayMainDir { get; set; }
    public EWaypointType? WaypointType { get; set; }
    public bool? WaypointNoRespawn { get; set; }
    public Vector3 AnchorPosition { get; set; } = Vector3.Zero;
    public CPlugSpawnModelHolderV3? WaypointSpawnModel { get; set; }

    public List<NodeLodAssignmentV3> Nodes { get; set; } = new();
}

/// <summary>Simple boxed holder so we can assign the socket spawn model after grouping without a circular dependency.</summary>
internal class CPlugSpawnModelHolderV3
{
    public required GBX.NET.Engines.Plug.CPlugSpawnModel SpawnModel { get; set; }
}

internal class NodeGrouperV3
{
    private const int MaxLodDistancesSlots = 3; // 3 finite thresholds + implicit infinity

    /// <summary>table[i] = max distance for global lod index i</summary>
    private readonly IReadOnlyList<float> _globalLodDistances;

    /// <summary>Moving-group config loaded by the caller, keyed by MovingGroup id.</summary>
    private readonly IReadOnlyDictionary<string, MovingParameterV3> _movingConfig;
    private ItemConfig _itemConfig;

    public NodeGrouperV3(IReadOnlyList<float> globalLodDistances, ItemConfig itemConfig)
    {
        _globalLodDistances = globalLodDistances;
        _itemConfig = itemConfig;
        _movingConfig = itemConfig.MovingGroups.ToDictionary(mg => mg.MovingGroupId, mg => new MovingParameterV3
        {
            KinematicConstraint = MovingGroupConfig.ToKinematicConstraint(mg.KinematicMovement),
            InstanceParams = MovingGroupConfig.ToInstanceParams(mg.KinematicModelConfig),
            ParentMovingGroupId = mg.ParentMovingGroupId,
            AnchorPosition = mg.AnchorPosition,
        });
    }

    public List<NodeDefGroupV3> Group(IEnumerable<NodeDefV3> nodeDefs)
    {
        var buckets = new Dictionary<string, (BucketInfo info, List<NodeDefV3> nodes)>();
        int isolatedCounter = 0;

        foreach (var nodeDef in nodeDefs)
        {
            var info = Classify(nodeDef, ref isolatedCounter);
            if (!buckets.TryGetValue(info.Key, out var entry))
                buckets[info.Key] = entry = (info, new List<NodeDefV3>());
            entry.nodes.Add(nodeDef);
        }

        SplitDuplicateShapesForInstancing(buckets, ref isolatedCounter);

        var result = new List<NodeDefGroupV3>();
        foreach (var entry in buckets.Values)
            result.AddRange(SplitByLod(entry.info, entry.nodes));

        return result;
    }

    /// <summary>
    /// A group's meshes are baked relative to a single shared anchor transform, so two nodes only end up
    /// with equal (anchor-relative) local geometry - and therefore only get to share the same
    /// <c>NormalizedMeshV3</c> pool entry in <see cref="FbxMeshConverterV3.ExtractMeshes"/> - if they belong
    /// to a group anchored at their own transform. Nodes merged into the default combined "static" bucket
    /// all share one anchor, so any node not sitting exactly at that anchor bakes to a unique transform and
    /// can never reuse a mesh already added to the pool.
    /// To allow reuse of the same source mesh at different world positions/rotations, any node whose
    /// underlying mesh data (its set of scene mesh indices) is duplicated by another node in the merged
    /// static bucket is pulled out into its own isolated group. Each occurrence then gets its own anchor
    /// (its own transform), so its local-to-anchor geometry normalizes to the same value for every
    /// occurrence of that shape, letting the mesh converter recognize and reuse the shared mesh - while
    /// non-duplicated static nodes remain merged as before.
    /// </summary>
    private void SplitDuplicateShapesForInstancing(Dictionary<string, (BucketInfo info, List<NodeDefV3> nodes)> buckets, ref int isolatedCounter)
    {
        foreach (var bucketKey in buckets.Keys.ToList())
        {
            var (info, nodes) = buckets[bucketKey];
            if (nodes.Count <= 1)
                continue;

            var shapeCounts = nodes
                .GroupBy(GetShapeSignature)
                .ToDictionary(g => g.Key, g => g.Count());

            var duplicateNodes = nodes.Where(n => shapeCounts[GetShapeSignature(n)] > 1).ToList();
            if (duplicateNodes.Count == 0)
                continue;

            var remainingNodes = nodes.Except(duplicateNodes).ToList();
            if (remainingNodes.Count > 0)
                buckets[bucketKey] = (info, remainingNodes);
            else
                buckets.Remove(bucketKey);

            foreach (var node in duplicateNodes)
            {
                var instanceKey = $"{bucketKey}_instance_{isolatedCounter++}";
                buckets[instanceKey] = (new BucketInfo { Key = instanceKey, Type = info.Type, MovingGroup = info.MovingGroup, TriggerEffectId = info.TriggerEffectId, GameplayMainDir = info.GameplayMainDir, WaypointType = info.WaypointType}, new List<NodeDefV3> { node });
            }
        }
    }

    /// <summary>Shape signature identifying nodes that reference the exact same underlying source mesh(es),
    /// making them eligible for instancing/mesh reuse regardless of their individual world transform.</summary>
    private static string GetShapeSignature(NodeDefV3 node) =>
        string.Join(",", node.Node.MeshIndices.OrderBy(i => i));

    private BucketInfo Classify(NodeDefV3 node, ref int isolatedCounter)
    {
        var f = node.NodeConfig.MeshFlags;
        bool isSingle = f.HasFlag(MeshFlags.SingleMesh);

        if (f.HasFlag(MeshFlags.TriggerWaypoint))
        {
            string key = isSingle ? $"single_{isolatedCounter++}" : $"waypoint_{node.NodeConfig.WaypointType?.ToString() ?? string.Empty}";
            return new BucketInfo { Key = key, Type = ModelTypeV3.Trigger_Waypoint, WaypointType = node.NodeConfig.WaypointType };
        }

        if (f.HasFlag(MeshFlags.TriggerEffect))
        {
            string key = isSingle ? $"single_{isolatedCounter++}" : $"trigger_{node.NodeConfig.TriggerEffect.ToString() ?? string.Empty}_{node.NodeConfig.GameplayMainDir}";
            return new BucketInfo { Key = key, Type = ModelTypeV3.Trigger_Special, TriggerEffectId = node.NodeConfig.TriggerEffect, GameplayMainDir = node.NodeConfig.GameplayMainDir };
        }

        if (f.HasFlag(MeshFlags.Moving))
        {
            bool hasGroup = !string.IsNullOrEmpty(node.NodeConfig.MovingGroup);
            string key = isSingle || !hasGroup
                ? $"{(isSingle ? "single" : "moving")}_{isolatedCounter++}"
                : $"{node.NodeConfig.MovingGroup}";
            return new BucketInfo { Key = key, Type = ModelTypeV3.Dynamic, MovingGroup = node.NodeConfig.MovingGroup };
        }

        {
            string key;
            if (isSingle)
                key = $"single_{isolatedCounter++}";
            else if (_itemConfig.ConversionOptions.HasFlag(ItemConversionOptions.SkipStaticItemGrouping))
                key = $"static_{isolatedCounter++}";
            else
                key = $"static";
            return new BucketInfo { Key = key, Type = ModelTypeV3.Static };
        }
    }

    private List<NodeDefGroupV3> SplitByLod(BucketInfo bucket, List<NodeDefV3> nodes)
    {
        MovingParameterV3 movingParams = null!;
        if (bucket.Type == ModelTypeV3.Dynamic && !string.IsNullOrEmpty(bucket.MovingGroup))
            _movingConfig.TryGetValue(bucket.MovingGroup, out movingParams!);

        var groups = new List<NodeDefGroupV3>();
        NodeDefGroupV3 NewGroup(string key) => new NodeDefGroupV3
        {
            GroupKey = key,
            OriginalGroupId = bucket.Key,
            Type = bucket.Type,
            KinematicConstraint = movingParams?.KinematicConstraint,
            DynaObjectModelParams = movingParams?.InstanceParams,
            TriggerGameplayId = bucket.TriggerEffectId,
            GameplayMainDir = bucket.GameplayMainDir,
            WaypointType = bucket.WaypointType,
            WaypointNoRespawn = _itemConfig.Waypoint?.NoRespawn ?? false,
            AnchorPosition = movingParams?.AnchorPosition is { } ap ? new Vector3(ap.X, ap.Y, ap.Z) : Vector3.Zero,
            RelativeMovingParentGroupId = movingParams?.ParentMovingGroupId,
        };

        int K = _globalLodDistances.Count; // valid node lod indices are 0..K inclusive; K itself is the implicit "last -> infinity" bucket, never a real distance entry

        foreach (var node in nodes)
            if (node.NodeConfig.Lods != null)
                foreach (var lod in node.NodeConfig.Lods)
                    if (lod < 0 || lod > K)
                        throw new InvalidOperationException(
                            $"Mesh config references LOD index {lod}, but only indices 0..{K} are valid " +
                            $"(ItemConfig.LodParameters.MaxLodDistances defines {K} distance(s), plus the implicit last-to-infinity bucket).");

        var nodesWithLods = nodes.Where(m => m.NodeConfig.Lods is { Count: > 0 }).ToList();
        var nodesWithoutLod = nodes.Where(m => m.NodeConfig.Lods == null || m.NodeConfig.Lods.Count == 0).ToList();

        // only indices < K ever need a slot in LodDistances; index == K rides for free on whichever group ends up last
        var realLods = nodesWithLods
            .SelectMany(m => m.NodeConfig.Lods)
            .Where(v => v < K)
            .Distinct()
            .OrderBy(x => x)
            .ToList();

        //var distinctLods = nodesWithLods
        //    .SelectMany(m => m.NodeConfig.Lods)
        //    .Distinct()
        //    .OrderBy(x => x)
        //    .ToList();

        //if (distinctLods.Count == 0)
        //{
        //    var single = NewGroup(bucket.Key);
        //    single.Nodes.AddRange(nodesWithoutLod.Select(m => new NodeLodAssignmentV3 { NodeDef = m }));
        //    return new List<NodeDefGroupV3> { single };
        //}

        if (realLods.Count == 0)
        {
            // no group split is needed at all: zero real thresholds means one bucket, "0 -> infinity" (local index 0),
            // which covers always-visible nodes and any node that only wanted the terminal index.
            var single = NewGroup(bucket.Key);
            foreach (var node in nodesWithLods)
                single.Nodes.Add(new NodeLodAssignmentV3 { NodeDef = node, LodIndices = new List<int> { 0 } });
            single.Nodes.AddRange(nodesWithoutLod.Select(m => new NodeLodAssignmentV3 { NodeDef = m, LodIndices = new List<int> { 0 } }));
            return new List<NodeDefGroupV3> { single };
        }

        var chunks = ChooseChunksMinimizingDuplication(nodesWithLods, realLods);

        for (int c = 0; c < chunks.Count; c++)
        {
            var chunk = chunks[c];
            bool isLastChunk = c == chunks.Count - 1;
            bool hasPad = c > 0;
            int shift = hasPad ? 1 : 0;

            var group = NewGroup($"{bucket.Key}_lod{c}");

            if (hasPad)
            {
                int boundaryGlobalIndex = chunks[c - 1][^1]; // last real value of the previous group, reused as this group's leading boundary
                group.LodDistances.Add(_globalLodDistances[boundaryGlobalIndex]);
            }

            var localIndex = new Dictionary<int, int>();
            for (int i = 0; i < chunk.Count; i++)
            {
                int globalLodIndex = chunk[i];
                group.LodDistances.Add(_globalLodDistances[globalLodIndex]);
                localIndex[globalLodIndex] = i + shift;
            }

            int terminalLocalIndex = group.LodDistances.Count; // the free implicit "last -> infinity" bucket for THIS group

            foreach (var node in nodesWithLods)
            {
                var overlap = new List<int>();
                foreach (var lod in node.NodeConfig.Lods.OrderBy(x => x))
                {
                    if (lod == K)
                    {
                        if (isLastChunk)
                            overlap.Add(terminalLocalIndex); // only the truly last group owns the real infinity range
                    }
                    else if (chunk.Contains(lod))
                    {
                        overlap.Add(localIndex[lod]);
                    }
                }

                if (overlap.Count > 0)
                    group.Nodes.Add(new NodeLodAssignmentV3 { NodeDef = node, LodIndices = overlap });
            }

            groups.Add(group);
        }
        foreach (var node in nodesWithoutLod)
            groups[0].Nodes.Add(new NodeLodAssignmentV3
            {
                NodeDef = node,
                LodIndices = Enumerable.Range(0, groups[0].LodDistances.Count + 1).ToList()
            });

        return groups;
    }

    private List<List<int>> ChooseChunksMinimizingDuplication(List<NodeDefV3> nodesWithLods, List<int> distinctLods)
    {
        int n = distinctLods.Count;

        // first block: 3 new thresholds. every later block: 2 new thresholds
        // (1 of its 3 slots is spent reusing the previous block's last value as a boundary)
        static int CapacityForBlock(bool isFirst) => isFirst ? MaxLodDistancesSlots : MaxLodDistancesSlots - 1;

        int requiredBlocks;
        {
            int covered = 0, blocks = 0;
            while (covered < n)
            {
                covered += CapacityForBlock(isFirst: blocks == 0);
                blocks++;
            }
            requiredBlocks = blocks;
        }

        var positionIndex = distinctLods
            .Select((val, idx) => (val, idx))
            .ToDictionary(t => t.val, t => t.idx);

        var meshesAtPosition = new List<HashSet<NodeDefV3>>(n);
        for (int i = 0; i < n; i++) meshesAtPosition.Add(new HashSet<NodeDefV3>());
        foreach (var node in nodesWithLods)
            foreach (var lod in node.NodeConfig.Lods)
                if (positionIndex.TryGetValue(lod, out int pos))
                    meshesAtPosition[pos].Add(node);

        var touches = new int[n, MaxLodDistancesSlots];
        for (int l = 0; l < n; l++)
        {
            var running = new HashSet<NodeDefV3>();
            for (int len = 1; len <= MaxLodDistancesSlots && l + len - 1 < n; len++)
            {
                running.UnionWith(meshesAtPosition[l + len - 1]);
                touches[l, len - 1] = running.Count;
            }
        }

        const int Inf = int.MaxValue / 2;
        var dp = new int[n + 1, requiredBlocks + 1];
        var parent = new int[n + 1, requiredBlocks + 1];
        for (int i = 0; i <= n; i++)
            for (int b = 0; b <= requiredBlocks; b++)
                dp[i, b] = Inf;
        dp[0, 0] = 0;

        for (int i = 1; i <= n; i++)
        {
            for (int b = 1; b <= requiredBlocks; b++)
            {
                int maxLen = Math.Min(MaxLodDistancesSlots, i);
                for (int len = 1; len <= maxLen; len++)
                {
                    int l = i - len;
                    int cap = CapacityForBlock(isFirst: l == 0);
                    if (len > cap) continue;
                    if (dp[l, b - 1] >= Inf) continue;

                    int cand = dp[l, b - 1] + touches[l, len - 1];
                    if (cand < dp[i, b])
                    {
                        dp[i, b] = cand;
                        parent[i, b] = l;
                    }
                }
            }
        }

        var chunks = new List<List<int>>();
        int cursor = n, blocksLeft = requiredBlocks;
        while (cursor > 0)
        {
            int start = parent[cursor, blocksLeft];
            chunks.Add(distinctLods.GetRange(start, cursor - start));
            cursor = start;
            blocksLeft--;
        }
        chunks.Reverse();
        return chunks;
    }

    internal class BucketInfo
    {
        public required string Key;
        public required ModelTypeV3 Type;
        public string? MovingGroup;
        public LegacyGameplayId? TriggerEffectId;
        public EWaypointType? WaypointType;
        public Vector3? GameplayMainDir;
    }
}
