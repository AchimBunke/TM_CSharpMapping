using GBX.NET.Engines.Meta;
using TM_GenericMapping.Items.FbxGbxConversion.Serialization;
using static GBX.NET.Engines.GameData.CGameItemModel;

namespace TM_GenericMapping.Items.FbxGbxConversion;


internal class MovingParameter
{
    public NPlugDyna_SKinematicConstraint KinematicConstraint { get; set; }
    public NPlugDynaObjectModel_SInstanceParams InstanceParams { get; set; }
}

/// <summary>
/// One mesh's contribution to a specific output group: which of the
/// group's LOD slots it fills. A mesh may appear in several MeshAssignments
/// across different groups if its Lods span more than 4 entries.
/// </summary>
internal class NodeLodAssignment
{
    public NodeDef NodeDef { get; set; }

    /// <summary>Subset of Mesh.Lods used by this particular group, ascending.</summary>
    public List<int> LodIndices { get; set; } = new();
}

internal class NodeDefGroup
{
    public string GroupKey { get; set; }

    public MeshGroup MeshGroup { get; set; }

    /// <summary>
    /// Finite distance thresholds for this group (max 3). Slot count =
    /// LodDistances.Count + 1, the last slot always implicitly extends to infinity.
    /// Empty means "no LOD switching in this group".
    /// </summary>
    public List<float> LodDistances { get; set; } = new();

    public List<NodeLodAssignment> Nodes { get; set; } = new();
}

internal class BucketInfo
{
    public string Key;
    public GroupType Type;
    public string MovingGroup;
    public LegacyGameplayId? TriggerEffectId;
    public EWaypointType? WaypointType;
}

internal class NodeGrouper
{
    private const int MaxSlotsPerGroup = 4; // 3 finite thresholds + implicit infinity

    /// <summary>table[i] = max distance for global lod index i</summary>
    private readonly IReadOnlyList<float> _globalLodDistances;

    /// <summary>Moving-group config loaded by the caller, keyed by MovingGroup id.</summary>
    private readonly IReadOnlyDictionary<string, MovingParameter> _movingConfig;
    private ItemConfig _itemConfig;

    public NodeGrouper(IReadOnlyList<float> globalLodDistances, ItemConfig itemConfig, IReadOnlyDictionary<string, MovingParameter> movingConfig = null)
    {
        _globalLodDistances = globalLodDistances;
        _itemConfig = itemConfig;
        _movingConfig = movingConfig ?? new Dictionary<string, MovingParameter>();
    }

    public List<NodeDefGroup> Group(IEnumerable<NodeDef> nodeDefs)
    {
        var buckets = new Dictionary<string, (BucketInfo info, List<NodeDef> nodes)>();
        int isolatedCounter = 0;

        foreach (var nodeDef in nodeDefs)
        {
            var info = Classify(nodeDef, ref isolatedCounter);
            if (!buckets.TryGetValue(info.Key, out var entry))
                buckets[info.Key] = entry = (info, new List<NodeDef>());
            entry.nodes.Add(nodeDef);
        }

        var result = new List<NodeDefGroup>();
        foreach (var entry in buckets.Values)
            result.AddRange(SplitByLod(entry.info, entry.nodes));

        return result;
    }

    /// <summary>
    /// Determines a mesh's GroupType and bucket key. SingleMesh forces isolation
    /// but does NOT change the underlying classification - a single mesh that is
    /// also Moving still reports GroupType.Moving, it just sits alone in its group.
    /// </summary>
    private BucketInfo Classify(NodeDef node, ref int isolatedCounter)
    {
        var f = node.NodeConfig.MeshFlags;
        bool isSingle = f.HasFlag(MeshFlags.SingleMesh);

        if (f.HasFlag(MeshFlags.TriggerWaypoint))
        {
            // ASSUMPTION: waypoint groups are keyed by WaypointType (same pattern
            // as TriggerSpecial/effect) rather than one universal bucket for all
            // waypoint types. Change the key below to a constant if you actually
            // want every waypoint mesh in a single shared bucket regardless of type.
            string key = isSingle ? $"single_{isolatedCounter++}" : $"waypoint_{node.NodeConfig.WaypointType?.ToString() ?? string.Empty}";
            return new BucketInfo { Key = key, Type = GroupType.Trigger_Waypoint, WaypointType = node.NodeConfig.WaypointType };
        }

        if (f.HasFlag(MeshFlags.TriggerEffect))
        {
            string key = isSingle ? $"single_{isolatedCounter++}" : $"trigger_{node.NodeConfig.TriggerEffect.ToString() ?? string.Empty}";
            return new BucketInfo { Key = key, Type = GroupType.Trigger_Special, TriggerEffectId = node.NodeConfig.TriggerEffect };
        }

        if (f.HasFlag(MeshFlags.Moving))
        {
            bool hasGroup = !string.IsNullOrEmpty(node.NodeConfig.MovingGroup);
            string key = isSingle || !hasGroup
                ? $"{(isSingle ? "single" : "moving")}_{isolatedCounter++}"
                : $"movegroup_{node.NodeConfig.MovingGroup}";
            return new BucketInfo { Key = key, Type = GroupType.DynaObject, MovingGroup = node.NodeConfig.MovingGroup };
        }

        // Static: Visible/Collidable, or the "misc" fallback for meshes with
        // none of the classifying flags set (kept as Static since there's
        // no better bucket for them).
        {
            string key = isSingle ? $"single_{isolatedCounter++}" : "static";
            return new BucketInfo { Key = key, Type = GroupType.StaticObject };
        }
    }

    private List<NodeDefGroup> SplitByLod(BucketInfo bucket, List<NodeDef> nodes)
    {
        MovingParameter movingParams = null;
        if (bucket.Type == GroupType.DynaObject && !string.IsNullOrEmpty(bucket.MovingGroup))
            _movingConfig.TryGetValue(bucket.MovingGroup, out movingParams);

        NodeDefGroup NewGroup(string key) => new NodeDefGroup
        {
            GroupKey = key,
            MeshGroup = new MeshGroup
            {
                GroupType = bucket.Type,
                KinematicConstraint = movingParams?.KinematicConstraint,
                DynaObjectModelParams = movingParams?.InstanceParams,
                TriggerGameplayId = bucket.TriggerEffectId,
                WaypointType = bucket.WaypointType,
                WaypointNoRespawn = _itemConfig.Waypoint?.NoRespawn ?? false,
            },
        };

        // A mesh with no Lods specified is implicitly visible at every LOD
        // level (an "all 1s" bitmask) - it must ride along in EVERY group
        // spawned from this bucket, not just one of them.
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
            single.Nodes.AddRange(nodesWithoutLod.Select(m => new NodeLodAssignment { NodeDef = m }));
            return new List<NodeDefGroup> { single };
        }

        var chunks = ChooseChunksMinimizingDuplication(nodesWithLods, distinctLods);

        var groups = new List<NodeDefGroup>();
        for (int c = 0; c < chunks.Count; c++)
        {
            var chunk = chunks[c];
            var group = NewGroup($"{bucket.Key}_lod{c}");

            // Every slot but the last gets a finite threshold;
            // the last slot of any chunk always means "to infinity" in that group.
            for (int i = 0; i < chunk.Count - 1; i++)
                group.LodDistances.Add(_globalLodDistances[chunk[i]]);

            foreach (var node in nodesWithLods)
            {
                var overlap = node.NodeConfig.Lods
                    .Where(chunk.Contains)
                    .OrderBy(x => x)
                    .ToList();

                if (overlap.Count > 0)
                    group.Nodes.Add(new NodeLodAssignment { NodeDef = node, LodIndices = overlap });
            }

            // LOD-agnostic meshes are present at every slot of every group.
            foreach (var node in nodesWithoutLod)
                group.Nodes.Add(new NodeLodAssignment { NodeDef = node, LodIndices = new List<int>(chunk) });

            groups.Add(group);
        }

        return groups;
    }

    /// <summary>
    /// Partitions the sorted distinct LOD indices into contiguous blocks of at
    /// most <see cref="MaxSlotsPerGroup"/>, using the minimum possible number
    /// of blocks (ceil(N/4) - group count is the primary objective and is fixed
    /// by this), while choosing WHERE to cut so that total mesh duplication
    /// (a mesh gets copied once per block its Lods overlap) is minimized.
    ///
    /// Duplication cost = sum-over-meshes(blocksTouched - 1)
    ///                   = sum-over-blocks(meshesTouchingBlock) - meshesWithLods.Count
    /// The second term is constant, so minimizing total "touches" per block
    /// is equivalent to minimizing duplication. Solved with a small DP over
    /// block boundaries (N is the distinct-lod count for one bucket, tiny).
    /// </summary>
    private List<List<int>> ChooseChunksMinimizingDuplication(List<NodeDef> nodesWithLods, List<int> distinctLods)
    {
        int n = distinctLods.Count;
        int requiredBlocks = (n + MaxSlotsPerGroup - 1) / MaxSlotsPerGroup;

        // position -> set of meshes that use that distinct lod value
        var positionIndex = distinctLods
            .Select((val, idx) => (val, idx))
            .ToDictionary(t => t.val, t => t.idx, comparer: null);

        var meshesAtPosition = new List<HashSet<NodeDef>>(n);
        for (int i = 0; i < n; i++) meshesAtPosition.Add(new HashSet<NodeDef>());
        foreach (var node in nodesWithLods)
            foreach (var lod in node.NodeConfig.Lods)
                if (positionIndex.TryGetValue(lod, out int pos))
                    meshesAtPosition[pos].Add(node);

        // touches[l, len-1] = distinct mesh count touching positions [l, l+len-1]
        var touches = new int[n, MaxSlotsPerGroup];
        for (int l = 0; l < n; l++)
        {
            var running = new HashSet<NodeDef>();
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

        // Reconstruct block boundaries from dp[n, requiredBlocks].
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
}

