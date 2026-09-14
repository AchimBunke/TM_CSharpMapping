using GBX.NET;
using GBX.NET.Engines.Meta;
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
    private const int MaxSlotsPerGroup = 4; // 3 finite thresholds + implicit infinity

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

        var result = new List<NodeDefGroupV3>();
        foreach (var entry in buckets.Values)
            result.AddRange(SplitByLod(entry.info, entry.nodes));

        return result;
    }

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

        var nodesWithLods = nodes.Where(m => m.NodeConfig.Lods is { Count: > 0 }).ToList();
        var nodesWithoutLod = nodes.Where(m => m.NodeConfig.Lods == null || m.NodeConfig.Lods.Count == 0).ToList();

        var distinctLods = nodesWithLods
            .SelectMany(m => m.NodeConfig.Lods)
            .Distinct()
            .OrderBy(x => x)
            .ToList();

        if (distinctLods.Count == 0)
        {
            var single = NewGroup(bucket.Key);
            single.Nodes.AddRange(nodesWithoutLod.Select(m => new NodeLodAssignmentV3 { NodeDef = m }));
            return new List<NodeDefGroupV3> { single };
        }

        var chunks = ChooseChunksMinimizingDuplication(nodesWithLods, distinctLods);

        for (int c = 0; c < chunks.Count; c++)
        {
            var chunk = chunks[c];
            var group = NewGroup($"{bucket.Key}_lod{c}");

            // Every slot but the last gets a finite threshold;
            // the last slot of any chunk always means "to infinity" in that group.
            for (int i = 0; i < chunk.Count - 1; i++)
            {
                int globalLodIndex = chunk[i];
                if (globalLodIndex >= _globalLodDistances.Count)
                    throw new InvalidOperationException(
                        $"Mesh config references LOD index {globalLodIndex}, but ItemConfig.LodParameters.MaxLodDistances only defines {_globalLodDistances.Count} distance(s). " +
                        "Add a matching entry to MaxLodDistances for every LOD level referenced by a mesh's Lods.");

                group.LodDistances.Add(_globalLodDistances[globalLodIndex]);
            }

            foreach (var node in nodesWithLods)
            {
                var overlap = node.NodeConfig.Lods
                    .Where(chunk.Contains)
                    .OrderBy(x => x)
                    .ToList();

                if (overlap.Count > 0)
                    group.Nodes.Add(new NodeLodAssignmentV3 { NodeDef = node, LodIndices = overlap });
            }

            foreach (var node in nodesWithoutLod)
                group.Nodes.Add(new NodeLodAssignmentV3 { NodeDef = node, LodIndices = new List<int>(chunk) });

            groups.Add(group);
        }

        return groups;
    }

    private List<List<int>> ChooseChunksMinimizingDuplication(List<NodeDefV3> nodesWithLods, List<int> distinctLods)
    {
        int n = distinctLods.Count;
        int requiredBlocks = (n + MaxSlotsPerGroup - 1) / MaxSlotsPerGroup;

        var positionIndex = distinctLods
            .Select((val, idx) => (val, idx))
            .ToDictionary(t => t.val, t => t.idx, comparer: null);

        var meshesAtPosition = new List<HashSet<NodeDefV3>>(n);
        for (int i = 0; i < n; i++) meshesAtPosition.Add(new HashSet<NodeDefV3>());
        foreach (var node in nodesWithLods)
            foreach (var lod in node.NodeConfig.Lods)
                if (positionIndex.TryGetValue(lod, out int pos))
                    meshesAtPosition[pos].Add(node);

        var touches = new int[n, MaxSlotsPerGroup];
        for (int l = 0; l < n; l++)
        {
            var running = new HashSet<NodeDefV3>();
            for (int len = 1; len <= MaxSlotsPerGroup && l + len - 1 < n; len++)
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
                int maxLen = Math.Min(MaxSlotsPerGroup, i);
                for (int len = 1; len <= maxLen; len++)
                {
                    int l = i - len;
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
