using GBX.NET.Engines.Meta;
using TM_GenericMapping.Items.FbxConverter.Serialization;
using static GBX.NET.Engines.GameData.CGameItemModel;

namespace TM_GenericMapping.Items.FbxConverter;


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
internal class MeshLodAssignment
{
    public MeshDef MeshItem { get; set; }

    /// <summary>Subset of Mesh.Lods used by this particular group, ascending.</summary>
    public List<int> LodIndices { get; set; } = new();
}

internal class MeshDefGroup
{
    public string GroupKey { get; set; }

    public MeshGroup MeshGroup { get; set; }

    /// <summary>
    /// Finite distance thresholds for this group (max 3). Slot count =
    /// LodDistances.Count + 1, the last slot always implicitly extends to infinity.
    /// Empty means "no LOD switching in this group".
    /// </summary>
    public List<float> LodDistances { get; set; } = new();

    public List<MeshLodAssignment> Meshes { get; set; } = new();
}

internal class BucketInfo
{
    public string Key;
    public GroupType Type;
    public string MovingGroup;
    public LegacyGameplayId? TriggerEffectId;
    public EWaypointType? WaypointType;
}

internal class MeshGrouper
{
    private const int MaxSlotsPerGroup = 4; // 3 finite thresholds + implicit infinity

    /// <summary>table[i] = max distance for global lod index i</summary>
    private readonly IReadOnlyList<float> _globalLodDistances;

    /// <summary>Moving-group config loaded by the caller, keyed by MovingGroup id.</summary>
    private readonly IReadOnlyDictionary<string, MovingParameter> _movingConfig;
    private ItemConfig _itemConfig;

    public MeshGrouper(IReadOnlyList<float> globalLodDistances, ItemConfig itemConfig, IReadOnlyDictionary<string, MovingParameter> movingConfig = null)
    {
        _globalLodDistances = globalLodDistances;
        _itemConfig = itemConfig;
        _movingConfig = movingConfig ?? new Dictionary<string, MovingParameter>();
    }

    public List<MeshDefGroup> Group(IEnumerable<MeshDef> meshes)
    {
        var buckets = new Dictionary<string, (BucketInfo info, List<MeshDef> meshes)>();
        int isolatedCounter = 0;

        foreach (var mesh in meshes)
        {
            var info = Classify(mesh, ref isolatedCounter);
            if (!buckets.TryGetValue(info.Key, out var entry))
                buckets[info.Key] = entry = (info, new List<MeshDef>());
            entry.meshes.Add(mesh);
        }

        var result = new List<MeshDefGroup>();
        foreach (var entry in buckets.Values)
            result.AddRange(SplitByLod(entry.info, entry.meshes));

        return result;
    }

    private string ComputeBucketKey(MeshDef mesh, ref int isolatedCounter)
    {
        var f = mesh.MeshConfig.MeshFlags;

        // Rule: SingleMesh -> always isolated, never merges with anything.
        if (f.HasFlag(MeshFlags.SingleMesh))
            return $"single_{isolatedCounter++}";

        // Rule: TriggerWaypoint -> one shared bucket for ALL waypoint triggers.
        if (f.HasFlag(MeshFlags.TriggerWaypoint))
            return "waypoint";

        // Rule: TriggerSpecial -> grouped only with same effect.
        if (f.HasFlag(MeshFlags.TriggerEffect))
            return $"trigger_{mesh.MeshConfig.TriggerEffect?.ToString() ?? string.Empty}";

        // Rule: Moving -> own group, unless a MovingGroup id is given.
        if (f.HasFlag(MeshFlags.Moving))
        {
            return !string.IsNullOrEmpty(mesh.MeshConfig.MovingGroup)
                ? $"movegroup_{mesh.MeshConfig.MovingGroup}"
                : $"moving_{isolatedCounter++}";
        }

        return "static"; // meshes with none of the above flags set
    }
    /// <summary>
    /// Determines a mesh's GroupType and bucket key. SingleMesh forces isolation
    /// but does NOT change the underlying classification - a single mesh that is
    /// also Moving still reports GroupType.Moving, it just sits alone in its group.
    /// </summary>
    private BucketInfo Classify(MeshDef mesh, ref int isolatedCounter)
    {
        var f = mesh.MeshConfig.MeshFlags;
        bool isSingle = f.HasFlag(MeshFlags.SingleMesh);

        if (f.HasFlag(MeshFlags.TriggerWaypoint))
        {
            // ASSUMPTION: waypoint groups are keyed by WaypointType (same pattern
            // as TriggerSpecial/effect) rather than one universal bucket for all
            // waypoint types. Change the key below to a constant if you actually
            // want every waypoint mesh in a single shared bucket regardless of type.
            string key = isSingle ? $"single_{isolatedCounter++}" : $"waypoint_{mesh.MeshConfig.WaypointType?.ToString() ?? string.Empty}";
            return new BucketInfo { Key = key, Type = GroupType.Trigger_Waypoint, WaypointType = mesh.MeshConfig.WaypointType };
        }

        if (f.HasFlag(MeshFlags.TriggerEffect))
        {
            string key = isSingle ? $"single_{isolatedCounter++}" : $"trigger_{mesh.MeshConfig.TriggerEffect.ToString() ?? string.Empty}";
            return new BucketInfo { Key = key, Type = GroupType.Trigger_Special, TriggerEffectId = mesh.MeshConfig.TriggerEffect };
        }

        if (f.HasFlag(MeshFlags.Moving))
        {
            bool hasGroup = !string.IsNullOrEmpty(mesh.MeshConfig.MovingGroup);
            string key = isSingle || !hasGroup
                ? $"{(isSingle ? "single" : "moving")}_{isolatedCounter++}"
                : $"movegroup_{mesh.MeshConfig.MovingGroup}";
            return new BucketInfo { Key = key, Type = GroupType.DynaObject, MovingGroup = mesh.MeshConfig.MovingGroup };
        }

        // Static: Visible/Collidable, or the "misc" fallback for meshes with
        // none of the classifying flags set (kept as Static since there's
        // no better bucket for them).
        {
            string key = isSingle ? $"single_{isolatedCounter++}" : "static";
            return new BucketInfo { Key = key, Type = GroupType.StaticObject };
        }
    }

    private List<MeshDefGroup> SplitByLod(BucketInfo bucket, List<MeshDef> meshes)
    {
        MovingParameter movingParams = null;
        if (bucket.Type == GroupType.DynaObject && !string.IsNullOrEmpty(bucket.MovingGroup))
            _movingConfig.TryGetValue(bucket.MovingGroup, out movingParams);

        MeshDefGroup NewGroup(string key) => new MeshDefGroup
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
        var meshesWithLods = meshes.Where(m => m.MeshConfig.Lods is { Count: > 0 }).ToList();
        var meshesWithoutLod = meshes.Where(m => m.MeshConfig.Lods == null || m.MeshConfig.Lods.Count == 0).ToList();

        var distinctLods = meshesWithLods
            .SelectMany(m => m.MeshConfig.Lods)
            .Distinct()
            .OrderBy(x => x)
            .ToList();

        if (distinctLods.Count == 0)
        {
            var single = NewGroup(bucket.Key);
            single.Meshes.AddRange(meshesWithoutLod.Select(m => new MeshLodAssignment { MeshItem = m }));
            return new List<MeshDefGroup> { single };
        }

        var chunks = ChooseChunksMinimizingDuplication(meshesWithLods, distinctLods);

        var groups = new List<MeshDefGroup>();
        for (int c = 0; c < chunks.Count; c++)
        {
            var chunk = chunks[c];
            var group = NewGroup($"{bucket.Key}_lod{c}");

            // Every slot but the last gets a finite threshold;
            // the last slot of any chunk always means "to infinity" in that group.
            for (int i = 0; i < chunk.Count - 1; i++)
                group.LodDistances.Add(_globalLodDistances[chunk[i]]);

            foreach (var mesh in meshesWithLods)
            {
                var overlap = mesh.MeshConfig.Lods
                    .Where(chunk.Contains)
                    .OrderBy(x => x)
                    .ToList();

                if (overlap.Count > 0)
                    group.Meshes.Add(new MeshLodAssignment { MeshItem = mesh, LodIndices = overlap });
            }

            // LOD-agnostic meshes are present at every slot of every group.
            foreach (var mesh in meshesWithoutLod)
                group.Meshes.Add(new MeshLodAssignment { MeshItem = mesh, LodIndices = new List<int>(chunk) });

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
    private List<List<int>> ChooseChunksMinimizingDuplication(List<MeshDef> meshesWithLods, List<int> distinctLods)
    {
        int n = distinctLods.Count;
        int requiredBlocks = (n + MaxSlotsPerGroup - 1) / MaxSlotsPerGroup;

        // position -> set of meshes that use that distinct lod value
        var positionIndex = distinctLods
            .Select((val, idx) => (val, idx))
            .ToDictionary(t => t.val, t => t.idx, comparer: null);

        var meshesAtPosition = new List<HashSet<MeshDef>>(n);
        for (int i = 0; i < n; i++) meshesAtPosition.Add(new HashSet<MeshDef>());
        foreach (var mesh in meshesWithLods)
            foreach (var lod in mesh.MeshConfig.Lods)
                if (positionIndex.TryGetValue(lod, out int pos))
                    meshesAtPosition[pos].Add(mesh);

        // touches[l, len-1] = distinct mesh count touching positions [l, l+len-1]
        var touches = new int[n, MaxSlotsPerGroup];
        for (int l = 0; l < n; l++)
        {
            var running = new HashSet<MeshDef>();
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

