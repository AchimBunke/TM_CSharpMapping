using GBX.NET;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.MetaNotPersistent;
using GBX.NET.Engines.MwFoundations;
using GBX.NET.Engines.Plug;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using TM_GenericMapping.Common;
using TM_GenericMapping.Items.FbxGbxConversion;
using TM_GenericMapping.Messaging;
using TM_GenericMapping.Templating;
using static GBX.NET.Engines.GameData.CGameItemModel;
using static GBX.NET.Engines.Plug.CPlugPrefab;
using static GBX.NET.Engines.Plug.CPlugSkel;
using static GBX.NET.Engines.Plug.CPlugSolid2Model;
using static GBX.NET.Engines.Plug.CPlugSurface;
using static GBX.NET.Engines.Plug.CPlugVertexStream;

namespace TM_GenericMapping.Items.MeshCompilation;

public enum ItemModel { Prefab, MeshModeler }

[Flags]
public enum ItemCompilerOptimization
{
    None = 0,

    PreferNadeoImporterStructure = 1 << 0,

    /// <summary>
    /// if PreferNadeoImporterStructure is enabled, allow mergin of multiple static and trigger parts into one to allow mesh collision.
    /// Might increase file-size if there are reuses parts.
    /// </summary>
    AllowMerging = 1 << 1,

    /// <summary>
    /// Prevents sharing of mesh/object/shape instances.
    /// </summary>
    ProhibitInstanceSharing = 1 << 2,
}

public struct CompileOptions()
{
    public ItemModel Target { get; init; } = ItemModel.Prefab;
    public ItemCompilerOptimization Optimization { get; init; } = ItemCompilerOptimization.PreferNadeoImporterStructure;
}
public struct ItemCompilerSettings
{
    public string Author;
};
public class ItemCompiler
{
    internal class PendingConstraint
    {
        public required NPlugDyna_SKinematicConstraint Constraint { get; init; }
        public Guid? ParentCluster { get; init; }
    }
    internal class PendingWaypoint
    {
        public Vector3? SpawnPosition { get; init; }
        public Quaternion? SpawnRotation { get; init; }
    }
    internal class CompileContext
    {
        public NodeRefTable nodeRefTable = new();

        public List<Guid> BuildDynaObjectClusters { get; } = new(); // includes also copied
        public Dictionary<CPlugDynaObjectModel, PendingConstraint> PendingConstraints { get; } = new();
        public Dictionary<NPlugTrigger_SWaypoint, PendingWaypoint> PendingWaypoints { get; } = new();

        public Dictionary<Guid, RefBase> GuidToRef { get; private set; } = new();
        public CompileOptions CompileOptions { get; set; }
        public void Reset()
        {
            nodeRefTable.Clear();
            PendingConstraints.Clear();
            PendingWaypoints.Clear();
            GuidToRef.Clear();
            BuildDynaObjectClusters.Clear();
            CompileOptions = new();
        }
        public void Rebuild(NormalizedItem item, CompileOptions compileOptions)
        {
            Reset();
            CompileOptions = compileOptions;
            GuidToRef = item.ModelPool.SelectMany(m => m.Value.Meshes.OfType<RefBase>().Concat(m.Value.Shapes).Concat(m.Value.Lights))
                .ToDictionary(i => i.Id, i => i);
        }
    }




    CompileContext compileContext;
    ItemCompilerSettings _settings;
    Ident _ident;


    public ItemCompiler() : this(new()) { }
    public ItemCompiler(ItemCompilerSettings settings)
    {
        _settings = settings;
        _ident = new Ident("", 26, _settings.Author ?? "TM_CSharpMapping");
        compileContext = new();
    }

    public ToolResult<CGameItemModel> Compile(NormalizedItem item, BuildSettings buildSettings, CompileOptions compileOptions)
    {
        switch (compileOptions.Target)
        {
            case ItemModel.Prefab:
                return CompilePrefabItem(item, buildSettings, compileOptions);
            case ItemModel.MeshModeler:
                return CompileMeshModelerItem(item, buildSettings, compileOptions);
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    public ToolResult<CGameItemModel> CompilePrefabItem(NormalizedItem item, BuildSettings buildSettings, CompileOptions compileOptions)
    {
        compileContext.Rebuild(item, compileOptions);

        var rootKey = item.ModelPool.First(kv => ReferenceEquals(kv.Value, item.Model)).Key;
        var rootEntResult = BuildEntity(item, new EntityRef { Id = Guid.Empty, ModelKey = rootKey }, buildSettings, out _);

        if (rootEntResult.IsFailure)
            return ToolResult.Fail(rootEntResult);

        CGameItemModel itemTemplate = null!;
        if (compileOptions.Optimization.HasFlag(ItemCompilerOptimization.PreferNadeoImporterStructure) &&
            CanConvertToCommonEntityModel(item, buildSettings, compileOptions))
        {
            var itemTemplateResult = ConvertToCommonEntityModel(item, rootEntResult.Value as CPlugPrefab, buildSettings);
            if(itemTemplateResult.IsFailure)
                return ToolResult.Fail(itemTemplateResult);
            itemTemplate = itemTemplateResult.Value;
        }
        else
        {
            itemTemplate = CreateItemTemplate().Value;


            itemTemplate.EntityModel = rootEntResult.Value;

            FinalizeKinematicConstraints(itemTemplate);
            FinalizeWaypoints(itemTemplate);
        }

        FillItemData(itemTemplate, item, buildSettings);

        return ToolResult.Success(itemTemplate, nameof(ItemCompiler));
    }
    public ToolResult<CGameItemModel> CompileMeshModelerItem(NormalizedItem item, BuildSettings buildSettings, CompileOptions compileOptions)
    {
        compileContext.Rebuild(item, compileOptions);


        var rootKey = item.ModelPool.First(kv => ReferenceEquals(kv.Value, item.Model)).Key;
        var rootCrystal = BuildCrystal(item, new EntityRef { Id = Guid.Empty, ModelKey = rootKey }, buildSettings);

        var itemTemplate = GbxTemplateLibrary.CreateCommonItemEntityModelEditionItemTemplate().Value;
        (itemTemplate.EntityModelEdition as CGameCommonItemEntityModelEdition)!.MeshCrystal = rootCrystal;

        FillItemData(itemTemplate, item, buildSettings);

        return ToolResult.Success(itemTemplate, nameof(ItemCompiler));
    }


    GbxTemplate<CGameItemModel> CreateItemTemplate() 
    {
        var itemTemplate = GbxTemplateLibrary.CreateMovingItemTemplate();
        //itemTemplate.Value.GetChunk<CGameItemModel.Chunk2E00201F>().U08 = 1;
        //itemTemplate.Value.GetChunk<CGameItemModel.Chunk2E002020>().U03 = false;
        return itemTemplate;
    }
    private ToolResult<CMwNod> BuildEntity(NormalizedItem item, EntityRefBase entity, BuildSettings settings, out SMetaPtr? modelParams)
    {
        var model = item.ModelPool[entity.ModelKey];
        modelParams = null;

        if (model.Type is ModelType.Container or ModelType.Variant_List)
        {
            var contentKey = ComputeContentKey(item, entity.ModelKey, entity.Id, settings);
            if (!compileContext.CompileOptions.Optimization.HasFlag(ItemCompilerOptimization.ProhibitInstanceSharing) &&
                compileContext.nodeRefTable.TryGetNode<CMwNod>(contentKey, out var cached))
                return ToolResult.Success(cached, nameof(ItemCompiler));

            ToolResult<CMwNod> builtResult;
            if (model.Type == ModelType.Container)
                builtResult = BuildPrefab(item, model.Children, settings, out modelParams).Cast<CMwNod>();

            else
                builtResult = BuildVariantList(item, model.Variants, settings).Cast<CMwNod>();

            if (builtResult.IsFailure)
                return ToolResult.Fail(builtResult);

            compileContext.nodeRefTable.Register(contentKey, builtResult.Value);
            return ToolResult.Success(builtResult.Value, nameof(ItemCompiler));
        }

        // leaf: dedup happens per-cluster inside BuildLeaf/BuildCluster, no outer registration needed here
        return BuildLeaf(item, model, settings, out modelParams);
    }

    ToolResult<CPlugPrefab> BuildPrefab(NormalizedItem item, List<EntityRef> children, BuildSettings settings, out SMetaPtr? modelParams)
    {
        modelParams = null;
        List<EntRef> ents = [];
        foreach (var child in children)
        {
            var modelResult = BuildEntity(item, child, settings, out var childModelParams);
            if (modelResult.IsFailure)
                return ToolResult.Fail(modelResult);

            var entRef = GbxItemUtils.CreateEntRef();
            entRef.Position = child.Position;
            entRef.Rotation = child.Rotation;
            entRef.Model = modelResult.Value;
            entRef.Params = childModelParams;

            ents.Add(entRef);
        }

        var prefab = GbxItemUtils.CreateCPlugPrefab();
        prefab.Ents = ents.ToArray();
        return ToolResult.Success(prefab, nameof(ItemCompiler));
    }
    ToolResult<NPlugItem_SVariantList> BuildVariantList(NormalizedItem item, List<NormalizedVariant> variants, BuildSettings settings)
    {
        List<NPlugItem_SVariant> builtVariants = [];
        foreach (var v in variants)
        {
            var variant = GbxItemUtils.CreateVariant();
            variant.Tags = v.Tags;
            variant.HiddenInManualCycle = v.HiddenInManualCycle;
            var entityResult = BuildEntity(item, v, settings, out _);
            if (entityResult.IsFailure)
                return ToolResult.Fail(entityResult);
            variant.EntityModel = entityResult.Value;
            builtVariants.Add(variant);
        }
        var variantList = GbxItemUtils.CreateVariantList();
        variantList.Variants = builtVariants.ToArray();
        return ToolResult.Success(variantList, nameof(ItemCompiler));
    }

    ToolResult<CMwNod> BuildLeaf(NormalizedItem item, NormalizedModel model, BuildSettings settings, out SMetaPtr? modelParams)
    {
        modelParams = null;

        var instanceIds = model.Meshes.Select(m => m.Id)
            .Concat(model.Shapes.Select(s => s.Id))
            .Concat(model.Lights.Select(l => l.Id));

        var instances = instanceIds
            .Select(id => (Id: id, Settings: settings.Instances[id]))
            .Where(i => i.Settings.Enabled)
            .ToList();

        var byCluster = instances.GroupBy(i => i.Settings.ClusterKey).ToList();

        if (byCluster.Count == 0)
            return ToolResult.Success<CMwNod>(GbxItemUtils.CreateCPlugPrefab(), nameof(ItemCompiler)); // empty container, nothing enabled

        if (byCluster.Count == 1)
            return BuildCluster(item, byCluster[0].Key, byCluster[0].ToList(), settings.Clusters[byCluster[0].Key], settings, out modelParams);

        List<EntRef> ents = [];
        foreach (var g in byCluster)
        {
            var entRef = GbxItemUtils.CreateEntRef();
            entRef.Position = Vector3.Zero;
            entRef.Rotation = Quaternion.Identity;
            var clusterResult = BuildCluster(item, g.Key, g.ToList(), settings.Clusters[g.Key], settings, out var childModelParams);
            if (clusterResult.IsFailure)
                return ToolResult.Fail(clusterResult);
            entRef.Model = clusterResult.Value;
            entRef.Params = childModelParams;
            ents.Add(entRef);
        }

        var prefab = GbxItemUtils.CreateCPlugPrefab();
        prefab.Ents = ents.ToArray();
        return ToolResult.Success<CMwNod>(prefab, nameof(ItemCompiler));
    }

    private ToolResult<CMwNod> BuildCluster(
        NormalizedItem item,
        Guid clusterKey,
        List<(Guid Id, InstanceSettings Instance)> instances,
        ClusterSettings cluster,
        BuildSettings settings,
        out SMetaPtr? modelParams)
    {
        modelParams = null;
        switch (cluster.Type)
        {
            case ModelType.Dynamic:
                {
                    modelParams = cluster.DynaObjectModelParams ?? new NPlugDynaObjectModel_SInstanceParams
                    {
                        Version = 2,
                        CastStaticShadow = true,
                        IsKinematic = true,
                        PeriodSc = 1,
                        PeriodScMax = 1,
                        Phase01 = -1,
                        Phase01Max = -1,
                        TextureId = 0,
                    };
                    return GetOrCreateDynaObjectModel(item, clusterKey, instances, cluster, settings).Cast<CMwNod>();
                }
            case ModelType.Trigger_Waypoint:
                return GetOrCreateTriggerWaypoint(item, clusterKey, instances, cluster, settings).Cast<CMwNod>();
            case ModelType.Trigger_Special:
                return GetOrCreateTriggerSpecial(item, clusterKey, instances, cluster, settings).Cast<CMwNod>();
            default:
                return GetOrCreateStaticObjectModel(item, clusterKey, instances, cluster, settings).Cast<CMwNod>();
        }

    }

    ToolResult<CPlugStaticObjectModel> GetOrCreateStaticObjectModel(NormalizedItem item, Guid clusterKey, List<(Guid Id, InstanceSettings Instance)> instances, ClusterSettings cluster, BuildSettings buildSettings)
    {
        var key = ComputeClusterContentKey(clusterKey, instances, cluster, buildSettings);

        if (!compileContext.CompileOptions.Optimization.HasFlag(ItemCompilerOptimization.ProhibitInstanceSharing) && 
            compileContext.nodeRefTable.TryGetNode<CPlugStaticObjectModel>(key, out var cached))
            return ToolResult.Success(cached, nameof(ItemCompiler));

        // build
        var staticObjectResult = CreateStaticObjectModel(item, instances, cluster, key, buildSettings);
        if (staticObjectResult.IsFailure)
            return ToolResult.Fail(staticObjectResult);
        var staticObject = staticObjectResult.Value;

        // register
        compileContext.nodeRefTable.Register(key, staticObject);


        return ToolResult.Success(staticObject, nameof(ItemCompiler));

    }
    ToolResult<CPlugStaticObjectModel> CreateStaticObjectModel(NormalizedItem item, List<(Guid Id, InstanceSettings Instance)> instances, ClusterSettings cluster, string clusterKey, BuildSettings buildSettings)
    {
        var staticObject = GbxTemplateLibrary.CreateStaticObjectModelTemplate().Value;

        staticObject.IsMeshCollidable = false;

        var meshInstances = instances
            .Where(i => i.Instance.Visible || i.Instance.Kind == RefKind.Light)
            .ToList();
        var solidResult = GetOrCreateSolid2Model(item, meshInstances, cluster, buildSettings);
        if (solidResult.IsFailure)
            return ToolResult.Fail(solidResult);
        var solid = solidResult.Value;

        staticObject.Mesh = solid;

        var collidable = instances.Where(i => i.Instance.Collidable).ToList();
        var staticCollidableInstances = collidable.Where(i => ResolveDestination(item, i, ShapeRole.Static) == ShapeRole.Static).ToList();


        var surface = GetOrCreateSurface(item, staticCollidableInstances, buildSettings);
        staticObject.Shape = surface;

        return ToolResult.Success(staticObject, nameof(ItemCompiler));
    }


    ToolResult<CPlugDynaObjectModel> GetOrCreateDynaObjectModel(NormalizedItem item, Guid clusterKey, List<(Guid Id, InstanceSettings Instance)> instances, ClusterSettings cluster, BuildSettings buildSettings)
    {
        var key = ComputeClusterContentKey(clusterKey, instances, cluster, buildSettings);

        if (!compileContext.CompileOptions.Optimization.HasFlag(ItemCompilerOptimization.ProhibitInstanceSharing) &&
            compileContext.nodeRefTable.TryGetNode<CPlugDynaObjectModel>(key, out var cached))
        {
            compileContext.BuildDynaObjectClusters.Add(clusterKey);
            return ToolResult.Success(cached, nameof(ItemCompiler));
        }

        // build
        var dynaObjectResult = CreateDynaObjectModel(item, instances, cluster, key, buildSettings);
        if (dynaObjectResult.IsFailure)
            return ToolResult.Fail(dynaObjectResult);
        var dynaObject = dynaObjectResult.Value;

        compileContext.BuildDynaObjectClusters.Add(clusterKey);

        // register
        compileContext.nodeRefTable.Register(key, dynaObject);


        compileContext.PendingConstraints.Add(dynaObject, new PendingConstraint
        {
            Constraint = cluster.KinematicConstraint!,
            ParentCluster = cluster.RelativeMovingParentCluster,
        });

        return ToolResult.Success(dynaObject, nameof(ItemCompiler));

    }

    ToolResult<CPlugDynaObjectModel> CreateDynaObjectModel(NormalizedItem item, List<(Guid Id, InstanceSettings Instance)> instances, ClusterSettings cluster, string clusterKey, BuildSettings buildSettings)
    {
        var dyna = GbxTemplateLibrary.CreateDynaObjectModelTemplate().Value;

        var meshInstances = instances
            .Where(i => i.Instance.Visible || i.Instance.Kind == RefKind.Light)
            .ToList();
        var solidResult = GetOrCreateSolid2Model(item, meshInstances, cluster, buildSettings);
        if (solidResult.IsFailure)
            return ToolResult.Fail(solidResult);
        var solid = solidResult.Value;

        dyna.Mesh = solid;

        var collidable = instances.Where(i => i.Instance.Collidable).ToList();

        var staticInstances = collidable.Where(i => ResolveDestination(item, i, ShapeRole.Static) == ShapeRole.Static).ToList();
        var dynamicInstances = collidable.Where(i => ResolveDestination(item, i, ShapeRole.Dynamic) == ShapeRole.Dynamic).ToList();

        if (dynamicInstances.Count == 0)
            return ToolResult.Fail(nameof(ItemCompiler), ErrorCodes.ItemCompiler.MissingDynaCollisionShape);

        var staticSurface = GetOrCreateSurface(item, staticInstances, buildSettings);
        dyna.StaticShape = staticSurface;

        var dynamicSurface = GetOrCreateSurface(item, dynamicInstances, buildSettings);
        dyna.DynaShape = dynamicSurface;

        return ToolResult.Success(dyna, nameof(ItemCompiler));
    }
    ToolResult<CPlugSolid2Model> GetOrCreateSolid2Model(NormalizedItem item, List<(Guid Id, InstanceSettings Instance)> instances, ClusterSettings cluster, BuildSettings buildSettings)
    {
        if (instances.Count == 0)
            return ToolResult.Success(CreateEmptySolid2Model(item), nameof(ItemCompiler));

        var key = ComputeSolidMeshKey(instances);
        if (!compileContext.CompileOptions.Optimization.HasFlag(ItemCompilerOptimization.ProhibitInstanceSharing) &&
            compileContext.nodeRefTable.TryGetNode<CPlugSolid2Model>(key, out var cached))
            return ToolResult.Success(cached, nameof(ItemCompiler));

        var solid = CreateSolid2Model(item, instances, cluster, buildSettings);

        compileContext.nodeRefTable.Register(key, solid);
        return ToolResult.Success(solid, nameof(ItemCompiler));
    }

    CPlugSolid2Model CreateSolid2Model(
        NormalizedItem item,
        List<(Guid Id, InstanceSettings Instance)> instances,
        ClusterSettings cluster,
        BuildSettings buildSettings)
    {
        var solid = CreateEmptySolid2Model(item);

        Dictionary<CPlugMaterialUserInst, int> materialInstances = [];
        Dictionary<CPlugVisual, int> visualIndexInThisSolid = [];

        List<CPlugVisual> visuals = [];
        List<CPlugMaterialUserInst> materials = [];
        List<CPlugSolid2Model.ShadedGeom> shadedGeom = [];

        List<CPlugLightUserModel> lightUserModels = [];
        List<Socket> sockets = [];
        List<LightInst> lightInsts = [];

        var visibleInstances = instances
            .Where(i => i.Instance.Visible && (i.Instance.Kind == RefKind.Mesh || i.Instance.Kind == RefKind.Shape));

        PreLightGen? preLightGen = null;
        foreach (var (id, instance) in visibleInstances)
        {
            var refBase = compileContext.GuidToRef[id];
            var meshRef = refBase as MeshRef;
            NormalizedMesh? mesh = meshRef != null ? item.MeshPool[meshRef.MeshKey] : null;

            var visual = GetOrCreateIndexedTriangles(item, refBase, instance, buildSettings, out CPlugMaterialUserInst material);

            if (!visualIndexInThisSolid.TryGetValue(visual, out var visualIndex))
            {
                visualIndex = visuals.Count;
                visuals.Add(visual);
                visualIndexInThisSolid.Add(visual, visualIndex);
            }

            if (!materialInstances.TryGetValue(material, out int matIdx))
            {
                matIdx = materials.Count;
                materials.Add(material);
                materialInstances[material] = matIdx;
            }

            int lodMask = instance.LODMaskOverride.HasValue ?
                instance.LODMaskOverride.Value :
                mesh != null ?
                    meshRef!.LODMask :
                    0;
            shadedGeom.Add(new CPlugSolid2Model.ShadedGeom
            {
                VisualIndex = visualIndex,
                MaterialIndex = matIdx,
                LodMask = lodMask,
                U01 = -1,
            });

            if (mesh != null)
                preLightGen = MergePreLightGenerator(mesh, instance, preLightGen);
        }

        var lightInstances = instances
            .Where(i => i.Instance.Kind == RefKind.Light);

        foreach (var (id, instance) in lightInstances)
        {
            var refBase = compileContext.GuidToRef[id];
            var lightRef = refBase as LightRef;
            NormalizedLight light = item.LightPool[lightRef!.LightKey];

            var lightInst = new LightInst() { ModelIndex = lightUserModels.Count, SocketIndex = sockets.Count };
            lightInsts.Add(lightInst);

            lightUserModels.Add(GbxItemUtils.CreateLightUserModel(light, instance));
            sockets.Add(new Socket()
            {
                U01 = -1,
                U02 = Iso4Utils.IsoFromTransform(lightRef.Position, lightRef.Rotation),
                Name = light.Name
            });

        }

        solid.CustomMaterials = materials.Select(m => new CPlugSolid2Model.Material() { MaterialName = "", MaterialUserInst = m }).ToArray();
        if (solid.CustomMaterials.Length > 0)
            solid.Materials = null;
        solid.Visuals = visuals.ToArray();
        solid.ShadedGeoms = shadedGeom.ToArray();

        solid.LightInsts = lightInsts.ToArray();
        solid.LightUserModels = lightUserModels.ToArray();
        if (lightInsts.Count > 0)
        {
            var skel = GbxItemUtils.CreateSkel(sockets);
            solid.Skel = skel;
        }

        solid.LodMaxDistAtFov90 = cluster.LODDistances;
        solid.PreLightGenerator = preLightGen;

        return solid;
    }


    CPlugSolid2Model CreateEmptySolid2Model(NormalizedItem item)
    {
        var solid = GbxTemplateLibrary.CreateCPlugSolid2ModelTemplate().Value;

        solid.LodMaxDistAtFov90 = [];
        solid.PreLightGenerator = null;
        solid.Visuals = [];
        solid.CustomMaterials = [];
        solid.ShadedGeoms = [];
        solid.LightInsts = [];
        solid.LightUserModels = [];
        solid.Materials = [];

        var c = solid.GetChunk<CPlugSolid2Model.Chunk090BB000>();
        c!.U06 = $"CSharpMapping ItemCompiler Solid2Model: {item.Name}";
        c.U18 = 0;
        c.U19 = [];
        c.Version = 34;

        solid.FileWriteTime = DateTime.Now;


        return solid;
    }


    CPlugVisualIndexedTriangles GetOrCreateIndexedTriangles(
        NormalizedItem item,
        RefBase refBase,
        InstanceSettings instanceSetting,
        BuildSettings buildSettings,
        out CPlugMaterialUserInst material)
    {
        NormalizedShape? shape = refBase is ShapeRef sr ? item.ShapePool[sr.ShapeKey] : null;
        NormalizedMesh? mesh = refBase is MeshRef mr ? item.MeshPool[mr.MeshKey] : null;

        material = GbxItemUtils.CreateErrorMat();
        if (mesh != null)
            material = mesh.Material;

        var key = ComputeIndexedTrianglesKey(refBase);

        if (!compileContext.CompileOptions.Optimization.HasFlag(ItemCompilerOptimization.ProhibitInstanceSharing) &&
            compileContext.nodeRefTable.TryGetNode<CPlugVisualIndexedTriangles>(key, out var cached))
            return cached;

        CPlugVisualIndexedTriangles? indexedTriangles = null;
        if (mesh != null)
            indexedTriangles = CreateIndexedTrianglesFromMesh(mesh, buildSettings);
        if (shape != null)
            indexedTriangles = CreateIndexedTrianglesFromShape(shape, buildSettings);
        if(indexedTriangles != null)
        {
            compileContext.nodeRefTable.Register(key, indexedTriangles);
            return indexedTriangles;
        }

        throw new InvalidOperationException($"Unknown RefBase type: {refBase.GetType().Name}");
    }
    CPlugVisualIndexedTriangles CreateIndexedTrianglesFromMesh(
       NormalizedMesh mesh,
       BuildSettings buildSettings)
    {
        var uvs = new SortedDictionary<int, Vec2[]>();
        var colors = new SortedDictionary<int, int[]>();

        if (mesh.TexCoords is not null)
            uvs[0] = mesh.TexCoords;
        if (mesh.LightmapCoords is not null)
            uvs[uvs.Count] = mesh.LightmapCoords;
        if (mesh.Colors is not null)
            colors[0] = mesh.Colors;

        var vertexStream = GbxTemplateLibrary.CreateCPlugVertexStreamTemplate().Value;
        vertexStream.Positions = mesh.Positions.ToArray();
        vertexStream.Normals = mesh.Normals.ToArray();
        vertexStream.UVs = uvs;
        vertexStream.Colors = colors;

        // fix data decl
        var dataDeclField = typeof(CPlugVertexStream).GetField("dataDecls",
            BindingFlags.NonPublic | BindingFlags.Instance);

        var dataDecls = (dataDeclField!.GetValue(vertexStream) as CPlugVertexStream.DataDecl[])!.ToList();

        // clear tex coords if rex or light is not available
        if (uvs.Count <= 0)
            dataDecls.RemoveAll(decl => decl.WeightCount == CPlugVertexStream.EPlugVDcl.TexCoord0);
        if (uvs.Count <= 1)
            dataDecls.RemoveAll(decl => decl.WeightCount == CPlugVertexStream.EPlugVDcl.TexCoord1);

        if (colors.Count <= 0)
            dataDecls.RemoveAll(decl => decl.WeightCount == CPlugVertexStream.EPlugVDcl.Color0);
        else
            dataDecls.Insert(2, new CPlugVertexStream.DataDecl() { Flags1 = 546310152, Flags2 = 64, Offset = 16 });

        if (mesh.TexCoords is null)
        {
            dataDecls.RemoveAll(decl => decl.WeightCount == EPlugVDcl.TangentU);
            dataDecls.RemoveAll(decl => decl.WeightCount == EPlugVDcl.TangentV);
        }

        if (colors.Count > 0)
        {
            dataDecls.FirstOrDefault(d => d.WeightCount == CPlugVertexStream.EPlugVDcl.Position)?.Flags1 = 7341056; // 9438208;

            dataDecls.FirstOrDefault(d => d.WeightCount == CPlugVertexStream.EPlugVDcl.Normal)?.Flags1 = 275782661;// 277879813;

            dataDecls.FirstOrDefault(d => d.WeightCount == CPlugVertexStream.EPlugVDcl.Color0)?.Flags1 = 544213000; //546310152;

            var tex0Decl = dataDecls.FirstOrDefault(d => d.WeightCount == CPlugVertexStream.EPlugVDcl.TexCoord0);
            if (tex0Decl != null)
            {
                tex0Decl.Flags1 = 544211466; // 546308618;
                tex0Decl.Flags2 = 80;
                tex0Decl.Offset = 20;
            }

            var tangentUDecl = dataDecls.FirstOrDefault(d => d.WeightCount == CPlugVertexStream.EPlugVDcl.TangentU);
            if (tangentUDecl != null)
            {
                tangentUDecl.Flags1 = 277879826;
                tangentUDecl.Flags2 = 112;
                tangentUDecl.Offset = 28;
            }

            var tangentVDecl = dataDecls.FirstOrDefault(d => d.WeightCount == CPlugVertexStream.EPlugVDcl.TangentV);
            if (tangentVDecl != null)
            {
                tangentVDecl.Flags1 = 277879828;
                tangentVDecl.Flags2 = 128;
                tangentVDecl.Offset = 32;
            }
        }
        dataDeclField.SetValue(vertexStream, dataDecls.ToArray());


        var countField = typeof(CPlugVertexStream).GetField("count",
            BindingFlags.NonPublic | BindingFlags.Instance);
        countField?.SetValue(vertexStream, vertexStream.Positions.Length);

        if (mesh.TexCoords is null)
        {
            GbxItemUtils.SetTangentUs(vertexStream, null);
        }
        else
        {
            GbxItemUtils.SetTangentUs(vertexStream, mesh.TangentUs ?? new Vec3[mesh.Positions.Length]);
        }

        if (mesh.TexCoords is null)
        {
            GbxItemUtils.SetTangentVs(vertexStream, null);
        }
        else
        {
            GbxItemUtils.SetTangentVs(vertexStream, mesh.TangentVs ?? new Vec3[mesh.Positions.Length]);
        }

        var indexBuffer = GbxTemplateLibrary.CreateCPlugIndexBufferTemplate().Value;
        indexBuffer.Indices = mesh.Indices.ToArray();
        indexBuffer.Flags = 2;

        var indexedTriangles = GbxTemplateLibrary.CreateCPlugVisualIndexedTrianglesTemplate().Value;
        indexedTriangles.VertexStreams = [vertexStream];
        indexedTriangles.IndexBuffer = indexBuffer;
        indexedTriangles.BoundingBox = GbxItemUtils.BuildBoxAligned(mesh);

        var countProperty = typeof(CPlugVisualIndexedTriangles).GetProperty("Count",
            BindingFlags.NonPublic | BindingFlags.Instance);
        countProperty?.SetValue(indexedTriangles, vertexStream.Positions.Length);

        return indexedTriangles;
    }

    CPlugVisualIndexedTriangles CreateIndexedTrianglesFromShape(
        NormalizedShape shape,
        BuildSettings buildSettings)
    {
        var tempMesh = new NormalizedMesh()
        {
            Positions = shape.Positions,
            Normals = new Vec3[shape.Positions.Length], // todo compute normals
            Indices = shape.Indices
        };
        return CreateIndexedTrianglesFromMesh(tempMesh, buildSettings);
    }

    PreLightGen? MergePreLightGenerator(NormalizedMesh mesh, InstanceSettings instanceSetting, PreLightGen? preLightGen)
    {
        var meshLightGen = GbxItemUtils.ComputePreLightGenFromMeshData(mesh);
        if (instanceSetting.LightmapSizeOverride.HasValue)
            meshLightGen?.U02 = instanceSetting.LightmapSizeOverride.Value;

        if (meshLightGen == null)
            return preLightGen;

        if (preLightGen is null)
            return meshLightGen;


        preLightGen.U02 = Math.Max(preLightGen.U02, meshLightGen.U02); // LMSizeLengthMeters
        preLightGen.U04 = Math.Min(preLightGen.U04, meshLightGen.U04); // Min UV-X
        preLightGen.U05 = Math.Min(preLightGen.U05, meshLightGen.U05); // Min UV-Y
        preLightGen.U06 = Math.Max(preLightGen.U06, meshLightGen.U06); // Max UV-X
        preLightGen.U07 = Math.Max(preLightGen.U07, meshLightGen.U07); // Max UV-Y

        preLightGen.U08 = Math.Max(preLightGen.U08, meshLightGen.U08); // Max (float.Max i think almost always)
        preLightGen.U09 = Math.Max(preLightGen.U09, meshLightGen.U09); // Max (float.Max i think almost always)
        preLightGen.U10 = Math.Min(preLightGen.U10, meshLightGen.U10); // Min (float.Max i think almost always)
        preLightGen.U11 = Math.Min(preLightGen.U11, meshLightGen.U11); // Min (float.Max i think almost always)

        return preLightGen;
    }



    CPlugSurface? GetOrCreateSurface(NormalizedItem item, List<(Guid Id, InstanceSettings Instance)> collidableInstances, BuildSettings buildSettings)
        => GetOrCreateSurface(item, collidableInstances, LegacyGameplayId.None, new Vec3(0, 0, 1), false, buildSettings);
    CPlugSurface? GetOrCreateSurface(
        NormalizedItem item,
        List<(Guid Id, InstanceSettings Instance)> triggerInstances,
        LegacyGameplayId gameplayId,
        Vec3 gameplayMainDir,
        bool trigger,
        BuildSettings buildSettings)
    {

        var key = ComputeSurfaceKey(triggerInstances, gameplayMainDir, trigger);

        if (!compileContext.CompileOptions.Optimization.HasFlag(ItemCompilerOptimization.ProhibitInstanceSharing) &&
            compileContext.nodeRefTable.TryGetNode<CPlugSurface>(key, out var cached))
            return cached;

        var surface = CreateSurface(item, triggerInstances, gameplayId, gameplayMainDir, trigger, buildSettings);

        compileContext.nodeRefTable.Register(key, surface!);

        return surface;
    }
    CPlugSurface? CreateSurface(
        NormalizedItem item,
        List<(Guid Id, InstanceSettings Instance)> collidableInstances,
        LegacyGameplayId gameplayId,
        Vec3 gameplayMainDir,
        bool trigger,
        BuildSettings buildSettings)
    {
        var geometrySources = collidableInstances.Select(i => ResolveCollisionSource(item, i.Id, i.Instance.Kind));

        var surface = GbxTemplateLibrary.CreateSurfaceTemplate().Value;

        var surfMesh = GbxTemplateLibrary.CreateSurfaceMeshTemplate().Value;

        MergeIntoCollisionGeometry(geometrySources, trigger, out var vertices, out var triangles);

        surfMesh.Vertices = vertices.ToArray();
        surfMesh.Triangles = triangles.ToArray();

        surface.Surf = surfMesh;

        surface.Surf.GameplayMainDir = gameplayMainDir;

        var chunk = surface.GetChunk<Chunk0900C003>();
        if (chunk!.U02!.Length == 0)
            chunk.U02 = [0];


        return surface;
    }

    ToolResult<NPlugTrigger_SWaypoint> GetOrCreateTriggerWaypoint(NormalizedItem item, Guid clusterKey, List<(Guid Id, InstanceSettings Instance)> instances, ClusterSettings cluster, BuildSettings buildSettings)
    {
        var key = ComputeClusterContentKey(clusterKey, instances, cluster, buildSettings);

        if (!compileContext.CompileOptions.Optimization.HasFlag(ItemCompilerOptimization.ProhibitInstanceSharing) &&
            compileContext.nodeRefTable.TryGetNode<NPlugTrigger_SWaypoint>(key, out var cached))
            return ToolResult.Success(cached, nameof(ItemCompiler));

        var waypointResult = CreateTriggerWaypoint(item, clusterKey, instances, cluster, buildSettings);
        if (waypointResult.IsFailure)
            return ToolResult.Fail(waypointResult);
        var waypoint = waypointResult.Value;

        if (cluster.WaypointType == EWaypointType.Checkpoint ||
            cluster.WaypointType == EWaypointType.Start ||
            cluster.WaypointType == EWaypointType.StartFinish)
        {
            compileContext.PendingWaypoints.Add(waypoint, new PendingWaypoint()
            {
                SpawnPosition = cluster.WaypointSpawnPosition ?? new Vec3(0, 0, 0),
                SpawnRotation = cluster.WaypointSpawnRotation ?? Quaternion.Identity,
            });
        }


        compileContext.nodeRefTable.Register(key, waypoint);
        return ToolResult.Success(waypoint, nameof(ItemCompiler));
    }
    ToolResult<NPlugTrigger_SWaypoint> CreateTriggerWaypoint(NormalizedItem item, Guid clusterKey, List<(Guid Id, InstanceSettings Instance)> instances, ClusterSettings cluster, BuildSettings buildSettings)
    {
        NPlugTrigger_SWaypoint triggerWaypoint = new NPlugTrigger_SWaypoint()
        {
            Version = 1,
            NoRespawn = cluster.WaypointNoRespawn ?? false,
            Type = (NPlugTrigger_SWaypoint.EGameItemWaypointType)(cluster.WaypointType.HasValue ? cluster.WaypointType.Value : EWaypointType.Checkpoint)
        };
        var gameplayMainDirDef = instances.FirstOrDefault(i =>
        {
            if (cluster.GameplayMainDir.HasValue)
                return true;
            if (i.Instance.Kind == RefKind.Shape)
                return true;
            return false;
        });

        Vec3 gameplayMainDir;
        if (cluster.GameplayMainDir.HasValue)
            gameplayMainDir = cluster.GameplayMainDir.Value;
        else
            gameplayMainDir = new Vec3(0, 0, 1);



        var surface = GetOrCreateSurface(item, instances, LegacyGameplayId.None, gameplayMainDir, true, buildSettings);
        triggerWaypoint.TriggerShape = surface;
        return ToolResult.Success(triggerWaypoint, nameof(ItemCompiler));
    }
    ToolResult<NPlugTrigger_SSpecial> GetOrCreateTriggerSpecial(NormalizedItem item, Guid clusterKey, List<(Guid Id, InstanceSettings Instance)> instances, ClusterSettings cluster, BuildSettings buildSettings)
    {
        var key = ComputeClusterContentKey(clusterKey, instances, cluster, buildSettings);

        if (!compileContext.CompileOptions.Optimization.HasFlag(ItemCompilerOptimization.ProhibitInstanceSharing) &&
            compileContext.nodeRefTable.TryGetNode<NPlugTrigger_SSpecial>(key, out var cached))
            return ToolResult.Success(cached, nameof(ItemCompiler));

        var specialResult = CreateTriggerSpecial(item, clusterKey, instances, cluster, buildSettings);
        if (specialResult.IsFailure)
            return ToolResult.Fail(specialResult);
        var special = specialResult.Value;

        compileContext.nodeRefTable.Register(key, special);
        return ToolResult.Success(special, nameof(ItemCompiler));
    }
    ToolResult<NPlugTrigger_SSpecial> CreateTriggerSpecial(NormalizedItem item, Guid clusterKey, List<(Guid Id, InstanceSettings Instance)> instances, ClusterSettings cluster, BuildSettings buildSettings)
    {
        NPlugTrigger_SSpecial triggerSpecial = new NPlugTrigger_SSpecial()
        {
            Version = 3
        };

        Vec3 gameplayMainDir;
        if (cluster.GameplayMainDir.HasValue)
            gameplayMainDir = cluster.GameplayMainDir.Value;
        else
            gameplayMainDir = new Vec3(0, 0, 1);

        var surface = GetOrCreateSurface(item, instances, LegacyGameplayId.None, gameplayMainDir, true, buildSettings);
        triggerSpecial.TriggerShape = surface;

        ItemTriggerEffectConverter.ConvertEffect(cluster.TriggerGameplayId!.Value, triggerSpecial);
        return ToolResult.Success(triggerSpecial, nameof(ItemCompiler));
    }

    object ResolveCollisionSource(NormalizedItem item, Guid id, RefKind kind) => kind switch
    {
        RefKind.Mesh => item.MeshPool[(compileContext.GuidToRef[id] as MeshRef)!.MeshKey],
        RefKind.Shape => item.ShapePool[(compileContext.GuidToRef[id] as ShapeRef)!.ShapeKey],
        _ => throw new InvalidOperationException($"Unexpected RefKind {kind} in collision source"),
    };
    void MergeIntoCollisionGeometry(IEnumerable<object> geometrySources, bool trigger, out List<Vec3> positions, out List<CPlugSurface.Mesh.Triangle> triangles)
    {
        //positions = new List<Vec3>();
        triangles = new List<CPlugSurface.Mesh.Triangle>();

        Dictionary<Vec3, int> positionMap = new Dictionary<Vec3, int>();

        foreach (var geometrySource in geometrySources)
        {
            ReadOnlySpan<Vec3> sourcePositions;
            ReadOnlySpan<int> sourceIndices;
            ReadOnlySpan<MaterialId> surfaceMaterialIds;
            NormalizedMesh normMesh;
            if ((normMesh = geometrySource as NormalizedMesh) != null)
            {
                sourcePositions = normMesh.Positions.AsSpan();
                sourceIndices = normMesh.Indices.AsSpan();
                surfaceMaterialIds = default;
            }
            else if (geometrySource is NormalizedShape normShape)
            {
                sourcePositions = normShape.Positions.AsSpan();
                sourceIndices = normShape.Indices.AsSpan();
                surfaceMaterialIds = normShape.SurfaceMaterialIds.AsSpan();
            }
            else
            {
                throw new InvalidOperationException($"Unexpected geometry source type: {geometrySource.GetType().Name}");
            }

            Dictionary<int, int> localPositionToGlobalPosition = new Dictionary<int, int>();
            for(int i = 0; i < sourcePositions.Length; i++)
            {
                var p = sourcePositions[i];
                if(!positionMap.TryGetValue(p, out var globalIndex))
                {
                    globalIndex = positionMap.Count;
                    positionMap[p] = globalIndex;
                }
                localPositionToGlobalPosition[i] = globalIndex;
            }

            for (int i = 0; i < sourceIndices.Length; i += 3)
            {
                MaterialId materialId = MaterialId.Concrete;
                if (!surfaceMaterialIds.IsEmpty)
                    materialId = surfaceMaterialIds[i / 3];
                else if (normMesh is not null)
                    materialId = normMesh.Material.SurfacePhysicId;
                else
                    materialId = MaterialId.Concrete;
                triangles.Add(new CPlugSurface.Mesh.Triangle
                {
                    Indices = new Int3(
                        localPositionToGlobalPosition[sourceIndices[i]],
                        localPositionToGlobalPosition[sourceIndices[i + 1]],
                        localPositionToGlobalPosition[sourceIndices[i + 2]]),
                    SurfaceIndex = 0,
                    U02 = trigger ? (byte)0 : (byte)materialId,
                    U03 = 0
                });
            }
        }
        positions = positionMap.Keys.ToList();
    }
    private ShapeRole ResolveDestination(NormalizedItem item, (Guid Id, InstanceSettings Instance) i, ShapeRole meshRole)
    {
        if (i.Instance.ShapeRoleOverride is { } explicitDest)
            return explicitDest;

        return i.Instance.Kind switch
        {
            RefKind.Shape => (compileContext.GuidToRef[i.Id] as ShapeRef)!.Role switch
            {
                ShapeRole.Static => ShapeRole.Static,
                _ => ShapeRole.Dynamic,
            },
            _ => meshRole, // meshes default 
        };
    }


    void FinalizeKinematicConstraints(CGameItemModel itemModel)
    {
        if (compileContext.PendingConstraints.Count == 0)
            return;

        if (itemModel.EntityModel is NPlugItem_SVariantList variantList)
        {
            foreach (var v in variantList.Variants ?? [])
            {
                if (v.EntityModel is CPlugPrefab prefab)
                {
                    FinalizeKinematicConstraints(prefab);
                }
            }
        }
        else if (itemModel.EntityModel is CPlugPrefab prefab)
        {
            FinalizeKinematicConstraints(prefab);
        }

    }
    void FinalizeKinematicConstraints(CPlugPrefab root)
    {
        if (compileContext.PendingConstraints.Count == 0)
            return;

        var flatEnts = FlattenEnts(root).ToList();
        var dynaModelsInItem = new List<(CPlugPrefab prefab, EntRef ent, CPlugDynaObjectModel dynaModel)>();
        for (int i = 0; i < flatEnts.Count; i++)
        {
            if (flatEnts[i].entRef.Model is CPlugDynaObjectModel dynaModel)
                dynaModelsInItem.Add((flatEnts[i].parent, flatEnts[i].entRef, dynaModel));
        }

        for (int i = 0; i < dynaModelsInItem.Count; ++i)
        {
            var (prefabParent, _, dyna) = dynaModelsInItem[i];

            if (!compileContext.PendingConstraints.TryGetValue(dyna, out var pendingConstraint))
                continue;

            int parentIndex = -1;
            if (pendingConstraint.ParentCluster.HasValue)
            {
                parentIndex = compileContext.BuildDynaObjectClusters.IndexOf(pendingConstraint.ParentCluster.Value);
            }

            var kinematicConstraint = BuildKinematicConstraint(pendingConstraint);
            var kcParams = new NPlugDyna_SPrefabConstraintParams()
            {
                Ent1 = parentIndex,
                Ent2 = i,
                Pos1 = Vec3.Zero,
                Pos2 = Vec3.Zero,
            };
            var entRef = GbxItemUtils.CreateEntRef();
            entRef.Model = kinematicConstraint;
            entRef.Params = kcParams;
            prefabParent.Ents = [.. prefabParent.Ents, entRef];
        }
    }
    private IEnumerable<(CPlugPrefab parent, EntRef entRef)> FlattenEnts(CMwNod node)
    {
        if (node is CPlugPrefab prefab)
            foreach (var ent in prefab.Ents)
            {
                yield return (prefab, ent);
                foreach (var nested in FlattenEnts(ent.Model!))
                    yield return nested;
            }
    }
    NPlugDyna_SKinematicConstraint BuildKinematicConstraint(PendingConstraint pendingConstraint)
    {
        if (pendingConstraint.Constraint != null)
        {
            return pendingConstraint.Constraint;
        }
        else
        {
            return GbxTemplateLibrary.CreateKinematicConstraintTemplate().Value;
        }
    }

    public void FinalizeWaypoints(CGameItemModel itemModel)
    {
        if (compileContext.PendingWaypoints.Count == 0)
            return;

        if (itemModel.EntityModel is NPlugItem_SVariantList variantList)
        {
            foreach (var v in variantList.Variants ?? [])
            {
                if (v.EntityModel is CPlugPrefab prefab)
                {
                    FinalizeKinematicWaypoints(prefab);
                }
            }
        }
        else if (itemModel.EntityModel is CPlugPrefab prefab)
        {
            FinalizeKinematicWaypoints(prefab);
        }
    }

    void FinalizeKinematicWaypoints(CPlugPrefab root)
    {
        if (compileContext.PendingWaypoints.Count == 0)
            return;

        var flatEnts = FlattenEnts(root).ToList();
        var waypointsInItem = new List<(CPlugPrefab prefab, EntRef ent, NPlugTrigger_SWaypoint waypoint)>();
        for (int i = 0; i < flatEnts.Count; i++)
        {
            if (flatEnts[i].entRef.Model is NPlugTrigger_SWaypoint waypoint)
                waypointsInItem.Add((flatEnts[i].parent, flatEnts[i].entRef, waypoint));
        }

        for (int i = 0; i < waypointsInItem.Count; ++i)
        {
            var (prefabParent, _, waypoint) = waypointsInItem[i];

            if (!compileContext.PendingWaypoints.TryGetValue(waypoint, out var pendingWaypoint))
                continue;
            if (prefabParent.Ents.Any(e => e.Model is CPlugSpawnModel))
                continue;
            var spawnModel = GbxItemUtils.CreateSpawnModel();

            var entRef = GbxItemUtils.CreateEntRef();
            entRef.Model = spawnModel;
            entRef.Position = pendingWaypoint.SpawnPosition ?? Vec3.Zero;
            entRef.Rotation = pendingWaypoint.SpawnRotation ?? Quaternion.Identity;

            prefabParent.Ents = [.. prefabParent.Ents, entRef];
        }
    }


    private string ComputeContentKey(NormalizedItem item, int modelKey, Guid entityId, BuildSettings settings)
    {
        var model = item.ModelPool[modelKey];

        return model.Type switch
        {
            ModelType.Container => ComputeContainerKey(item, model, modelKey, settings),
            ModelType.Variant_List => ComputeVariantListKey(item, model, modelKey, settings),
            _ => ComputeLeafKey(item, model, modelKey, settings), // NEW — leaves DO need a key for container-parent purposes
        };
    }
    private string ComputeContainerKey(NormalizedItem item, NormalizedModel model, int modelKey, BuildSettings settings)
    {
        var childKeys = model.Children
            .Select(c => $"{c.Position}|{c.Rotation}|{ComputeContentKey(item, c.ModelKey, c.Id, settings)}");
        return $"container:{modelKey}:[{string.Join(";", childKeys)}]";
    }

    private string ComputeVariantListKey(NormalizedItem item, NormalizedModel model, int modelKey, BuildSettings settings)
    {
        var variantKeys = model.Variants.Select(v => ComputeContentKey(item, v.ModelKey, v.Id, settings));
        return $"variantlist:{modelKey}:[{string.Join(";", variantKeys)}]";
    }
    private string ComputeLeafKey(NormalizedItem item, NormalizedModel model, int modelKey, BuildSettings settings)
    {
        var instanceIds = model.Meshes.Select(m => m.Id).Concat(model.Shapes.Select(s => s.Id)).Concat(model.Lights.Select(l => l.Id)).ToList();
        var byCluster = instanceIds
            .Select(id => (Id: id, Settings: settings.Instances[id]))
            .Where(i => i.Settings.Enabled)
            .GroupBy(i => i.Settings.ClusterKey)
            .OrderBy(g => g.Key); // stable order across calls

        var clusterKeys = byCluster.Select(g => ComputeClusterContentKey(g.Key, g.ToList(), settings.Clusters[g.Key], settings));
        return $"leaf:{modelKey}:[{string.Join(";", clusterKeys)}]";
    }

    private string ComputeClusterContentKey(Guid clusterKey, List<(Guid Id, InstanceSettings Settings)> instances,
        ClusterSettings cluster, BuildSettings settings)
    {
        var identityPart = cluster.Detached ? $":cluster={clusterKey}" : "";

        var parts = instances.OrderBy(i => i.Id).Select(i =>
            $"{i.Id}:{i.Settings.Enabled}:{i.Settings.Visible}:{i.Settings.Collidable}:{i.Settings.LODMaskOverride}");

        return $"cluster{identityPart}:{cluster.Type}:" +
               $"{cluster.WaypointType}:{cluster.WaypointNoRespawn}:{cluster.TriggerGameplayId}:{string.Join(",", cluster.LODDistances)}:[{string.Join(";", parts)}]";
    }

    private string ComputeSolidMeshKey(List<(Guid Id, InstanceSettings Instance)> visibleInstances)
    {
        var parts = visibleInstances
            .Select(ComputeSolidMeshInstanceKey)
            .OrderBy(p => p, StringComparer.Ordinal);

        return $"solidmesh:[{string.Join(";", parts)}]";
    }

    private string ComputeSolidMeshInstanceKey((Guid Id, InstanceSettings Instance) instance)
    {
        var refBase = compileContext.GuidToRef[instance.Id];
        var lodMask = refBase is MeshRef meshRef
            ? instance.Instance.LODMaskOverride ?? meshRef.LODMask
            : instance.Instance.LODMaskOverride ?? 0;

        return refBase switch
        {
            MeshRef mr => $"mesh:{mr.MeshKey}:lod={lodMask}:lm={FormatNullableFloat(instance.Instance.LightmapSizeOverride)}",
            ShapeRef sr => $"shape:{sr.ShapeKey}:lod={lodMask}",
            LightRef lr => $"light:{lr.LightKey}:type={instance.Instance.LightTypeOverride}:pos={FormatVector3(lr.Position)}:rot={FormatQuaternion(lr.Rotation)}",
            _ => throw new InvalidOperationException($"Unknown RefBase type: {refBase.GetType().Name}")
        };
    }

    private string ComputeIndexedTrianglesKey(RefBase refBase)
    {
        return refBase switch
        {
            MeshRef mr => $"mesh:{mr.MeshKey}",
            ShapeRef sr => $"shape:{sr.ShapeKey}",
            LightRef lr => $"light:{lr.LightKey}",
            _ => throw new InvalidOperationException($"Unknown RefBase type: {refBase.GetType().Name}")
        };
    }

    private string ComputeSurfaceKey(List<(Guid Id, InstanceSettings Instance)> collidableInstances)
        => ComputeSurfaceKey(collidableInstances, Vec3.Zero, false);
    private string ComputeSurfaceKey(List<(Guid Id, InstanceSettings Instance)> collidableInstances, Vec3 gameplayMainDir, bool trigger)
    {
        var parts = collidableInstances
            .Select(i => ComputeCollisionSourceKey(i.Id, i.Instance.Kind))
            .OrderBy(s => s, StringComparer.Ordinal);

        return $"surface:{trigger}:dir={FormatVec3(gameplayMainDir)}:[{string.Join(",", parts)}]";
    }

    private string ComputeCollisionSourceKey(Guid id, RefKind kind)
    {
        var refBase = compileContext.GuidToRef[id];

        return kind switch
        {
            RefKind.Mesh when refBase is MeshRef mr => $"mesh:{mr.MeshKey}",
            RefKind.Shape when refBase is ShapeRef sr => $"shape:{sr.ShapeKey}",
            _ => throw new InvalidOperationException($"Unexpected RefKind {kind} in collision source key"),
        };
    }

    private static string FormatNullableFloat(float? value)
        => value.HasValue ? value.Value.ToString("R", CultureInfo.InvariantCulture) : "-";

    private static string FormatVec3(Vec3 value)
        => $"{value.X.ToString("R", CultureInfo.InvariantCulture)},{value.Y.ToString("R", CultureInfo.InvariantCulture)},{value.Z.ToString("R", CultureInfo.InvariantCulture)}";

    private static string FormatVector3(Vector3 value)
        => $"{value.X.ToString("R", CultureInfo.InvariantCulture)},{value.Y.ToString("R", CultureInfo.InvariantCulture)},{value.Z.ToString("R", CultureInfo.InvariantCulture)}";

    private static string FormatQuaternion(Quaternion value)
        => $"{value.X.ToString("R", CultureInfo.InvariantCulture)},{value.Y.ToString("R", CultureInfo.InvariantCulture)},{value.Z.ToString("R", CultureInfo.InvariantCulture)},{value.W.ToString("R", CultureInfo.InvariantCulture)}";


    void FillItemData(CGameItemModel item, NormalizedItem normalizedItem, BuildSettings buildSettings)
    {
        item.Name = string.IsNullOrWhiteSpace(normalizedItem.Name) ? "New Item" : normalizedItem.Name;
        ChunkSafeItemOperations.SetIcon(item, normalizedItem.Icon, normalizedItem.IconWebP);
        item.Description = string.IsNullOrWhiteSpace(normalizedItem.Description) ? "No Description" : normalizedItem.Description;
        item.Ident = _ident;
        item.DefaultPlacement = normalizedItem.PlacementParam ?? GbxTemplateLibrary.CreatePlacementParamTemplate();
        if (item.EntityModel is CPlugPrefab prefab)
            prefab.FileWriteTime = DateTime.Now;

        if (buildSettings.Clusters.Any(c => c.Value.WaypointType.HasValue))
        {
            var waypointType = buildSettings.Clusters.FirstOrDefault(c => c.Value.WaypointType.HasValue).Value.WaypointType;
            if (waypointType.HasValue)
                item.WaypointType = (EWaypointType)waypointType.Value;
        }
        else
        {
            item.WaypointType = EWaypointType.None;
        }
    }

    //-----------------------
    // MeshModeler
    internal record CrystalCompileContext(
        List<CPlugCrystal.Layer> Layers,
        List<CPlugCrystal.Material> Materials,
        Dictionary<CPlugMaterialUserInst, CPlugMaterialUserInst> MaterialMap,
        List<int> SmoothingGroups,
        CPlugMaterialUserInst ErrorMat);

    CPlugCrystal BuildCrystal(NormalizedItem item, EntityRefBase entity, BuildSettings buildSettings)
    {
        var model = item.ModelPool[entity.ModelKey];

        var context = new CrystalCompileContext(new(), new(), new(), new(), GbxItemUtils.CreateErrorMat());
        BuildEntityCrystal(item, entity, Vector3.Zero, Quaternion.Identity, buildSettings, context);

        CPlugCrystal crystal = GbxTemplateLibrary.CreateCPlugCrystalTemplate().Value;
        crystal.Layers = context.Layers;
        crystal.Materials = context.Materials;
        var chunk = crystal.Chunks.Get<CPlugCrystal.Chunk09003007>()!;
        chunk.U01 = context.SmoothingGroups.ToArray();
        return crystal;
    }

    void BuildEntityCrystal(NormalizedItem item,
        EntityRefBase entity,
        Vector3 parentPosition,
        Quaternion parentRotation,
        BuildSettings buildSettings,
        CrystalCompileContext context)
    {
        var model = item.ModelPool[entity.ModelKey];
        switch (model.Type)
        {
            case ModelType.Container:
                BuildPrefabCrystal(item, model.Children, parentPosition, parentRotation, buildSettings, context);
                break;
            case ModelType.Variant_List:
                BuildVariantListCrystal(item, model.Variants, parentPosition, parentRotation, buildSettings, context);
                break;
            default:
                BuildLeafCrystal(item, model, parentPosition, parentRotation, buildSettings, context);
                break;
        }

    }
    void BuildPrefabCrystal(NormalizedItem item,
        List<EntityRef> children,
        Vector3 parentPosition,
        Quaternion parentRotation,
        BuildSettings buildSettings,
        CrystalCompileContext context)
    {
        foreach (var ent in children)
        {
            var (globalPosition, globalRotation) = CalculateGlobalTransforms(ent, parentPosition, parentRotation);
            BuildEntityCrystal(item, ent, globalPosition, globalRotation, buildSettings, context);
        }
    }
    void BuildVariantListCrystal(NormalizedItem item,
        List<NormalizedVariant> variants,
        Vector3 parentPosition,
        Quaternion parentRotation,
        BuildSettings buildSettings,
        CrystalCompileContext context)
    {
        foreach (var variant in variants)
        {
            BuildEntityCrystal(item, variant, parentPosition, parentRotation, buildSettings, context);
        }
    }

    void BuildLeafCrystal(
        NormalizedItem item,
        NormalizedModel model,
        Vector3 parentPosition,
        Quaternion parentRotation,
        BuildSettings settings,
        CrystalCompileContext context)
    {
        var instanceIds = model.Meshes.Select(m => m.Id)
            .Concat(model.Shapes.Select(s => s.Id))
            .Concat(model.Lights.Select(l => l.Id));

        var instances = instanceIds
            .Select(id => (Id: id, Settings: settings.Instances[id]))
            .Where(i => i.Settings.Enabled)
            .ToList();

        var byCluster = instances.GroupBy(i => i.Settings.ClusterKey).ToList();

        foreach (var cluster in byCluster)
        {
            BuildLayers(item, cluster.ToList(), settings.Clusters[cluster.Key], parentPosition, parentRotation, settings, context);
        }

    }

    void BuildLayers(
        NormalizedItem item,
        List<(Guid Id, InstanceSettings Instance)> instances,
        ClusterSettings cluster,
        Vector3 parentPosition,
        Quaternion parentRotation,
        BuildSettings settings,
        CrystalCompileContext context)
    {
        foreach (var instance in instances)
        {
            if (instance.Instance.Kind == RefKind.Light)
                continue;
            var refBase = compileContext.GuidToRef[instance.Id];
            MeshRef? meshRef = refBase as MeshRef;
            NormalizedMesh? mesh = meshRef != null ? item.MeshPool[meshRef.MeshKey] : null;
            ShapeRef? shapeRef = refBase as ShapeRef;
            NormalizedShape? shape = shapeRef != null ? item.ShapePool[shapeRef.ShapeKey] : null;

            CPlugMaterialUserInst baseMaterialInstance;
            if (mesh != null)
                baseMaterialInstance = mesh.Material;
            else
                baseMaterialInstance = context.ErrorMat;

            if (!context.MaterialMap.TryGetValue(baseMaterialInstance, out var materialInstance))
            {
                context.MaterialMap[baseMaterialInstance] = materialInstance = ObjectCloner.DeepCloneObject(baseMaterialInstance)!;
            }
            var material = new CPlugCrystal.Material
            {
                MaterialUserInst = materialInstance,
                MaterialName = string.Empty,
            };

            CPlugCrystal.Layer layer = null!;
            int layerIndex = context.Layers.Count;
            if (cluster.Type == ModelType.Trigger_Special || cluster.Type == ModelType.Trigger_Waypoint)
            {
                CPlugCrystal.TriggerLayer triggerLayer = BuildTriggerLayer(item, instance, material, parentPosition, parentRotation);
                triggerLayer.Crystal!.U02 = layerIndex;
                layer = triggerLayer;
            }
            else
            {
                CPlugCrystal.GeometryLayer geometryLayer;
                if (mesh != null)
                {
                    geometryLayer = BuildGeometryLayer(
                        item,
                        instance,
                        cluster.LODDistances,
                        material,
                        parentPosition,
                        parentRotation);
                }
                else if (shape != null)
                {
                    geometryLayer = BuildGeometryLayer(
                        item,
                        instance,
                        cluster.LODDistances,
                        material,
                        parentPosition,
                        parentRotation);
                }
                else
                    continue;
                context.SmoothingGroups.AddRange(
                    geometryLayer.Crystal!.Faces.Select(_ =>
                    {
                        if (meshRef is null)
                            return 0;
                        if (instance.Instance.SmoothingGroupOverride.HasValue)
                            return instance.Instance.SmoothingGroupOverride.Value;
                        if (meshRef.SmoothingGroup.HasValue)
                            return meshRef.SmoothingGroup.Value;
                        return 0;
                    }));

                geometryLayer.Crystal!.U02 = layerIndex;
                geometryLayer.IsVisible = instance.Instance.Visible;
                geometryLayer.Collidable = instance.Instance.Collidable;

                layer = geometryLayer;
            }

            context.Materials.Add(material);
            layer.LayerId = $"Layer{layerIndex}";
            context.Layers.Add(layer);
        }

        if (cluster.Type == ModelType.Trigger_Waypoint &&
              (cluster.WaypointType == EWaypointType.Checkpoint || cluster.WaypointType == EWaypointType.Start || cluster.WaypointType == EWaypointType.StartFinish))
        {
            var layerCount = context.Layers.Count;
            var spawnLayer = BuildSpawnLayer(cluster, parentPosition, parentRotation);
            List<CPlugCrystal.PartInLayer> parts = [];
            for (int i = 0; i < layerCount; ++i)
            {
                parts.Add(new CPlugCrystal.PartInLayer() { GroupIndex = 0, LayerId = context.Layers[i].LayerId });
            }
            spawnLayer.Mask = parts.ToArray();
            spawnLayer.LayerId = $"Layer{layerCount}";
            spawnLayer.LayerName = $"Spawn Position";

            context.Layers.Add(spawnLayer);
        }
    }
    CPlugCrystal.GeometryLayer BuildGeometryLayer(
        NormalizedItem item,
        (Guid Id, InstanceSettings Instance) instance,
        float[] lodDistances,
        CPlugCrystal.Material material,
        Vector3 parentPosition,
        Quaternion parentRotation)
    {
        var refBase = compileContext.GuidToRef[instance.Id];
        int? lodMask = null;
        string name = "";
        Vec3[] positions = null!;
        int[] indices = null!;
        Vec2[]? texCoords = null;
        Vec2[]? lightmapCoords = null;
        if (refBase is MeshRef meshRef)
        {
            var mesh = item.MeshPool[meshRef.MeshKey];
            lodMask = instance.Instance.LODMaskOverride.HasValue ? instance.Instance.LODMaskOverride.Value : meshRef!.LODMask;
            name = mesh.Name;
            positions = mesh.Positions;
            indices = mesh.Indices;
            texCoords = mesh.TexCoords;
            lightmapCoords = mesh.LightmapCoords;
        }
        else if (refBase is ShapeRef shapeRef)
        {
            var shape = item.ShapePool[shapeRef.ShapeKey];
            positions = shape.Positions;
            indices = shape.Indices;
        }

        var layer = GbxTemplateLibrary.CreateGeometryLayerTemplate().Value;
        bool isLod = lodMask.HasValue ? !LODUtils.IsVisibleInAllLods(lodMask.Value, lodDistances.Length + 1) : false;
        layer.LayerName = $"Geometry {name}{(isLod ? " LOD-" + LodMaskToString(lodMask!.Value, lodDistances.Length + 1) : "")}";

        var crystal = GbxTemplateLibrary.CreateGeometryCrystalTemplate().Value;

        var localPositions = WeldPositions(positions, out var remap);

        // transform to global space
        crystal.Positions = localPositions.Select(p => (parentPosition + Vector3.Transform(p, parentRotation)).ToVec3()).ToArray();

        var group = crystal.Groups[0];
        group.Name = "part";
        crystal.Groups = [group];

        var faces = new List<CPlugCrystal.Face>();

        for (int i = 0; i < indices.Length; i += 3)
        {
            var vertices = new CPlugCrystal.Vertex[3];

            for (int v = 0; v < 3; v++)
            {
                var idx = indices[i + v];
                var texCoord = texCoords != null ? texCoords[idx] : Vec2.Zero;
                var lightmap = lightmapCoords != null ? lightmapCoords[idx] : Vec2.Zero;

                // always quantizing lightmap coord because gbx reader/writer also always does this
                lightmap = QuantizeLightmapCoord(lightmap);

                vertices[v] = new CPlugCrystal.Vertex(remap[idx], texCoord, lightmap);
            }

            faces.Add(new CPlugCrystal.Face(vertices, group, material, null));
        }

        crystal.Faces = faces.ToArray();
        layer.Crystal = crystal;
        return layer;
    }

    string LodMaskToString(int lodMask, int lodCount)
    {
        return Convert.ToString(lodMask, 2).PadLeft(lodCount, '0');
    }

    CPlugCrystal.TriggerLayer BuildTriggerLayer(
        NormalizedItem item,
        (Guid Id, InstanceSettings Instance) instance,
        CPlugCrystal.Material material,
        Vector3 parentPosition,
        Quaternion parentRotation)
    {
        var refBase = compileContext.GuidToRef[instance.Id];

        string name = "";
        Vec3[] positions = null!;
        int[] indices = null!;

        if (refBase is MeshRef meshRef)
        {
            var mesh = item.MeshPool[meshRef.MeshKey];
            name = mesh.Name;
            positions = mesh.Positions;
            indices = mesh.Indices;

        }
        else if (refBase is ShapeRef shapeRef)
        {
            var shape = item.ShapePool[shapeRef.ShapeKey];
            positions = shape.Positions;
            indices = shape.Indices;
        }

        var layer = GbxTemplateLibrary.CreateTriggerLayerTemplate().Value;
        layer.LayerName = $"Trigger {name}";
        var crystal = GbxTemplateLibrary.CreateTriggerCrystalTemplate().Value;

        var localPositions = WeldPositions(positions, out var remap);
        // transform to global space
        crystal.Positions = localPositions.Select(p => (parentPosition + Vector3.Transform(p, parentRotation)).ToVec3()).ToArray();

        var group = crystal.Groups[0];
        group.Name = "part";
        crystal.Groups = [group];

        var faces = new List<CPlugCrystal.Face>();

        for (int i = 0; i < indices.Length; i += 3)
        {
            var vertices = new CPlugCrystal.Vertex[3];

            for (int v = 0; v < 3; v++)
            {
                var idx = indices[i + v];
                var texCoord = Vec2.Zero;
                var lightmap = Vec2.Zero;

                // always quantizing lightmap coord because gbx reader/writer also always does this
                lightmap = QuantizeLightmapCoord(lightmap);

                vertices[v] = new CPlugCrystal.Vertex(remap[idx], texCoord, lightmap);
            }

            faces.Add(new CPlugCrystal.Face(vertices, group, material, null));
        }

        crystal.Faces = faces.ToArray();
        layer.Crystal = crystal;
        return layer;
    }

    CPlugCrystal.SpawnPositionLayer BuildSpawnLayer(ClusterSettings cluster, Vector3 parentPosition, Quaternion parentRotation)
    {
        var layer = new CPlugCrystal.SpawnPositionLayer();
        layer.Ver = 2;
        layer.CrystalEnabled = false;
        layer.ModifierVersion = 0;
        layer.SpawnPositionVersion = 1;

        var localSpawnRotation = cluster.WaypointSpawnRotation.HasValue ? cluster.WaypointSpawnRotation.Value : Quaternion.Identity;
        var localSpawnPosition = cluster.WaypointSpawnPosition.HasValue ? cluster.WaypointSpawnPosition.Value : Vector3.Zero;

        var globalSpawnRotation = parentRotation * localSpawnRotation;
        var globalSpawnPosition = parentPosition + Vector3.Transform(localSpawnPosition, parentRotation);

        var pitchYawRoll = globalSpawnRotation.ToPitchYawRoll();
        layer.HorizontalAngle = pitchYawRoll.Y * MathUtils.Rad2Deg;
        layer.VerticalAngle = pitchYawRoll.X * MathUtils.Rad2Deg;
        layer.RollAngle = pitchYawRoll.Z * MathUtils.Rad2Deg;
        layer.SpawnPosition = globalSpawnPosition;

        return layer;
    }



    Vec3[] WeldPositions(Vec3[] positions, out int[] remap)
    {
        var unique = new List<Vec3>(positions.Length);
        var indexMap = new Dictionary<Vec3, int>(positions.Length);
        remap = new int[positions.Length];

        for (int i = 0; i < positions.Length; i++)
        {
            var p = positions[i];
            if (!indexMap.TryGetValue(p, out int newIdx))
            {
                newIdx = unique.Count;
                unique.Add(p);
                indexMap[p] = newIdx;
            }
            remap[i] = newIdx;
        }

        return [.. unique];
    }
    Vec2 QuantizeLightmapCoord(Vec2 coord)
    {
        return new Vec2((ushort)MathF.Round(coord.X * ushort.MaxValue) / (float)ushort.MaxValue,
            (ushort)MathF.Round(coord.Y * ushort.MaxValue) / (float)ushort.MaxValue);
    }

    (Vector3 position, Quaternion rotation) CalculateGlobalTransforms(
        EntityRef entityRef,
        Vector3 parentPosition,
        Quaternion parentRotation)
    {
        Vector3 globalPosition =
            parentPosition + Vector3.Transform(entityRef.Position, parentRotation);

        Quaternion globalRotation =
            parentRotation * entityRef.Rotation;

        return (globalPosition, globalRotation);
    }


    // -----------------------
    // CommonEntityModel
    bool CanConvertToCommonEntityModel(NormalizedItem item, BuildSettings buildSettings, CompileOptions compileOptions)
    {
        if (buildSettings.Variants.Count > 0)
            return false;
        int staticClusterCount = 0;
        int triggerClusterCount = 0;
        HashSet<float> lodDistances = [];

        HashSet<Guid> checkedClusters = [];
        
        foreach(var instance in buildSettings.Instances)
        {
            if (!instance.Value.Enabled)
                continue;

            if (checkedClusters.Contains(instance.Value.ClusterKey))
                continue;            
            checkedClusters.Add(instance.Value.ClusterKey);

            var cluster = buildSettings.Clusters[instance.Value.ClusterKey];

            if (cluster.Type == ModelType.Dynamic)
                return false;
            else if (cluster.Type == ModelType.Trigger_Special)
                return false;
            else if (cluster.Type == ModelType.Trigger_Waypoint)
                triggerClusterCount++;
            else
                staticClusterCount++;

            if (lodDistances.Count == 0)
                lodDistances = cluster.LODDistances.ToHashSet();
            else if (!lodDistances.SequenceEqual(cluster.LODDistances))
                return false;

            if (cluster.WaypointNoRespawn.HasValue && cluster.WaypointNoRespawn.Value)
                return false; // no respawn not possible with simple item
        }

        if (staticClusterCount > 1 && !compileOptions.Optimization.HasFlag(ItemCompilerOptimization.AllowMerging))
            return false;
        if (triggerClusterCount > 1 && !compileOptions.Optimization.HasFlag(ItemCompilerOptimization.AllowMerging))
            return false;

        return true;
    }

    ToolResult<CGameItemModel> ConvertToCommonEntityModel(NormalizedItem item, CPlugPrefab prefab, BuildSettings buildSettings)
    {
        var entities = ExtractEntities(prefab, Vector3.Zero, Quaternion.Identity).ToList();
        var commonEntityModel = GbxTemplateLibrary.CreateCommonItemEntityModelTemplate().Value;

        commonEntityModel.StaticObject.Mesh = CreateEmptySolid2Model(item);

        var surface = GbxTemplateLibrary.CreateSurfaceTemplate().Value;
        var surfMesh = GbxTemplateLibrary.CreateSurfaceMeshTemplate().Value;
        surfMesh.Vertices = [];
        surfMesh.Triangles = [];
        surface.Surf = surfMesh;
        var chunk = surface.GetChunk<Chunk0900C003>();
        if (chunk!.U02!.Length == 0)
            chunk.U02 = [0];
        bool hasTriggerShape = false;

        foreach (var entity in entities)
        {
            if(entity.ent.Model is CPlugStaticObjectModel staticObject)
            {
                var result = TransformAndMergeStaticObject(commonEntityModel.StaticObject, staticObject, entity.position, entity.rotation);
                if (result.IsFailure)
                    return ToolResult.Fail(result);
            }
            else if(entity.ent.Model is NPlugTrigger_SWaypoint waypoint)
            {
                hasTriggerShape = true;
                var result = TransformAndMergeWaypoint(surface, waypoint, entity.position, entity.rotation);
                if (result.IsFailure)
                    return ToolResult.Fail(result);
            }
        }
        if (commonEntityModel.StaticObject.Mesh.CustomMaterials.Length > 0)
            commonEntityModel.StaticObject.Mesh.Materials = null;

        if (IsMeshCollidable(buildSettings))
        {
            commonEntityModel.StaticObject.IsMeshCollidable = true;
            commonEntityModel.StaticObject.Shape = null;
        }
        else
        {
            commonEntityModel.StaticObject.IsMeshCollidable = false;
        }

        Iso4 waypointSpawn = Iso4.Identity;
        if (hasTriggerShape)
        {
            commonEntityModel.TriggerShape = surface;
            if(compileContext.PendingWaypoints.Count(pw=>pw.Value.SpawnPosition.HasValue) > 0)
            {
                var pendingWaypoint = compileContext.PendingWaypoints.First(pw=>pw.Value.SpawnPosition.HasValue);
                waypointSpawn = Iso4Utils.IsoFromTransform(pendingWaypoint.Value.SpawnPosition.Value!, pendingWaypoint.Value.SpawnRotation.Value!);
            }
        }
        commonEntityModel.GetChunk<CGameCommonItemEntityModel.Chunk2E027000>().U03 = waypointSpawn;


        var itemModel = CreateItemTemplate();
       

        itemModel.Value.EntityModel = commonEntityModel;

        return ToolResult.Success(itemModel.Value, nameof(ItemCompiler));
    }
    IEnumerable<(Vector3 position, Quaternion rotation, EntRef ent)> ExtractEntities(CPlugPrefab prefab, Vector3 parentPosition, Quaternion parentRotation)
    {
        foreach (var ent in prefab.Ents)
        {
            var globalPosition = parentPosition + Vector3.Transform(ent.Position, parentRotation);
            var globalRotation = parentRotation * ent.Rotation.ToQuaternion();
            if (ent.Model is CPlugPrefab nestedPrefab)
            {
                foreach (var nested in ExtractEntities(nestedPrefab, globalPosition, globalRotation))
                {
                    yield return nested;
                }
            }
            else
                yield return (globalPosition, globalRotation, ent);
        }
    }

    bool IsMeshCollidable(BuildSettings buildSettings)
    {
        foreach(var cluster in buildSettings.Clusters)
        {
            if (cluster.Value.Type == ModelType.Trigger_Waypoint)
                continue;
            foreach(var instance in buildSettings.Instances.Where(i=>i.Value.ClusterKey == cluster.Key))
            {
                if(!instance.Value.Visible || !instance.Value.Collidable)
                {
                    return false;
                }
            }
        }
        return true;
    }

    ToolResult<None> TransformAndMergeStaticObject(CPlugStaticObjectModel target, CPlugStaticObjectModel staticObject, Vector3 position, Quaternion rotation)
    {
        var clone = ObjectCloner.DeepCloneObject(staticObject)!;
        var transformResult = TransformStaticObjectModel(clone, position, rotation);
        if (transformResult.IsFailure)
            return ToolResult.Fail(transformResult);
        MergeStaticObject(target, clone);
        return ToolResult.Success(None.Value, nameof(ItemCompiler));
    }
    ToolResult<None> TransformAndMergeWaypoint(CPlugSurface target, NPlugTrigger_SWaypoint waypoint, Vector3 position, Quaternion rotation)
    {
        var clone = ObjectCloner.DeepCloneObject(waypoint)!;
        var transformResult = TransformWaypointTrigger(clone, position, rotation);
        if (transformResult.IsFailure)
            return ToolResult.Fail(transformResult);
        MergeSurface(target, clone.TriggerShape!);
        return ToolResult.Success(None.Value, nameof(ItemCompiler));
    }

    ToolResult<None> TransformStaticObjectModel(CPlugStaticObjectModel staticObject, Vector3 position, Quaternion rotation)
    {
        if (position == Vector3.Zero && rotation == Quaternion.Identity)
            return ToolResult.Success(None.Value, nameof(ItemCompiler));
        foreach (var visual in staticObject.Mesh?.Visuals ?? [])
        {
            foreach (var vertexStream in visual.VertexStreams)
            {
                vertexStream.Positions = vertexStream.Positions.Select(p => (position + Vector3.Transform(p, rotation)).ToVec3()).ToArray();
                vertexStream.Normals = vertexStream.Normals.Select(n => Vector3.Transform(n, rotation).ToVec3()).ToArray();
                var tangentUs = GbxItemUtils.GetTangentUs(vertexStream);
                if (tangentUs != null)
                    GbxItemUtils.SetTangentUs(vertexStream, tangentUs.Select(t => Vector3.Transform(t, rotation).ToVec3()).ToArray());

                var tangentVs = GbxItemUtils.GetTangentVs(vertexStream);
                if (tangentVs != null)
                    GbxItemUtils.SetTangentVs(vertexStream, tangentVs.Select(t => Vector3.Transform(t, rotation).ToVec3()).ToArray());
            }
            visual.BoundingBox = GbxItemUtils.BuildBoxAligned(visual);
        }
        if(staticObject.Mesh?.LightUserModels?.Length > 0)
        {
            var skel = staticObject.Mesh.Skel!;
            var sockets = GbxItemUtils.ParseSockets(skel);
            foreach(var s in sockets)
            {
                var currentRot = s.U02.GetRotationQuaternion();
                var curPos = s.U02.GetPosition();
                var newRot = currentRot * Quaternion.Inverse(rotation);
                var newPos = position + Vector3.Transform(curPos, rotation);
                s.U02 = Iso4Utils.IsoFromTransform(newPos, newRot);
            }
            staticObject.Mesh.Skel = GbxItemUtils.CreateSkel(sockets);
        }
        if (staticObject.Shape is not null)
        {
            TransformSurface(staticObject.Shape, position, rotation);
        }
        return ToolResult.Success(nameof(ItemCompiler));
    }
    ToolResult<None> TransformSurface(CPlugSurface surface, Vector3 position, Quaternion rotation)
    {
        if (surface.Surf is not CPlugSurface.Mesh surfaceMesh)
            return ToolResult.Fail(nameof(ItemCompiler), ErrorCodes.ItemCompiler.UnsupportedSurfaceType);

        surfaceMesh.Vertices = surfaceMesh.Vertices.Select(p => (position + Vector3.Transform(p, rotation)).ToVec3()).ToArray();
        return ToolResult.Success(nameof(ItemCompiler));
    }

    void MergeStaticObject(CPlugStaticObjectModel target, CPlugStaticObjectModel staticObject)
    {
        MergeSolid2Model(target.Mesh!, staticObject.Mesh!);
        if(target.Shape is null)
            target.Shape = staticObject.Shape;
        else if (staticObject.Shape is not null)
            MergeSurface(target.Shape, staticObject.Shape);
    }
    void MergeSolid2Model(CPlugSolid2Model target, CPlugSolid2Model solid2Model)
    {
        bool targetHasLod = target.LodMaxDistAtFov90?.Length > 0;
        target.LodMaxDistAtFov90 = targetHasLod ? target.LodMaxDistAtFov90 : solid2Model.LodMaxDistAtFov90;

        var matIndexOffset = target.Materials?.Length ?? 0;
        var visualIndexOffset = target.Visuals?.Length ?? 0;
        foreach (var geom in solid2Model.ShadedGeoms)
        {
            geom.MaterialIndex += matIndexOffset;
            geom.VisualIndex += visualIndexOffset;
            geom.LodMask = targetHasLod ? LODUtils.GetAllLodsMask(target.LodMaxDistAtFov90?.Length + 1 ?? 1) : geom.LodMask;
        }
        if (!targetHasLod)
        {
            foreach (var geom in target.ShadedGeoms ?? [])
            {
                geom.LodMask = LODUtils.GetAllLodsMask(target.LodMaxDistAtFov90?.Length + 1 ?? 1);
            }
        }
       
        target.CustomMaterials = [.. target.CustomMaterials ?? [], .. solid2Model.CustomMaterials ?? []];
        target.Visuals = [.. target.Visuals ?? [], .. solid2Model.Visuals ?? []];
        target.ShadedGeoms = [.. target.ShadedGeoms ?? [], .. solid2Model.ShadedGeoms ?? []];

        // lights if any
        int lightModelOffset = target.LightUserModels?.Length ?? 0;
        foreach (var l in solid2Model.LightInsts ?? [])
        {
            l.ModelIndex += lightModelOffset;
            l.SocketIndex += lightModelOffset;
        }
        target.LightInsts = [.. target.LightInsts ?? [], .. solid2Model.LightInsts ?? []];
        target.LightUserModels = [.. target.LightUserModels ?? [], .. solid2Model.LightUserModels ?? []];
        if(target.LightInsts.Length > 0)
        {
            var sockets = target.Skel != null ? GbxItemUtils.ParseSockets(target.Skel) : [];
            sockets.AddRange(solid2Model.Skel != null ? GbxItemUtils.ParseSockets(solid2Model.Skel) : []);
            target.Skel = GbxItemUtils.CreateSkel(sockets);
        }
        target.PreLightGenerator = GbxItemUtils.MergePreLightGenerator(target.PreLightGenerator, solid2Model.PreLightGenerator);
        target.FileWriteTime = DateTime.Now;

    }
    void MergeSurface(CPlugSurface target, CPlugSurface surface)
    {
        if (target.Surf is not CPlugSurface.Mesh targetMesh || surface.Surf is not CPlugSurface.Mesh surfaceMesh)
            return;
        var vertexOffset = targetMesh.Vertices.Length;
        targetMesh.Vertices = [.. targetMesh.Vertices, .. surfaceMesh.Vertices];
        targetMesh.Triangles = [.. targetMesh.Triangles, .. surfaceMesh.Triangles.Select(i => i with { Indices = i.Indices + new Int3(vertexOffset, vertexOffset,vertexOffset)})];
    }
    ToolResult<None> TransformWaypointTrigger(NPlugTrigger_SWaypoint waypoint, Vector3 position, Quaternion rotation)
    {
        if (position == Vector3.Zero && rotation == Quaternion.Identity)
            return ToolResult.Success(nameof(ItemCompiler));
        return TransformSurface(waypoint.TriggerShape!, position, rotation);
    }



}
