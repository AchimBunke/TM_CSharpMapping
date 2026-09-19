using GBX.NET.Engines.Meta;
using GBX.NET.Engines.Plug;
using System.Numerics;
using static GBX.NET.Engines.GameData.CGameItemModel;

namespace TM_GenericMapping.Items.MeshCompilation;


public enum RefKind { Mesh, Shape, Light }
public enum LightType
{
    Point,
    Spot,
    Area
}


public sealed class InstanceSettings
{
    public RefKind Kind { get; set; }
    public Guid ClusterKey { get; set; }

    public bool Enabled { get; set; } = true;

    // mesh + shape
    public bool Visible { get; set; }       // contributes geometry to the cluster's SolidMesh
    public bool Collidable { get; set; }    // contributes geometry to the cluster's collision Surface

    // shape only
    public ShapeRole? ShapeRoleOverride { get; set; }
 

    // mesh only
    public int? LODMaskOverride { get; set; }
    public float? LightmapSizeOverride { get; set; }
    public int? SmoothingGroupOverride { get; set; }

    // ligth only
    public LightType? LightTypeOverride { get; set; }
}

public sealed class ClusterSettings
{
    public ModelType Type { get; set; }

    public float[] LODDistances { get; set; } = [];
    public EWaypointType? WaypointType { get; set; }
    public bool? WaypointNoRespawn { get; set; }
    public LegacyGameplayId? TriggerGameplayId { get; set; }
    public Vector3? GameplayMainDir { get; set; }

    public NPlugDyna_SKinematicConstraint? KinematicConstraint { get; set; }
    public NPlugDynaObjectModel_SInstanceParams? DynaObjectModelParams { get; set; }
    public Guid? RelativeMovingParentCluster { get; set; }

    public Vector3? WaypointSpawnPosition { get; set; }
    public Quaternion? WaypointSpawnRotation { get; set; }

    /// <summary>
    /// if model is originally shared across multiple entities, this cluster is detached from the original entity and chnages will apply only to this node
    /// </summary>
    public bool Detached { get; set; }
}

public sealed class VariantSetting
{
    public Guid VariantKey { get; set; }
    public Dictionary<string, string> Tags { get; set; } = [];
    public bool HiddenInManualCycle { get; set; }
}

public sealed class BuildSettings
{
    public Dictionary<Guid, InstanceSettings> Instances { get; set; } = [];          // key = MeshRef/ShapeRef/LightRef.Id
    public Dictionary<Guid, ClusterSettings> Clusters { get; set; } = [];  // key = arbitrary cluster Guid
    public List<VariantSetting> Variants { get; set; } = [];
    public Dictionary<Guid, Guid> EntityClusterAssignments { get; set; } = []; // entity.Id -> ClusterKey it renders

    public static BuildSettings DefaultFromItem(NormalizedItem item)
    {
        var settings = new BuildSettings();
        var clusterByModelKey = new Dictionary<int, Guid>();

        void Visit(NormalizedModel model, EntityRefBase refBase, int modelKey)
        {
            if (!clusterByModelKey.TryGetValue(modelKey, out var clusterKey))
            {
                clusterKey = Guid.NewGuid();
                clusterByModelKey[modelKey] = clusterKey;

                var entRef = refBase as EntityRef;

                settings.Clusters[clusterKey] = new ClusterSettings
                {
                    Type = model.Type,
                    LODDistances = model.LODDistances,
                    WaypointType = model.WaypointType,
                    WaypointNoRespawn = model.WaypointNoRespawn,
                    TriggerGameplayId = model.TriggerGameplayId,
                    KinematicConstraint = entRef?.KinematicConstraint,
                    DynaObjectModelParams = entRef?.DynaObjectModelParams,
                    RelativeMovingParentCluster = entRef != null && entRef.RelativeMovingParentKey.HasValue && clusterByModelKey.TryGetValue(entRef.RelativeMovingParentKey.Value, out var parentCluster) ? parentCluster : null,
                    WaypointSpawnPosition = entRef?.WaypointSpawnPosition,
                    WaypointSpawnRotation = entRef?.WaypointSpawnRotation,
                    GameplayMainDir = model.GameplayMainDir,

                };

                foreach (var m in model.Meshes)
                    settings.Instances[m.Id] = new InstanceSettings
                    {
                        Kind = RefKind.Mesh,
                        ClusterKey = clusterKey,
                        Visible = m.Properties.HasFlag(MeshProperties.Visible),
                        Collidable = m.Properties.HasFlag(MeshProperties.Collidable),
                        LODMaskOverride = m.Properties.HasFlag(MeshProperties.LOD) ? m.LODMask : null,
                        SmoothingGroupOverride = m.SmoothingGroup,
                        LightmapSizeOverride = m.PreLightGenerator?.U02,
                    };

                foreach (var s in model.Shapes)
                    settings.Instances[s.Id] = new InstanceSettings { Kind = RefKind.Shape, ClusterKey = clusterKey, Collidable = true, ShapeRoleOverride = s.Role, };

                foreach (var l in model.Lights)
                {
                    LightType type = item.LightPool[l.LightKey].LightModel.GetChunk<CPlugLightUserModel.Chunk090F9000>().U01 switch
                    {
                        0 => LightType.Point,
                        1 => LightType.Spot,
                        2 => LightType.Area,
                        _ => LightType.Point,
                    };
                    settings.Instances[l.Id] = new InstanceSettings { Kind = RefKind.Light, ClusterKey = clusterKey, LightTypeOverride = type };
                }

                foreach (var child in model.Children)
                    Visit(item.ModelPool[child.ModelKey], child, child.ModelKey);

                foreach (var v in model.Variants)
                {
                    settings.Variants.Add(new VariantSetting { VariantKey = v.Id, Tags = new(v.Tags), HiddenInManualCycle = v.HiddenInManualCycle });
                    Visit(item.ModelPool[v.ModelKey], v, v.ModelKey);
                }
            }

            settings.EntityClusterAssignments[refBase?.Id ?? Guid.Empty] = clusterKey;
        }

        var rootKey = item.ModelPool.First(kv => ReferenceEquals(kv.Value, item.Model)).Key;
        Visit(item.Model, null!, rootKey);
        return settings;
    }
}