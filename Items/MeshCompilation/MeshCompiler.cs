using GBX.NET.Engines.Meta;
using GBX.NET.Engines.MwFoundations;
using GBX.NET.Engines.Plug;
using System.Numerics;
using static GBX.NET.Engines.Game.CGameCtnMediaClipGroup;
using static TM_GenericMapping.Items.MeshBuilder;

namespace TM_GenericMapping.Items.MeshCompilation;

public enum ItemModel { General, MeshModeler, VariantList }

[Flags]
public enum MeshCompilerOptimization
{
    None = 0,
    PreferMeshCollision = 1 << 0
}
//public enum ClusteringMode
//{
//    Local,        // cluster only within each source entity; ClusterKey never merges across entities
//    GlobalMerge,  // ClusterKey honored across the whole item; merged clusters anchor at AnchorEntityKey
//    FlattenAll,   // GlobalMerge + collapse the output tree, except dynamic subtrees stay intact
//}
public struct CompileOptions()
{
    public ItemModel Target { get; init; } = ItemModel.General;
    public MeshCompilerOptimization Optimization { get; init; } = MeshCompilerOptimization.PreferMeshCollision;
}
public class MeshCompiler
{
    internal class CompileContext
    {
        public NodeRefTableV3 nodeRefTable = new();

        public void Reset()
        {
            nodeRefTable.Clear();
        }
    }

    CompileContext compileContext = new();

    public void Compile(NormalizedItemV3 item, BuildSettings buildSettings, CompileOptions compileOptions)
    {
        compileContext.Reset();

        var rootKey = item.ModelPool.First(kv => ReferenceEquals(kv.Value, item.Model)).Key;
        var rootEnt = BuildEntity(item, new EntityRef { Id = Guid.Empty, ModelKey = rootKey }, buildSettings);
    }


    private CMwNod BuildEntity(NormalizedItemV3 item, EntityRefBase entity, BuildSettings settings)
    {
        var contentKey = ComputeContentKey(item, entity.ModelKey, entity.Id, settings);

        if (compileContext.nodeRefTable.TryGetNode<CMwNod>(contentKey, out var cached))
            return cached; // structurally identical subtree, including all setting overrides -> reuse directly

        var model = item.ModelPool[entity.ModelKey];

        CMwNod built = model.Type switch
        {
            ModelTypeV3.Container => BuildPrefab(item, model.Children, settings),
            ModelTypeV3.Variant_List => BuildVariantList(item, model.Variants, settings),
            _ => BuildLeaf(item, model, settings),
        };

        compileContext.nodeRefTable.Register(contentKey, built);
        return built;
    }

   

    CPlugPrefab BuildPrefab(NormalizedItemV3 item, List<EntityRef> children, BuildSettings settings)
    {
        var ents = children.Select(c =>
        {
            var model = BuildEntity(item, c, settings);
            var entRef = GbxItemUtils.CreateEntRef();
            entRef.Position = c.Position;
            entRef.Rotation = c.Rotation;
            entRef.Model = model;
            return entRef;
        }).ToList();
        var prefab = GbxItemUtils.CreateCPlugPrefab();
        prefab.Ents = ents.ToArray();
        return prefab;
    }
    NPlugItem_SVariantList BuildVariantList(NormalizedItemV3 item, List<NormalizedVariantV3> variants, BuildSettings settings)
    {
        var builtVariants = variants.Select(v =>
        {
            var variant = GbxItemUtils.CreateVariant();
            variant.Tags = v.Tags;
            variant.HiddenInManualCycle = v.HiddenInManualCycle;
            variant.EntityModel = BuildEntity(item, v, settings);
            return variant;
        }).ToList();
        var variantList = GbxItemUtils.CreateVariantList();
        variantList.Variants = builtVariants.ToArray();
        return variantList;
    }

    CMwNod BuildLeaf(NormalizedItemV3 item, NormalizedModelV3 model, BuildSettings settings)
    {
        var instanceIds = model.Meshes.Select(m => m.Id)
            .Concat(model.Shapes.Select(s => s.Id))
            .Concat(model.Lights.Select(l => l.Id));

        var instances = instanceIds
            .Select(id => (Id: id, Settings: settings.Instances[id]))
            .Where(i => i.Settings.Enabled)
            .ToList();

        var byCluster = instances.GroupBy(i => i.Settings.ClusterKey).ToList();

        if (byCluster.Count == 0)
            return GbxItemUtils.CreateCPlugPrefab(); // empty container, nothing enabled

        if (byCluster.Count == 1)
            return BuildCluster(item, byCluster[0].ToList(), settings.Clusters[byCluster[0].Key], settings);

        var ents = byCluster.Select(g =>
        {
            var entRef = GbxItemUtils.CreateEntRef();
            entRef.Position = Vector3.Zero;
            entRef.Rotation = Quaternion.Identity;
            entRef.Model = BuildCluster(item, g.ToList(), settings.Clusters[g.Key], settings);
            return entRef;
        }).ToList();

        var prefab = GbxItemUtils.CreateCPlugPrefab();
        prefab.Ents = ents.ToArray();
        return prefab;
    }

    private string ComputeContentKey(NormalizedItemV3 item, int modelKey, Guid entityId, BuildSettings settings)
    {
        var model = item.ModelPool[modelKey];

        if (model.Type == ModelTypeV3.Container)
        {
            var childKeys = model.Children
                .Select(c => $"{c.Position}|{c.Rotation}|{ComputeContentKey(item, c.ModelKey, c.Id, settings)}");
            return $"container:{modelKey}:[{string.Join(";", childKeys)}]";
        }

        if (model.Type == ModelTypeV3.Variant_List)
        {
            var variantKeys = model.Variants.Select(v => ComputeContentKey(item, v.ModelKey, v.Id, settings));
            return $"variantlist:{modelKey}:[{string.Join(";", variantKeys)}]";
        }

        var instanceIds = model.Meshes.Select(m => m.Id).Concat(model.Shapes.Select(s => s.Id)).Concat(model.Lights.Select(l => l.Id)).ToList();

        bool anyDetached = instanceIds.Any(id => settings.Clusters[settings.Instances[id].ClusterKey].Detached);

        var instanceParts = instanceIds.OrderBy(id => id).Select(id =>
        {
            var instance = settings.Instances[id];
            var cluster = settings.Clusters[instance.ClusterKey];
            return $"{id}:{instance.Enabled}:{instance.Visible}:{instance.Collidable}:{instance.LODMaskOverride}:" +
                   $"{cluster.Movable}:{cluster.TriggerWaypoint}:{cluster.TriggerSpecial}:" +
                   $"{cluster.WaypointType}:{cluster.TriggerGameplayId}:{string.Join(",", cluster.LODDistances)}";
        });

        // detached -> fold entityId into the key so it NEVER collides with another entity's build,
        // even one with byte-identical settings
        var identityPart = anyDetached ? $"entity:{entityId}" : "";

        return $"leaf:{modelKey}:{identityPart}:[{string.Join(";", instanceParts)}]";
    }
}
