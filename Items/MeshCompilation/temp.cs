using GBX.NET.Engines.Meta;
using GBX.NET.Engines.Plug;
using System.Numerics;
using static GBX.NET.Engines.Plug.CPlugSurface;
using static GBX.NET.Engines.Plug.NPlugTrigger_SWaypoint;

namespace TM_GenericMapping.Items.MeshCompilation.Temp;


//// =====================================================================
//// SETTINGS — flat, no groups, no positional indices, checkbox-driven
//// =====================================================================

//[Flags]
//public enum InstanceRole
//{
//    None = 0,
//    Visible = 1 << 0,
//    Collidable = 1 << 1,
//    Movable = 1 << 2,
//    Trigger = 1 << 3,
//    TriggerSpecial = 1 << 4,
//}

//public enum RefKind { Mesh, Shape, Light }

//public struct InstanceRow
//{
//    public Guid RefId { get; set; }      // MeshRef.Id / ShapeRef.Id / LightRef.Id
//    public RefKind Kind { get; set; }
//    public InstanceRole Role { get; set; }
//    public int? LODMask { get; set; }    // meshes only
//}

//public struct VariantSetting()
//{
//    public Guid VariantEntryId { get; set; } // matches NormalizedVariantV3 identity if you add one
//    public Dictionary<string, string> Tags { get; set; } = [];
//    public bool HiddenInManualCycle { get; set; }
//}

//[Flags]
//public enum MeshBuilderOptimization
//{
//    None = 0,
//    PreferMeshCollision = 1 << 0,
//}

//public enum ItemModel { MeshModeler, General, VariantList }

//public struct BuildSettings()
//{
//    public List<InstanceRow> Rows { get; set; } = [];
//    public List<VariantSetting> Variants { get; set; } = [];
//    public ItemModel TargetModel { get; set; } = ItemModel.General;
//    public MeshBuilderOptimization Optimization { get; set; } = MeshBuilderOptimization.PreferMeshCollision;

//    // ---- default construction: mirrors truth exactly, one row per ref ----
//    public static BuildSettings DefaultFromItem(NormalizedItemV3 item)
//    {
//        var settings = new BuildSettings();

//        void Visit(NormalizedModelV3 model)
//        {
//            foreach (var m in model.Meshes)
//            {
//                var role = InstanceRole.None;
//                if (m.Properties.HasFlag(MeshPropertiesV3.Visible)) role |= InstanceRole.Visible;
//                if (m.Properties.HasFlag(MeshPropertiesV3.Collidable)) role |= InstanceRole.Collidable;
//                if (model.Type == ModelTypeV3.Dynamic) role |= InstanceRole.Movable;
//                if (model.Type == ModelTypeV3.Trigger_Waypoint) role |= InstanceRole.Trigger;
//                if (model.Type == ModelTypeV3.Trigger_Special) role |= InstanceRole.TriggerSpecial;

//                settings.Rows.Add(new InstanceRow
//                {
//                    RefId = m.Id,
//                    Kind = RefKind.Mesh,
//                    Role = role,
//                    LODMask = m.LODMask,
//                });
//            }

//            foreach (var s in model.Shapes)
//                settings.Rows.Add(new InstanceRow { RefId = s.Id, Kind = RefKind.Shape, Role = InstanceRole.Collidable });

//            foreach (var l in model.Lights)
//                settings.Rows.Add(new InstanceRow { RefId = l.Id, Kind = RefKind.Light, Role = InstanceRole.None });

//            foreach (var child in model.Children)
//                Visit(item.ModelPool[child.ModelKey]);

//            foreach (var v in model.Variants)
//                Visit(item.ModelPool[v.ModelKey]);
//        }

//        Visit(item.Model);
//        return settings;
//    }
//}


//// =====================================================================
//// OUTPUT (gbx-shaped) TYPES
//// =====================================================================

//public abstract class GameModel { }

//public class GamePrefab : GameModel { public List<GameEnt> Ents { get; set; } = []; }
//public class GameEnt { public Vector3 Position; public Quaternion Rotation; public required GameModel Model; }

//public class GameStaticObjectModel : GameModel { public GameSolidMesh? Mesh; public GameSurface? Shape; }
//public class GameDynamicObject : GameModel { public GameSolidMesh? Mesh; public GameSurface? StaticShape; public GameSurface? DynamicShape; }
//public class GameKinematicConstraint : GameModel
//{
//    public required int TargetEntIndex;
//    public required int ParentEntIndex;
//    public NPlugDyna_SKinematicConstraint? Data;
//}
//public class GameTriggerSpecial : GameModel { public required GameSurface TriggerShape; public LegacyGameplayId? GameplayId; }
//public class GameTriggerWaypoint : GameModel { public required GameSurface TriggerShape; public EGameItemWaypointType? WaypointType; public bool? NoRespawn; }
//public class GameSpawnModel : GameModel { public Vector3 Position; public Quaternion Rotation; }
//public class GameVariantList : GameModel { public List<GameVariant> Variants { get; set; } = []; }
//public class GameVariant { public required GameModel Model; public VariantSetting? Settings; }

//public class GameSolidMesh
//{
//    public List<GameShadedGeom> ShadedGeoms { get; set; } = [];
//    public float[] LodDistances { get; set; } = [];
//    public List<CPlugMaterialUserInst> Materials { get; set; } = [];
//    public List<GameVisual> Visuals { get; set; } = [];
//}
//public struct GameShadedGeom { public int MaterialIndex; public int VisualIndex; public int LodMask; }
//public class GameVisual { public required MeshData Data; }
//public class GameSurface { public required MeshData Data; public MaterialId[] SurfaceMaterialIds; public Vec3 GameplayMainDir; }


//// =====================================================================
//// COMPILER
//// =====================================================================

//public class CompileContext
//{
//    public List<(GameEnt Ent, EntityRef Source)> DynamicEnts { get; } = [];
//    public List<GameSpawnModel> SpawnCandidates { get; } = [];
//}

//public static class Compiler
//{
//    public static GamePrefab Compile(NormalizedItemV3 item, BuildSettings settings)
//    {
//        var ctx = new CompileContext();
//        var rootEnt = CompileEntity(item, ToRootEntityRef(item), settings, ctx);

//        var root = rootEnt.Model as GamePrefab ?? new GamePrefab { Ents = [rootEnt] };
//        FinalizeKinematics(root, ctx);
//        FinalizeSpawn(root, ctx);
//        return root;
//    }

//    // synthetic EntityRef for the item's top-level Model, since Item stores it directly (not via EntityRef)
//    private static EntityRef ToRootEntityRef(NormalizedItemV3 item) =>
//        new EntityRef { ModelKey = item.ModelPool.First(kv => ReferenceEquals(kv.Value, item.Model)).Key };

//    // ---------------------------------------------------------------
//    // Entity / model recursion
//    // ---------------------------------------------------------------

//    private static GameEnt CompileEntity(NormalizedItemV3 item, EntityRef entity, BuildSettings settings, CompileContext ctx)
//    {
//        var model = item.ModelPool[entity.ModelKey];

//        if (model.Type == ModelTypeV3.Container && model.Children.Count > 0)
//        {
//            var prefab = new GamePrefab
//            {
//                Ents = model.Children.Select(c => CompileEntity(item, c, settings, ctx)).ToList()
//            };
//            return new GameEnt { Position = entity.Position, Rotation = entity.Rotation, Model = prefab };
//        }

//        if (model.Type == ModelTypeV3.Variant_List && model.Variants.Count > 0)
//        {
//            var variantList = new GameVariantList
//            {
//                Variants = model.Variants.Select(v => new GameVariant
//                {
//                    Model = CompileEntity(item, ToEntityRef(v), settings, ctx).Model,
//                }).ToList()
//            };
//            return new GameEnt { Position = entity.Position, Rotation = entity.Rotation, Model = variantList };
//        }

//        // leaf model: derive clusters from row roles, build whichever GameModel kinds result
//        var rows = RowsForModel(item, model, settings);
//        var clusters = DeriveClusters(rows).ToList();

//        // In the common case there's exactly one cluster -> this entity IS that model.
//        // If splitting occurred (e.g. trigger split off from a static model), wrap in a Container prefab
//        // so the split-off piece still gets its own sibling entity, co-located with the source.
//        var builtModels = clusters.Select(c => BuildCluster(item, c, model, settings)).ToList();

//        GameModel resultModel = builtModels.Count == 1
//            ? builtModels[0]
//            : new GamePrefab
//            {
//                Ents = builtModels.Select(bm => new GameEnt { Position = Vector3.Zero, Rotation = Quaternion.Identity, Model = bm }).ToList()
//            };

//        var gameEnt = new GameEnt { Position = entity.Position, Rotation = entity.Rotation, Model = resultModel };

//        foreach (var bm in builtModels)
//            if (bm is GameDynamicObject)
//                ctx.DynamicEnts.Add((gameEnt, entity));

//        if (resultModel is GameSpawnModel spawn)
//            ctx.SpawnCandidates.Add(spawn);

//        return gameEnt;
//    }

//    private static EntityRef ToEntityRef(NormalizedVariantV3 v) => new EntityRef { ModelKey = v.ModelKey };

//    // ---------------------------------------------------------------
//    // Row lookup + clustering
//    // ---------------------------------------------------------------

//    private static List<InstanceRow> RowsForModel(NormalizedItemV3 item, NormalizedModelV3 model, BuildSettings settings)
//    {
//        var meshIds = model.Meshes.Select(m => m.Id).ToHashSet();
//        var shapeIds = model.Shapes.Select(s => s.Id).ToHashSet();
//        var lightIds = model.Lights.Select(l => l.Id).ToHashSet();

//        return settings.Rows.Where(r =>
//            (r.Kind == RefKind.Mesh && meshIds.Contains(r.RefId)) ||
//            (r.Kind == RefKind.Shape && shapeIds.Contains(r.RefId)) ||
//            (r.Kind == RefKind.Light && lightIds.Contains(r.RefId))
//        ).ToList();
//    }

//    // Splits rows into clusters that must become separate output models.
//    // Default: everything stays together (one cluster). Only trigger-without-movable splits off,
//    // since a static visual/collision model and a static trigger can't coexist in one output node.
//    private static IEnumerable<List<InstanceRow>> DeriveClusters(List<InstanceRow> rows)
//    {
//        bool anyMovable = rows.Any(r => r.Role.HasFlag(InstanceRole.Movable));
//        var triggerRows = rows.Where(r => r.Role.HasFlag(InstanceRole.Trigger) || r.Role.HasFlag(InstanceRole.TriggerSpecial)).ToList();
//        var rest = rows.Except(triggerRows).ToList();

//        if (triggerRows.Count > 0 && !anyMovable && rest.Count > 0)
//        {
//            yield return rest;
//            yield return triggerRows;
//        }
//        else
//        {
//            yield return rows; // triggers absent, or funneled into a dyna shape below -> stays one cluster
//        }
//    }

//    // ---------------------------------------------------------------
//    // Cluster -> concrete GameModel
//    // ---------------------------------------------------------------

//    private static GameModel BuildCluster(NormalizedItemV3 item, List<InstanceRow> cluster, NormalizedModelV3 sourceModel, BuildSettings settings)
//    {
//        bool movable = cluster.Any(r => r.Role.HasFlag(InstanceRole.Movable));
//        bool trigger = cluster.Any(r => r.Role.HasFlag(InstanceRole.Trigger));
//        bool triggerSpecial = cluster.Any(r => r.Role.HasFlag(InstanceRole.TriggerSpecial));

//        if (trigger && !movable)
//            return new GameTriggerWaypoint
//            {
//                TriggerShape = ResolveCollision(item, cluster, settings)!,
//                WaypointType = sourceModel.WaypointType,
//                NoRespawn = sourceModel.WaypointNoRespawn,
//            };

//        if (triggerSpecial && !movable)
//            return new GameTriggerSpecial
//            {
//                TriggerShape = ResolveCollision(item, cluster, settings)!,
//                GameplayId = sourceModel.TriggerGameplayId,
//            };

//        if (movable)
//            return new GameDynamicObject
//            {
//                Mesh = BuildSolidMesh(item, cluster),
//                StaticShape = ResolveExplicitShape(item, cluster, ShapeRoleV3.Static),
//                DynamicShape = (trigger || triggerSpecial)
//                    ? ResolveCollision(item, cluster, settings)          // funneled: trigger geometry becomes the dyna shape
//                    : ResolveExplicitShape(item, cluster, ShapeRoleV3.Dynamic)
//                      ?? ResolveMeshDerivedCollision(item, cluster, settings),
//            };

//        return new GameStaticObjectModel
//        {
//            Mesh = BuildSolidMesh(item, cluster),
//            Shape = ResolveCollision(item, cluster, settings),
//        };
//    }

//    // ---------------------------------------------------------------
//    // SolidMesh: ONE visual per mesh, no geometry merging
//    // ---------------------------------------------------------------

//    private static GameSolidMesh BuildSolidMesh(NormalizedItemV3 item, List<InstanceRow> cluster)
//    {
//        var solid = new GameSolidMesh();
//        var materialIndex = new Dictionary<CPlugMaterialUserInst, int>();

//        foreach (var row in cluster.Where(r => r.Kind == RefKind.Mesh && r.Role.HasFlag(InstanceRole.Visible)))
//        {
//            var meshRef = FindMeshRef(item, row.RefId);
//            var mesh = item.MeshPool[meshRef.MeshKey];

//            int visualIndex = solid.Visuals.Count;
//            solid.Visuals.Add(new GameVisual { Data = ToMeshData(mesh) }); // always its own visual, per your correction

//            if (!materialIndex.TryGetValue(mesh.Material, out int matIdx))
//            {
//                matIdx = solid.Materials.Count;
//                solid.Materials.Add(mesh.Material);
//                materialIndex[mesh.Material] = matIdx;
//            }

//            solid.ShadedGeoms.Add(new GameShadedGeom
//            {
//                MaterialIndex = matIdx,
//                VisualIndex = visualIndex,
//                LodMask = row.LODMask ?? 1,
//            });
//        }

//        solid.LodDistances = DeriveLodDistances(cluster); // e.g. pulled from source model, unchanged
//        return solid.ShadedGeoms.Count > 0 ? solid : null!;
//    }

//    // ---------------------------------------------------------------
//    // Collision resolution: shape + mesh-derived, merged per Optimization flag
//    // ---------------------------------------------------------------

//    private static GameSurface? ResolveCollision(NormalizedItemV3 item, List<InstanceRow> cluster, BuildSettings settings)
//    {
//        var meshCollision = ResolveMeshDerivedCollision(item, cluster, settings);
//        var explicitShape = ResolveExplicitShape(item, cluster, ShapeRoleV3.Static); // Collision-role shape

//        if (meshCollision is not null && explicitShape is not null)
//            return settings.Optimization.HasFlag(MeshBuilderOptimization.PreferMeshCollision)
//                ? meshCollision
//                : explicitShape;

//        return meshCollision ?? explicitShape;
//    }

//    private static GameSurface? ResolveMeshDerivedCollision(NormalizedItemV3 item, List<InstanceRow> cluster, BuildSettings settings)
//    {
//        var collidableMeshRows = cluster.Where(r => r.Kind == RefKind.Mesh && r.Role.HasFlag(InstanceRole.Collidable)).ToList();
//        if (collidableMeshRows.Count == 0) return null;

//        var meshes = collidableMeshRows.Select(r => item.MeshPool[FindMeshRef(item, r.RefId).MeshKey]);
//        return new GameSurface { Data = MergeIntoCollisionGeometry(meshes), SurfaceMaterialIds = [], GameplayMainDir = new Vec3(0, 0, 1) };
//    }

//    private static GameSurface? ResolveExplicitShape(NormalizedItemV3 item, List<InstanceRow> cluster, ShapeRoleV3 role)
//    {
//        var shapeRow = cluster.FirstOrDefault(r => r.Kind == RefKind.Shape &&
//            FindShapeRef(item, r.RefId)?.Role == role);
//        if (shapeRow.RefId == Guid.Empty) return null;

//        var shapeRef = FindShapeRef(item, shapeRow.RefId)!;
//        var shape = item.ShapePool[shapeRef.ShapeKey];
//        return new GameSurface { Data = ToMeshData(shape), SurfaceMaterialIds = shape.SurfaceMaterialIds, GameplayMainDir = shape.GameplayMainDir };
//    }

//    // ---------------------------------------------------------------
//    // Ref lookup helpers (Guid -> concrete ref object, scanning pools once)
//    // ---------------------------------------------------------------

//    private static MeshRef FindMeshRef(NormalizedItemV3 item, Guid id) =>
//        item.ModelPool.Values.SelectMany(m => m.Meshes).First(m => m.Id == id);

//    private static ShapeRef? FindShapeRef(NormalizedItemV3 item, Guid id) =>
//        item.ModelPool.Values.SelectMany(m => m.Shapes).FirstOrDefault(s => s.Id == id);

//    // ---------------------------------------------------------------
//    // Post-pass: flatten ents, resolve KinematicConstraint indices
//    // ---------------------------------------------------------------

//    private static void FinalizeKinematics(GamePrefab root, CompileContext ctx)
//    {
//        var flat = FlattenEnts(root).ToList();
//        var indexOf = flat.Select((e, i) => (e, i)).ToDictionary(x => x.e, x => x.i);

//        foreach (var (ent, source) in ctx.DynamicEnts)
//        {
//            if (source.KinematicConstraint is null) continue;

//            int targetIdx = indexOf[ent];
//            int parentIdx = source.RelativeMovingParentKey is int pk && TryFindDynamicEntByKey(ctx, pk, out var parentEnt)
//                ? indexOf[parentEnt]
//                : -1;

//            root.Ents.Add(new GameEnt
//            {
//                Position = Vector3.Zero,
//                Rotation = Quaternion.Identity,
//                Model = new GameKinematicConstraint { TargetEntIndex = targetIdx, ParentEntIndex = parentIdx, Data = source.KinematicConstraint }
//            });
//        }
//    }

//    private static bool TryFindDynamicEntByKey(CompileContext ctx, int key, out GameEnt ent)
//    {
//        // NOTE: adapt this to however you key "which dynamic entity is the parent" —
//        // e.g. match on EntityRef.Id if RelativeMovingParentKey stores a Guid-derived int, or similar.
//        foreach (var (e, _) in ctx.DynamicEnts) { ent = e; return true; /* placeholder match logic */ }
//        ent = null!;
//        return false;
//    }

//    private static IEnumerable<GameEnt> FlattenEnts(GameModel model)
//    {
//        switch (model)
//        {
//            case GamePrefab p:
//                foreach (var ent in p.Ents)
//                {
//                    yield return ent;
//                    foreach (var nested in FlattenEnts(ent.Model))
//                        yield return nested;
//                }
//                break;
//            case GameVariantList v:
//                foreach (var variant in v.Variants)
//                    foreach (var nested in FlattenEnts(variant.Model))
//                        yield return nested;
//                break;
//        }
//    }

//    private static void FinalizeSpawn(GamePrefab root, CompileContext ctx)
//    {
//        if (ctx.SpawnCandidates.Count > 1)
//            throw new InvalidOperationException("Only one SpawnModel allowed per item");
//        // single spawn candidate is already an ent inside the tree from normal compilation
//    }

//    // ---------------------------------------------------------------
//    // Placeholder data conversion stubs — wire up to your actual mesh/shape data
//    // ---------------------------------------------------------------

//    private static MeshData ToMeshData(NormalizedMeshV3 mesh) => throw new NotImplementedException();
//    private static MeshData ToMeshData(NormalizedShapeV3 shape) => throw new NotImplementedException();
//    private static MeshData MergeIntoCollisionGeometry(IEnumerable<NormalizedMeshV3> meshes) => throw new NotImplementedException();
//    private static float[] DeriveLodDistances(List<InstanceRow> cluster) => [];
//}