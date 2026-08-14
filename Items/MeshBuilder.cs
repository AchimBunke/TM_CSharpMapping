using GBX.NET;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.MetaNotPersistent;
using GBX.NET.Engines.MwFoundations;
using GBX.NET.Engines.Plug;
using System.Dynamic;
using System.Numerics;
using System.Reflection;
using TM_GenericMapping.Common;
using TM_GenericMapping.Messaging;
using TM_GenericMapping.Templating;
using TmEssentials;
using static GBX.NET.Engines.GameData.CGameItemModel;
using static GBX.NET.Engines.Plug.CPlugPrefab;
using static GBX.NET.Engines.Plug.CPlugSkel;
using static GBX.NET.Engines.Plug.CPlugSolid2Model;
using static GBX.NET.Engines.Plug.CPlugSurface;
using static GBX.NET.Engines.Plug.CPlugVertexStream;
using static GBX.NET.Engines.Plug.NPlugTrigger_SWaypoint;

namespace TM_GenericMapping.Items;

public class MeshBuilder
{
    public struct MeshBuilderSettings
    {
        public string Author;
    };

    public enum ItemModel
    {
        MeshModeler,
        General,
        VariantList,
    }

    [Flags]
    public enum MeshBuilderOptimization
    {
        None,
        PreferMeshCollision = 1 << 0,
    }

    public struct MeshInstanceSetting
    {
        public int MeshIndex { get; set; }
        public int GroupId { get; set; }

        public bool Visible { get; set; }
        public bool Collidable { get; set; }
        public bool Movable { get; set; }
        public bool Trigger { get; set; }

        public int? LODMask { get; set; }
    }

    public struct LightInstanceSetting
    {
        public int LightIndex { get; set; }
        public int GroupId { get; set; }

        public LightType Type { get; set; }
    }

    public struct GroupSetting
    {
        public GroupSetting() { }
        public GroupType Type { get; set; }
        public int GroupId { get; set; }
        public int? VariantId { get; set; }
        public float[] LODDistances { get; set; } = [];
        public Vector3 Position { get; set; } = Vector3.Zero;
        public Quaternion Rotation { get; set; } = Quaternion.Identity;
        public NPlugDyna_SKinematicConstraint? KinematicConstraint { get; set; }
        public NPlugDynaObjectModel_SInstanceParams? DynaObjectModelParams { get; set; }
        public LegacyGameplayId? TriggerGameplayId { get; set; }
        public EGameItemWaypointType? WaypointType { get; set; }
        public bool? WaypointNoRespawn { get; set; }
        public CPlugSpawnModel? WaypointSpawnModel { get; set; }
    }

    public struct VariantSetting()
    {
        public Dictionary<string, string> Tags { get; set; } = [];
        public bool HiddenInManualCycle { get; set; }
    }


    public struct BuildSettings
    {
        public BuildSettings()
        {

        }

        public List<LightInstanceSetting> LightSettings = [];
        public List<MeshInstanceSetting> MeshSettings = [];
        public List<GroupSetting> GroupSettings = [];
        public List<VariantSetting> VariantSettings = [];

        public ItemModel TargetModel = ItemModel.General;
        public MeshBuilderOptimization Optimization = MeshBuilderOptimization.PreferMeshCollision;

        public static BuildSettings DefaultFromMesh(NormalizedItem item)
        {
            var options = new BuildSettings();
            
            List<LightInstanceSetting> lightSettings = [];
            List<MeshInstanceSetting> meshSettings = [];
            Dictionary<int, GroupSetting> groupSettings = [];
            List<VariantSetting> variantSettings = [];

            variantSettings = item.VariantGroups.Select(vg => new VariantSetting()
            {
                Tags = new Dictionary<string, string>(vg.Tags),
                HiddenInManualCycle = vg.HiddenInManualCycle
            }).ToList();

            // create meshes
            for (int i = 0; i < item.Meshes.Length; ++i)
            {
                var submesh = item.Meshes[i];
                var submeshGroup = item.Groups[submesh.GroupIndex];
                if (!submesh.Properties.HasFlag(MeshProperties.Enabled))
                    continue;
                var instanceSetting = new MeshInstanceSetting();
                instanceSetting.MeshIndex = i;
                instanceSetting.GroupId = submesh.GroupIndex;
                GroupType groupType = GroupType.StaticObject;
                switch (submesh.Type)
                {
                    case MeshType.Mesh:
                        break;
                    case MeshType.Trigger_Waypoint:
                        instanceSetting.Trigger = true;
                        groupType = GroupType.Trigger_Waypoint;
                        break;
                    case MeshType.Trigger_Special:
                        instanceSetting.Trigger = true;
                        groupType = GroupType.Trigger_Special;
                        break;
                    case MeshType.Dyna_Shape:
                        instanceSetting.Movable = true;
                        instanceSetting.Collidable = true;
                        groupType = GroupType.DynaObject;
                        break;
                    case MeshType.Static_Shape:
                        instanceSetting.Collidable = true;
                        break;
                }
                if (submesh.Properties.HasFlag(MeshProperties.Visible))
                    instanceSetting.Visible = true;
                if (submesh.Properties.HasFlag(MeshProperties.Collidable))
                    instanceSetting.Collidable = true;
                if (submesh.Properties.HasFlag(MeshProperties.LOD))
                    instanceSetting.LODMask = submesh.LODMask;

                meshSettings.Add(instanceSetting);

                if (!groupSettings.TryGetValue(submesh.GroupIndex, out var groupSetting))
                {
                    groupSettings[submesh.GroupIndex] = groupSetting = new GroupSetting()
                    {
                        LODDistances = submeshGroup.LODDistances,
                        GroupId = submesh.GroupIndex,
                        Type = groupType,
                        Position = submeshGroup.Position,
                        Rotation = submeshGroup.Rotation,
                        VariantId = submeshGroup.VariantIndex,
                    };
                }

                if (groupType != GroupType.StaticObject)
                    groupSettings[submesh.GroupIndex] = groupSetting with { Type = groupType };
                if (groupType == GroupType.DynaObject)
                    groupSettings[submesh.GroupIndex] = groupSetting with 
                    { 
                        Type = groupType,
                        DynaObjectModelParams = submeshGroup.DynaObjectModelParams,
                        KinematicConstraint = submeshGroup.KinematicConstraint
                    };
                if (groupType == GroupType.Trigger_Special)
                    groupSettings[submesh.GroupIndex] = groupSetting with 
                    { 
                        Type = groupType,
                        TriggerGameplayId = submeshGroup.TriggerGameplayId
                    };
                if (groupType == GroupType.Trigger_Waypoint)
                    groupSettings[submesh.GroupIndex] = groupSetting with
                    {
                        Type = groupType,
                        WaypointNoRespawn = submeshGroup.WaypointNoRespawn,
                        WaypointType = (EGameItemWaypointType?)submeshGroup.WaypointType,
                        WaypointSpawnModel = submeshGroup.WaypointSpawnModel,
                    };

            }

            for (int i = 0; i < item.Lights.Length; ++i)
            {
                var light = item.Lights[i];
                var lightGroup = item.Groups[light.GroupIndex];
                lightSettings.Add(new LightInstanceSetting
                {
                    LightIndex = i,
                    GroupId = light.GroupIndex,
                    Type = light.Type,
                });
            }

            options.MeshSettings = meshSettings;
            options.GroupSettings = groupSettings.OrderBy(kv => kv.Key).Select(kv => kv.Value).ToList();
            options.LightSettings = lightSettings;


            // create groups
            return options;
        }
 
    }

    //const string MovingItemTemplatePath = @"MovingItemTemplate.Item.Gbx";
    //const string EntityModelEditionTemplatePath = @"EntityModelEditionTemplate.Item.Gbx";
    //const string EntityModelTemplatePath = @"EntityModelTemplate.Item.Gbx";
    //const string TriggerItemTemplatePath = @"TriggerItemTemplate.Item.Gbx";
    //const string TriggerLayerTemplatePath = @"TriggerLayerTemplate.Item.Gbx";

    //CGameItemModel movingItemTemplate;
    //CGameItemModel entityModelEditionTemplate;
    //CGameItemModel entityModelTemplate;
    //CGameItemModel triggerItemTemplate;
    //CGameItemModel triggerLayerTemplate;

    //CGameItemModel MovingItemTemplate => (movingItemTemplate ??= Gbx.Parse<CGameItemModel>(TemplateLoader.GetTemplate(MovingItemTemplatePath)));
    //CGameItemModel EntityModelEditionTemplate => (entityModelEditionTemplate ??= Gbx.Parse<CGameItemModel>(TemplateLoader.GetTemplate(EntityModelEditionTemplatePath)));
    //CGameItemModel EntityModelTemplate => (entityModelTemplate ??= Gbx.Parse<CGameItemModel>(TemplateLoader.GetTemplate(EntityModelTemplatePath)));
    //CGameItemModel TriggerItemTemplate => (triggerItemTemplate ??= Gbx.Parse<CGameItemModel>(TemplateLoader.GetTemplate(TriggerItemTemplatePath)));
    //CGameItemModel TriggerLayerTemplateItem => (triggerLayerTemplate ??= Gbx.Parse<CGameItemModel>(TemplateLoader.GetTemplate(TriggerLayerTemplatePath)));

    //CGameCommonItemEntityModel CommonItemEntityModelTemplate => (EntityModelTemplate.EntityModel as CGameCommonItemEntityModel);
    //CPlugSolid2Model Solid2ModelTemplate => (CommonItemEntityModelTemplate.StaticObject.Mesh);
    //CPlugVisualIndexedTriangles IndexedTrianglesTemplate => (Solid2ModelTemplate.Visuals[0] as CPlugVisualIndexedTriangles);
    //CPlugVertexStream VertexStreamTemplate => IndexedTrianglesTemplate.VertexStreams[0];
    //CPlugIndexBuffer IndexBufferTemplate => IndexedTrianglesTemplate.IndexBuffer;

    //CGameCommonItemEntityModelEdition CommonItemEntityModelEditionTemplate => (EntityModelEditionTemplate.EntityModelEdition as CGameCommonItemEntityModelEdition);
    //CPlugCrystal MeshCrystalTemplate => CommonItemEntityModelEditionTemplate.MeshCrystal;
    //CPlugCrystal.GeometryLayer GeometryLayerTemplate => MeshCrystalTemplate.Layers[0] as CPlugCrystal.GeometryLayer;
    //CPlugCrystal.Crystal GeometryCrystalTemplate => GeometryLayerTemplate.Crystal;

    //CPlugPrefab CPlugPrefabTemplate => (MovingItemTemplate.EntityModel as CPlugPrefab);
    //CPlugDynaObjectModel DynaObjectModelTemplate => ItemExtensions.TryGetDynaObjectModel(MovingItemTemplate, out var dyna) ? dyna : null;
    //NPlugDyna_SKinematicConstraint KinematicConstraintTemplate => (MovingItemTemplate.EntityModel as CPlugPrefab)?.Ents[1].Model as NPlugDyna_SKinematicConstraint;
    //CPlugSurface SurfaceTemplate => DynaObjectModelTemplate.DynaShape;
    //CPlugSurface.Mesh SurfaceMeshTemplate => SurfaceTemplate.Surf as CPlugSurface.Mesh;

    //CPlugStaticObjectModel StaticObjectModelTemplate => ItemExtensions.TryGetStaticObjectModel(TriggerItemTemplate, out var staticObj) ? staticObj : null;
    //NPlugTrigger_SSpecial TriggerSpecialTemplate => ItemExtensions.TryGetTriggerSpecial(TriggerItemTemplate, out var triggerSpecial) ? triggerSpecial : null;

    //CPlugCrystal.TriggerLayer TriggerLayerTemplate => (TriggerLayerTemplateItem.EntityModelEdition as CGameCommonItemEntityModelEdition).MeshCrystal.Layers[0] as CPlugCrystal.TriggerLayer;
    //CPlugCrystal.Crystal TriggerCrystalTemplate => TriggerLayerTemplate.Crystal;


    Ident ident;

    MeshBuilderSettings _settings;
    public MeshBuilder() : this(new()) { }
    public MeshBuilder(MeshBuilderSettings settings)
    {
        _settings = settings;
        ident = new Ident("", 26, _settings.Author ?? "TM_CSharpMapping");
    }

    
     
    // ─────────────────────────────────────────────
    // Surface
    // ─────────────────────────────────────────────

    // Easiest approach: raw triangle mesh for both static and dynamic
    // If dynamic needs a convex hull later, that's a separate concern
    public ToolResult<CPlugSurface> BuildSurface(NormalizedItem item, ReadOnlySpan<int> visibles, ReadOnlySpan<int> nonCollidables, ReadOnlySpan<int> surfaces, BuildSettings buildOptions)
    {
        var surface = GbxTemplateLibrary.CreateSurfaceTemplate().Value;


        var surfMesh = GbxTemplateLibrary.CreateSurfaceMeshTemplate().Value;

        // merge all submesh positions and indices into one surface
        var allPositions = new List<Vec3>();
        var allTriangles = new List<CPlugSurface.Mesh.Triangle>();

        foreach (var subMeshIndex in surfaces)
        {
            var subMesh = item.Meshes[subMeshIndex];

            int vertOffset = allPositions.Count;
            allPositions.AddRange(subMesh.Positions);

            for (int i = 0; i < subMesh.Indices.Length; i += 3)
            {
                MaterialId materialId = MaterialId.Concrete;
                if (subMesh.SurfaceMaterialIds != null)
                    materialId = subMesh.SurfaceMaterialIds[i / 3];
                else
                    materialId = subMesh.Material.SurfacePhysicId;
                allTriangles.Add(new CPlugSurface.Mesh.Triangle
                {
                    Indices = new Int3(
                        vertOffset + subMesh.Indices[i],
                        vertOffset + subMesh.Indices[i + 1],
                        vertOffset + subMesh.Indices[i + 2]),
                    SurfaceIndex = 0,
                    U02 = (byte)materialId,
                    U03 = 0
                });
            }
        }

        surfMesh.Vertices = allPositions.ToArray();
        surfMesh.Triangles = allTriangles.ToArray();

        surface.Surf = surfMesh; 
        var chunk = surface.GetChunk<Chunk0900C003>();
        var nonCollidableLookup = nonCollidables.ToArray();
        //chunk.U02 = visibles.ToArray()
        //    .Select(idx => (idx, item.Meshes[idx]))
        //    .Select(d => nonCollidableLookup.Contains(d.idx) ? (ushort)MaterialId.NotCollidable : (ushort)d.Item2.Material.SurfacePhysicId) // non-collidables get different material
        //    .ToArray();
        if (chunk.U02.Length == 0)
            chunk.U02 = [0];
        return ToolResult.Success(surface, nameof(MeshBuilder));
    }

    // Same mesh for both — simplest valid approach for dynamic too
    // Replace with convex hull later if physics behavior needs it
    public ToolResult<CPlugSurface> BuildDynaSurface(NormalizedItem item, GroupSetting groupSetting, BuildSettings buildSettings)
    {
        var dynamicMeshes = buildSettings.MeshSettings.Where(ms => ms.GroupId == groupSetting.GroupId).Where(ms => ms.Collidable && ms.Movable).Select(ms=>ms.MeshIndex).ToArray();
        if (dynamicMeshes.Length == 0)
            return ToolResult.Fail(nameof(MeshBuilder), ErrorCodes.MeshBuilder.MissingDynaShape);
        var visibles = buildSettings.MeshSettings.Where(ms => ms.GroupId == groupSetting.GroupId).Where(ms => ms.Visible).Select(ms => ms.MeshIndex).ToArray();
        var nonCollidables = buildSettings.MeshSettings.Where(ms => ms.GroupId == groupSetting.GroupId).Where(ms => !ms.Collidable).Select(ms => ms.MeshIndex).ToArray();
        return BuildSurface(item, visibles, nonCollidables, dynamicMeshes, buildSettings);
    }
    public ToolResult<CPlugSurface> BuildStaticSurface(NormalizedItem item, GroupSetting groupSetting, BuildSettings buildSettings)
    {
        var staticMeshes = buildSettings.MeshSettings.Where(ms => ms.GroupId == groupSetting.GroupId).Where(ms => ms.Collidable && item.Meshes[ms.MeshIndex].Type != MeshType.Dyna_Shape)
            .Select(ms => ms.MeshIndex).ToArray();
        if (staticMeshes.Length == 0)
            return ToolResult.Fail(nameof(MeshBuilder), ErrorCodes.MeshBuilder.MissingStaticShape);
        var visibles = buildSettings.MeshSettings.Where(ms => ms.GroupId == groupSetting.GroupId).Where(ms => ms.Visible).Select(ms => ms.MeshIndex).ToArray();
        var nonCollidables = buildSettings.MeshSettings.Where(ms => ms.GroupId == groupSetting.GroupId).Where(ms => !ms.Collidable).Select(ms => ms.MeshIndex).ToArray();
        return BuildSurface(item, visibles, nonCollidables, staticMeshes, buildSettings);
    }


    // ─────────────────────────────────────────────
    // Solid2Model — Option A: mutate existing
    // ─────────────────────────────────────────────

    public ToolResult<None> PopulateSolid2Model(CPlugSolid2Model target, NormalizedItem item, GroupSetting groupSetting, BuildSettings buildSetting)
    {
        List<CPlugVisual> visuals = [];
        List<CPlugSolid2Model.ShadedGeom> shadedGeom = [];
        List<LightInst> lightInstances = [];
        List<CPlugLightUserModel> lightUserModels = [];
        List<Socket> sockets = [];

        Dictionary<CPlugMaterialUserInst, int> materialInstances = [];


        target.LodMaxDistAtFov90 = groupSetting.LODDistances;
        PreLightGen? preLightGen = null;
        foreach (var meshSetting in buildSetting.MeshSettings
            .Where(ms=>ms.GroupId == groupSetting.GroupId)
            .Where(ms => ms.Visible))
        {
            var submesh = item.Meshes[meshSetting.MeshIndex];

            var indexedTriangles = BuildIndexedTrianglesVisual(item, submesh);

            int visualIndex = visuals.Count;

            preLightGen = MergePreLightGenerator(submesh, preLightGen);

            visuals.Add(indexedTriangles);

            if(!materialInstances.TryGetValue(submesh.Material, out int materialIndex))
            {
                materialIndex = materialInstances.Count;
                materialInstances.Add(submesh.Material, materialIndex);
            }
            submesh.Material.GetChunk<CPlugMaterialUserInst.Chunk090FD001>().U02 = 0;

            shadedGeom.Add(new CPlugSolid2Model.ShadedGeom
            {
                VisualIndex = visualIndex,
                MaterialIndex = materialIndex,
                LodMask = meshSetting.LODMask.HasValue ? meshSetting.LODMask.Value : LODUtils.GetAllLodsMask(groupSetting.LODDistances.Length + 1),
                U01 = -1,
            });
        }

        
        foreach(var lightSetting in buildSetting.LightSettings
            .Where(ls => ls.GroupId == groupSetting.GroupId))
        {
            var light = item.Lights[lightSetting.LightIndex];
            var lightInst = new LightInst() { ModelIndex = lightUserModels.Count, SocketIndex = sockets.Count };
            lightInstances.Add(lightInst);
            
            lightUserModels.Add(CreateLightUserModel(light, lightSetting));
            sockets.Add(new Socket()
            {
                U01 = -1,
                U02 = IsoFromTransform(light.Position, light.Rotation),
                Name = light.Name
            });
        }


        target.PreLightGenerator = preLightGen;
        target.Visuals = visuals.ToArray();
        target.CustomMaterials = materialInstances.Keys.Select(k => new Material() { MaterialName = "", MaterialUserInst = k }).ToArray();
        target.ShadedGeoms = shadedGeom.ToArray();
        target.LightInsts = lightInstances.ToArray();
        target.LightUserModels = lightUserModels.ToArray();
        if(lightInstances.Count > 0)
        {
            var skel = CreateSkel(sockets);
            target.Skel = skel;
        }

        target.FileWriteTime = DateTime.Now;


        var c = target.GetChunk<CPlugSolid2Model.Chunk090BB000>();
        c.U06 = $"CSharpMapping MeshBuilder Solid2Model: {item.Name}";
        c.U18 = 0;
        return ToolResult.Success(nameof(MeshBuilder));
    }
    CPlugSkel CreateSkel(List<Socket> sockets)
    {
        var skel = new CPlugSkel()
        {
            Name = "",
            U04 = [],
            U05 = 1,
            U06 = 0,
            U07 = [],
            U08 = [],
        };
        var c = skel.CreateChunk<CPlugSkel.Chunk090BA000>();
        c.Version = 20;

        var socketField = typeof(CPlugSkel).GetField("sockets",
            BindingFlags.NonPublic | BindingFlags.Instance);
        socketField?.SetValue(skel, sockets.ToArray());

        var jointExprsField = typeof(CPlugSkel).GetField("jointExprs",
           BindingFlags.NonPublic | BindingFlags.Instance);
        jointExprsField?.SetValue(skel, new JointExpr[0]);

        var jointsField = typeof(CPlugSkel).GetField("joints",
           BindingFlags.NonPublic | BindingFlags.Instance);
        jointsField?.SetValue(skel, new Joint[0]);

        return skel;
    }
    CPlugLightUserModel CreateLightUserModel(NormalizedLight light, LightInstanceSetting lightSetting)
    {
        var lightModel = ObjectCloner.DeepCloneObject(light.LightModel);
        var c = lightModel.GetChunk<CPlugLightUserModel.Chunk090F9000>();
        c.U01 = (int)lightSetting.Type;
        return lightModel;
    }

    public static PreLightGen CreateEmtpyPreLightGen()
    {
        return new PreLightGen()
        {
            Version = 1,
            U01 = 1,
            U03 = true,
            U12 = [],
            UvGroups = [],
        };
    }
    PreLightGen? MergePreLightGenerator(NormalizedMesh mesh, PreLightGen? preLightGen)
    {
        var meshLightGen = mesh.PreLightGenerator ?? CreatePreLightGeneratorFromMeshData(mesh);
        
        if (meshLightGen == null)
            return preLightGen;

        if(preLightGen is null)
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
    public static PreLightGen? CreatePreLightGeneratorFromMeshData(NormalizedMesh mesh)
    {
        if (mesh.LightmapCoords == null)
            return null;
        var preLightGen = CreateEmtpyPreLightGen();
        preLightGen.U08 = float.MaxValue;
        preLightGen.U09 = float.MaxValue;
        preLightGen.U10 = float.MinValue;
        preLightGen.U11 = float.MinValue;
        preLightGen.U02 = ComputeLightMapSizeLengthMeters(mesh.Positions, mesh.LightmapCoords, mesh.Indices);
        preLightGen.U04 = mesh.LightmapCoords.Min(uv => uv.X);
        preLightGen.U05 = mesh.LightmapCoords.Min(uv => uv.Y);
        preLightGen.U06 = mesh.LightmapCoords.Max(uv => uv.X);
        preLightGen.U07 = mesh.LightmapCoords.Max(uv => uv.Y);
        return preLightGen;
    }
    public static float ComputeLightMapSizeLengthMeters(
        IReadOnlyList<Vec3> positions,
        IReadOnlyList<Vec2> lightmapUVs,
        IReadOnlyList<int> triangleIndices)
    {
        double sumWorldLen = 0.0;
        double sumUvLen = 0.0;

        for (int i = 0; i < triangleIndices.Count; i += 3)
        {
            int i0 = triangleIndices[i], i1 = triangleIndices[i + 1], i2 = triangleIndices[i + 2];
            int[] tri = { i0, i1, i2 };

            for (int e = 0; e < 3; e++)
            {
                int a = tri[e];
                int b = tri[(e + 1) % 3];

                sumWorldLen += Vector3.Distance(positions[a], positions[b]);
                sumUvLen += Vector2.Distance(lightmapUVs[a], lightmapUVs[b]);
            }
        }

        return sumUvLen < 1e-9 ? 0f : (float)(sumWorldLen / sumUvLen);
    }

    // ─────────────────────────────────────────────
    // Visual builder (shared)
    // ─────────────────────────────────────────────

    CPlugVisualIndexedTriangles BuildIndexedTrianglesVisual(NormalizedItem item, NormalizedMesh subMesh)
    {
        var uvs = new SortedDictionary<int, Vec2[]>();
        var colors = new SortedDictionary<int, int[]>();

        if (subMesh.TexCoords is not null)
            uvs[0] = subMesh.TexCoords;
        if (subMesh.LightmapCoords is not null)
            uvs[uvs.Count] = subMesh.LightmapCoords;
        if (subMesh.Colors is not null)
            colors[0] = subMesh.Colors;

        var vertexStream = GbxTemplateLibrary.CreateCPlugVertexStreamTemplate().Value;
        vertexStream.Positions = subMesh.Positions.ToArray();
        vertexStream.Normals = subMesh.Normals.ToArray();
        vertexStream.UVs = uvs;
        vertexStream.Colors = colors;

        // fix data decl
        var dataDeclField = typeof(CPlugVertexStream).GetField("dataDecls",
            BindingFlags.NonPublic | BindingFlags.Instance);

        var dataDecls = (dataDeclField.GetValue(vertexStream) as CPlugVertexStream.DataDecl[]).ToList();

        // clear tex coords if rex or light is not available
        if (uvs.Count <= 0)
            dataDecls.RemoveAll(decl => decl.WeightCount == CPlugVertexStream.EPlugVDcl.TexCoord0);
        if (uvs.Count <= 1)
            dataDecls.RemoveAll(decl => decl.WeightCount == CPlugVertexStream.EPlugVDcl.TexCoord1);

        if (colors.Count <= 0)
            dataDecls.RemoveAll(decl => decl.WeightCount == CPlugVertexStream.EPlugVDcl.Color0);
        else
            dataDecls.Insert(2, new CPlugVertexStream.DataDecl() { Flags1 = 546310152, Flags2 = 64, Offset = 16 });

        if(subMesh.TexCoords is null)
        {
            dataDecls.RemoveAll(decl => decl.WeightCount == EPlugVDcl.TangentU);
            dataDecls.RemoveAll(decl => decl.WeightCount == EPlugVDcl.TangentV);
        }


        //if(subMesh.TangentUs == null)
        //    dataDecls.RemoveAll(decl => decl.WeightCount == CPlugVertexStream.EPlugVDcl.TangentU);
        //if (subMesh.TangentVs == null)
        //    dataDecls.RemoveAll(decl => decl.WeightCount == CPlugVertexStream.EPlugVDcl.TangentV);

        if (colors.Count > 0)
        {
            dataDecls.FirstOrDefault(d => d.WeightCount == CPlugVertexStream.EPlugVDcl.Position)?.Flags1 = 9438208;

            dataDecls.FirstOrDefault(d => d.WeightCount == CPlugVertexStream.EPlugVDcl.Normal)?.Flags1 = 277879813;

            dataDecls.FirstOrDefault(d => d.WeightCount == CPlugVertexStream.EPlugVDcl.Color0)?.Flags1 = 546310152;

            var tex0Decl = dataDecls.FirstOrDefault(d => d.WeightCount == CPlugVertexStream.EPlugVDcl.TexCoord0);
            if (tex0Decl != null)
            {
                tex0Decl.Flags1 = 546308618;
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

        // Set flags to match what data is actually present
        //uint flags = 0x01; // positions always
        //if (vertexStream.Normals?.Length > 0) flags |= 0x02;
        //if (uVs.ContainsKey(0)) flags |= 0x04;
        //if (uVs.ContainsKey(1)) flags |= 0x08;

        //var flagsField = typeof(CPlugVertexStream).GetField("flags",
        //    BindingFlags.NonPublic | BindingFlags.Instance);
        //flagsField?.SetValue(vertexStream, flags);

        // Also fix count while you're in there
        var countField = typeof(CPlugVertexStream).GetField("count",
            BindingFlags.NonPublic | BindingFlags.Instance);
        countField?.SetValue(vertexStream, vertexStream.Positions.Length);

        var tangentUs = typeof(CPlugVertexStream).GetField("tangentUs",
         BindingFlags.NonPublic | BindingFlags.Instance)!;
        if(subMesh.TexCoords is null)
        {
            tangentUs.SetValue(vertexStream, null);
        }
        else
        {
            tangentUs.SetValue(vertexStream, subMesh.TangentUs ?? new Vec3[subMesh.Positions.Length]);
        }

        var tangentVs = typeof(CPlugVertexStream).GetField("tangentVs",
             BindingFlags.NonPublic | BindingFlags.Instance)!;
        if (subMesh.TexCoords is null)
        {
            tangentVs.SetValue(vertexStream, null);
        }
        else
        {
            tangentVs.SetValue(vertexStream, subMesh.TangentVs ?? new Vec3[subMesh.Positions.Length]);
        }

        var indexBuffer = GbxTemplateLibrary.CreateCPlugIndexBufferTemplate().Value;
        indexBuffer.Indices = subMesh.Indices.ToArray();
        indexBuffer.Flags = 2;

        var indexedTriangles = GbxTemplateLibrary.CreateCPlugVisualIndexedTrianglesTemplate().Value;
        indexedTriangles.VertexStreams = [vertexStream];
        indexedTriangles.IndexBuffer = indexBuffer;
        if (TryGetBoundingBox(item, out var bb))
            indexedTriangles.BoundingBox = bb;
        else
            indexedTriangles.BoundingBox = BuildBoxAligned(subMesh);

        var countProperty = typeof(CPlugVisualIndexedTriangles).GetProperty("Count",
            BindingFlags.NonPublic | BindingFlags.Instance);
        countProperty?.SetValue(indexedTriangles, vertexStream.Positions.Length);

        return indexedTriangles;
    }


    // ─────────────────────────────────────────────
    // Solid2Model — Option B: build from scratch
    // ─────────────────────────────────────────────

    public ToolResult<CPlugSolid2Model> BuildSolid2Model(NormalizedItem item, GroupSetting groupSetting, BuildSettings buildSettings)
    {
        // grab source solid from SourceData if available as chunk donor
        // otherwise construct empty (may be missing required chunks)
        CPlugSolid2Model solid = GbxTemplateLibrary.CreateCPlugSolid2ModelTemplate().Value;

        var result = PopulateSolid2Model(solid, item, groupSetting, buildSettings);
        if (result.IsFailure)
            return ToolResult.Fail(result);


        return ToolResult.Success(solid, nameof(MeshBuilder));
    }

    // ─────────────────────────────────────────────
    // DynaObjectModel
    // ─────────────────────────────────────────────

    // Option A: mutate existing DynaObjectModel (preferred — avoids chunk issues)
    public ToolResult<None> PopulateDynaObjectModel(CPlugDynaObjectModel target, NormalizedItem item, GroupSetting groupSetting, BuildSettings buildSettings)
    {
        var meshResult = BuildSolid2Model(item, groupSetting, buildSettings);
        if(meshResult.IsFailure)
            return ToolResult.Fail(meshResult);
        target.Mesh = meshResult.Value;

        var dynaShapeResult = BuildDynaSurface(item, groupSetting, buildSettings);
        if (dynaShapeResult.IsFailure)
            return ToolResult.Fail(dynaShapeResult);
        target.DynaShape = dynaShapeResult.Value;

        var staticShapeResult = BuildStaticSurface(item, groupSetting, buildSettings);
        if (staticShapeResult.IsFailure)
            return ToolResult.Fail(staticShapeResult);
        target.StaticShape = staticShapeResult.Value;
        return ToolResult.Success(nameof(MeshBuilder));
    }

    // Option B: build new DynaObjectModel using target as chunk donor
    public ToolResult<CPlugDynaObjectModel> BuildDynaObjectModel(NormalizedItem item, GroupSetting groupSetting, BuildSettings buildSettings)
    {
        CPlugDynaObjectModel dyna = GbxTemplateLibrary.CreateDynaObjectModelTemplate().Value;
        var result = PopulateDynaObjectModel(dyna, item, groupSetting, buildSettings);
        if (result.IsFailure)
            return ToolResult.Fail(result);
        return ToolResult.Success(dyna, nameof(MeshBuilder));
    }
    public ToolResult<EntRef> BuildDynaObjectModelEntRef(NormalizedItem item, GroupSetting groupSetting, BuildSettings buildSettings)
    {
        var dnyObjectResult = BuildDynaObjectModel(item, groupSetting, buildSettings);
        if (dnyObjectResult.IsFailure)
            return ToolResult.Fail(dnyObjectResult);
        var entRef = CreateEntRef();
        entRef.Model = dnyObjectResult.Value;
        entRef.Params = groupSetting.DynaObjectModelParams ?? new NPlugDynaObjectModel_SInstanceParams
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
        return ToolResult.Success(entRef, nameof(MeshBuilder));
    }
    public ToolResult<NPlugDyna_SKinematicConstraint> BuildKinematicConstraint(NormalizedItem item, GroupSetting groupSetting, BuildSettings buildSettings)
    {
        return ToolResult.Success(groupSetting.KinematicConstraint != null ? groupSetting.KinematicConstraint : GbxTemplateLibrary.CreateKinematicConstraintTemplate().Value, nameof(MeshBuilder));
    }


    // StaticObjectModel
    // ─────────────────────────────────────────────

    public ToolResult<None> PopulateStaticObjectModel(CPlugStaticObjectModel target, NormalizedItem item, GroupSetting groupSetting, BuildSettings buildSettings, bool forceStaticShape)
    {
        var meshResult = BuildSolid2Model(item, groupSetting, buildSettings);
        if (meshResult.IsFailure)
            return ToolResult.Fail(meshResult);
        target.Mesh = meshResult.Value;
        if (!forceStaticShape && IsMeshCollidable(groupSetting, buildSettings))
        {
            target.Shape = null;
            target.IsMeshCollidable = true;
            return ToolResult.Success(nameof(MeshBuilder));
        }
        target.IsMeshCollidable = false;
        if (!HasCollidables(groupSetting, buildSettings))
        {
            target.Shape = null;
            return ToolResult.Success(nameof(MeshBuilder));
        }

        var staticShapeResult = BuildStaticSurface(item, groupSetting, buildSettings);
        if (staticShapeResult.IsFailure)
            return ToolResult.Fail(staticShapeResult);
        target.Shape = staticShapeResult.Value;
        return ToolResult.Success(nameof(MeshBuilder));
    }

    public ToolResult<CPlugStaticObjectModel> BuildStaticObjectModel(NormalizedItem item, GroupSetting groupSetting, BuildSettings buildSettings, bool forceStaticShape)
    {
        CPlugStaticObjectModel staticObj = GbxTemplateLibrary.CreateStaticObjectModelTemplate().Value;
        var result = PopulateStaticObjectModel(staticObj, item, groupSetting, buildSettings, forceStaticShape);
        if (result.IsFailure)
            return ToolResult.Fail(result);
        return ToolResult.Success(staticObj, nameof(MeshBuilder));
    }
    bool IsMeshCollidable(GroupSetting groupSetting, BuildSettings buildSettings)
    {
        return buildSettings.MeshSettings.Where(ms => ms.GroupId == groupSetting.GroupId).All(ms => ms.Collidable && ms.Visible);
    } 
    bool HasCollidables(GroupSetting groupSetting, BuildSettings buildSettings)
    {
        return buildSettings.MeshSettings.Where(ms => ms.GroupId == groupSetting.GroupId).Any(ms => ms.Collidable);
    }

    // ─────────────────────────────────────────────
    // CommonItemEntityModelEdition (Crystal)
    // ─────────────────────────────────────────────
    public ToolResult<CPlugCrystal> BuildCrystal(NormalizedItem item, BuildSettings buildSettings)
    {
        CPlugCrystal crystal = GbxTemplateLibrary.CreateCPlugCrystalTemplate().Value;

        var result = PopulateMeshCrystal(crystal, item, buildSettings);
        if (result.IsFailure)
            return ToolResult.Fail(result);
        return ToolResult.Success(crystal, nameof(MeshBuilder));
    }

    public ToolResult<None> PopulateMeshCrystal(CPlugCrystal target, NormalizedItem item, BuildSettings buildSettings)
    {
        List<CPlugCrystal.Layer> layers = [];
        List<CPlugCrystal.Material> materials = [];
        Dictionary<CPlugMaterialUserInst, CPlugMaterialUserInst> materialMap = [];
        List<int> smoothingGroups = [];

        int layerIdx = 0;
        foreach (var meshSetting in buildSettings.MeshSettings
            .Where(ms => !ms.Trigger))
        {
            var submesh = item.Meshes[meshSetting.MeshIndex];
            var groupSetting = buildSettings.GroupSettings[submesh.GroupIndex];
            if (!materialMap.TryGetValue(submesh.Material, out var materialInstance))
                materialMap[submesh.Material] = materialInstance = ObjectCloner.DeepCloneObject(submesh.Material);
            var material = new CPlugCrystal.Material
            {
                MaterialUserInst = materialInstance,
                MaterialName = string.Empty,
            };
            materials.Add(material);
            var layer = BuildGeometryLayer(submesh, material, meshSetting,groupSetting, buildSettings);
            smoothingGroups.AddRange(layer.Crystal.Faces.Select(_ => submesh.SmoothingGroup.HasValue ? submesh.SmoothingGroup.Value : 0));
            layer.Crystal.U02 = layerIdx;
            layer.LayerId = $"Layer{layerIdx}";
            layer.IsVisible = meshSetting.Visible;
            layer.Collidable = meshSetting.Collidable;
            layers.Add(layer);
            layerIdx++;
        }
        foreach (var meshSetting in buildSettings.MeshSettings
            .Where(ms => ms.Trigger))
        {
            var submesh = item.Meshes[meshSetting.MeshIndex];
            if (!materialMap.TryGetValue(submesh.Material, out var materialInstance))
                materialMap[submesh.Material] = materialInstance = ObjectCloner.DeepCloneObject(submesh.Material);
            var material = new CPlugCrystal.Material
            {
                MaterialUserInst = materialInstance,
                MaterialName = string.Empty,
            };
            materials.Add(material);
            var layer = BuildTriggerLayer(submesh, material);
            layer.Crystal.U02 = layerIdx;
            layer.LayerId = $"Layer{layerIdx}";
            layer.LayerName = $"Trigger {submesh.Name}";
            
            layers.Add(layer);
            layerIdx++;
        }
        var normalLayerCount = layers.Count;
        foreach(var group in buildSettings.GroupSettings
            .Where(g=>g.WaypointSpawnModel != null))
        {
            var layer = BuildSpawnLayer(group.WaypointSpawnModel);

            List<CPlugCrystal.PartInLayer> parts = [];
            for (int i = 0; i < normalLayerCount; ++i)
            {
                parts.Add(new CPlugCrystal.PartInLayer() { GroupIndex = 0, LayerId = layers[i].LayerId });
            }
            layer.Mask = parts.ToArray();

            layer.LayerId = $"Layer{layerIdx}";
            layer.LayerName = $"Spawn Position";

            layers.Add(layer);
            layerIdx++;
        }
        target.Layers = layers;
        target.Materials = materials;
        var chunk = target.Chunks.Get<CPlugCrystal.Chunk09003007>();
        chunk.U01 = smoothingGroups.ToArray();
        return ToolResult.Success(nameof(MeshBuilder));
    }



    // ─────────────────────────────────────────────
    // GeometryLayer builder (shared)
    // ─────────────────────────────────────────────
    CPlugCrystal.GeometryLayer BuildGeometryLayer(NormalizedMesh submesh, CPlugCrystal.Material material, MeshInstanceSetting meshSetting, GroupSetting groupSetting, BuildSettings buildSettings)
    {
        var layer = GbxTemplateLibrary.CreateGeometryLayerTemplate().Value;
        bool isLod = meshSetting.LODMask.HasValue ? !LODUtils.IsVisibleInAllLods(meshSetting.LODMask.Value, groupSetting.LODDistances.Length + 1) : false;
        layer.LayerName = $"Geometry {submesh.Name}{(isLod ? " LOD-" + LodMaskToString(meshSetting.LODMask!.Value, groupSetting.LODDistances.Length + 1) : "")}";

        var crystal = GbxTemplateLibrary.CreateGeometryCrystalTemplate().Value;
        crystal.Positions = WeldPositions(submesh.Positions, out var remap);

        var group = crystal.Groups[0];
        group.Name = "part";
        crystal.Groups = [group];

        var faces = new List<CPlugCrystal.Face>();

        for (int i = 0; i < submesh.Indices.Length; i += 3)
        {
            var vertices = new CPlugCrystal.Vertex[3];

            for (int v = 0; v < 3; v++)
            {
                var idx = submesh.Indices[i + v];
                var texCoord = submesh.TexCoords?[idx] ?? Vec2.Zero;
                var lightmap = submesh.LightmapCoords?[idx] ?? Vec2.Zero;

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
    CPlugCrystal.TriggerLayer BuildTriggerLayer(NormalizedMesh submesh, CPlugCrystal.Material material)
    {
        var layer = GbxTemplateLibrary.CreateTriggerLayerTemplate().Value;
        layer.LayerName = $"Trigger {submesh.Name}";

        var crystal = GbxTemplateLibrary.CreateTriggerCrystalTemplate().Value;
        crystal.Positions = WeldPositions(submesh.Positions, out var remap);

        var group = crystal.Groups[0];
        group.Name = "part";
        crystal.Groups = [group];

        var faces = new List<CPlugCrystal.Face>();

        for (int i = 0; i < submesh.Indices.Length; i += 3)
        {
            var vertices = new CPlugCrystal.Vertex[3];

            for (int v = 0; v < 3; v++)
            {
                var idx = submesh.Indices[i + v];
                var texCoord = submesh.TexCoords?[idx] ?? Vec2.Zero;
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
    CPlugCrystal.SpawnPositionLayer BuildSpawnLayer(CPlugSpawnModel? model)
    {
        var layer = new CPlugCrystal.SpawnPositionLayer();
        layer.Ver = 2;
        layer.CrystalEnabled = false;
        layer.ModifierVersion = 0;
        layer.SpawnPositionVersion = 1;

        var pitchYawRoll = model.Loc.GetPitchYawRoll(inDegrees: true);
        layer.HorizontalAngle = pitchYawRoll.Y;
        layer.VerticalAngle = pitchYawRoll.X;
        layer.RollAngle = pitchYawRoll.Z;
        layer.SpawnPosition = model.Loc.GetPosition();

        return layer;
    }

    void NullifySurfaceMaterial(CPlugSurface surface)
    {
        var mesh = surface.Surf as CPlugSurface.Mesh;
        if (mesh is null || mesh.Triangles == null)
            return;
        for (int i = 0; i < mesh.Triangles.Length; ++i)
        {
            mesh.Triangles[i] = mesh.Triangles[i] with { U02 = 0 };
        }
    }
    public ToolResult<None> PopulateTriggerSpecial(NPlugTrigger_SSpecial target, NormalizedItem item, GroupSetting groupSetting, BuildSettings buildSettings)
    {
        var triggerMeshes = buildSettings.MeshSettings.Where(ms => ms.GroupId == groupSetting.GroupId).Where(ms => ms.Trigger).Select(ms => ms.MeshIndex).ToArray();
        if (triggerMeshes.Length == 0)
            return ToolResult.Fail(nameof(MeshBuilder), ErrorCodes.MeshBuilder.MissingTrigger);

        var surfaceResult = BuildSurface(item, [], [], triggerMeshes, buildSettings);
        if (surfaceResult.IsFailure)
            return ToolResult.Fail(surfaceResult);
        CPlugSurface surface = surfaceResult.Value;
        NullifySurfaceMaterial(surface);
        surface.Surf!.GameplayMainDir = new Vec3(0, 0, 1);
        target.TriggerShape = surface;
        return ToolResult.Success(nameof(MeshBuilder));
    }
    public ToolResult<NPlugTrigger_SSpecial> BuildTriggerSpecial(NormalizedItem item, LegacyGameplayId gameplayId, GroupSetting groupSetting, BuildSettings buildSettings)
    {
        NPlugTrigger_SSpecial triggerSpecial = new NPlugTrigger_SSpecial()
        {
            Version = 3
        };
        
        var result = PopulateTriggerSpecial(triggerSpecial, item, groupSetting, buildSettings);
        if (result.IsFailure)
            return ToolResult.Fail(result);

        ItemTriggerEffectConverter.ConvertEffect(gameplayId, triggerSpecial);

        return ToolResult.Success(triggerSpecial, nameof(MeshBuilder));
    }

    public ToolResult<None> PopulateTriggerWaypoint(NPlugTrigger_SWaypoint target, NormalizedItem item, GroupSetting groupSetting, BuildSettings buildSettings)
    {
        var triggerMeshes = buildSettings.MeshSettings.Where(ms => ms.GroupId == groupSetting.GroupId).Where(ms => ms.Trigger).Select(ms => ms.MeshIndex).ToArray();
        if (triggerMeshes.Length == 0)
            return ToolResult.Fail(nameof(MeshBuilder), ErrorCodes.MeshBuilder.MissingTrigger);

        var surfaceResult = BuildSurface(item, [], [], triggerMeshes, buildSettings);
        if (surfaceResult.IsFailure)
            return ToolResult.Fail(surfaceResult);
        CPlugSurface surface = surfaceResult.Value;
        NullifySurfaceMaterial(surface);
        surface.Surf!.GameplayMainDir = new Vec3(0, 0, 1);
        target.TriggerShape = surface;
        return ToolResult.Success(nameof(MeshBuilder));
    }
    public ToolResult<NPlugTrigger_SWaypoint> BuildTriggerWaypoint(NormalizedItem item, GroupSetting groupSetting, BuildSettings buildSettings)
    {
        NPlugTrigger_SWaypoint triggerWaypoint = new NPlugTrigger_SWaypoint()
        {
            Type = groupSetting.WaypointType.HasValue ? groupSetting.WaypointType.Value : EGameItemWaypointType.Checkpoint,
            NoRespawn = groupSetting.WaypointNoRespawn.HasValue ? groupSetting.WaypointNoRespawn.Value : false,
            Version = 1,
        };

        var result = PopulateTriggerWaypoint(triggerWaypoint, item, groupSetting, buildSettings);
        if (result.IsFailure)
            return ToolResult.Fail(result);

        return ToolResult.Success(triggerWaypoint, nameof(MeshBuilder));
    }

    public ToolResult<CPlugSpawnModel> BuildSpawnModel(NormalizedItem item, GroupSetting groupSetting, BuildSettings buildSettings)
    {
        CPlugSpawnModel spawnModel;
        if (groupSetting.WaypointSpawnModel is not null)
            spawnModel = ObjectCloner.DeepCloneObject(groupSetting.WaypointSpawnModel);
        else
        {
            spawnModel = CreateSpawnModel();
        }
        return ToolResult.Success(spawnModel, nameof(MeshBuilder));
    }
    public static CPlugSpawnModel CreateSpawnModel()
    {
        var spawnModel = new CPlugSpawnModel()
        {
            DefaultGravitySpawn = new Vec3(0, -1, 0),
            TorqueX = 0,
            TorqueDuration = TimeInt32.Zero,
            Loc = IsoFromTransform(Vec3.Zero, Quaternion.Identity),
        };
        var c = spawnModel.CreateChunk<CPlugSpawnModel.Chunk0917A000>();
        c.Version = 3;
        return spawnModel;
    }
    public static Iso4 IsoFromTransform(Vec3 location, Quaternion rotation)
    {
        var m = Matrix4x4.CreateFromQuaternion(rotation);

        return new Iso4(
            m.M11, m.M12, m.M13,
            m.M21, m.M22, m.M23,
            m.M31, m.M32, m.M33,
            location.X, location.Y, location.Z
        );
    }
    public static Iso4 IsoFromPitchYawRoll(Vec3 location, float pitchDeg, float yawDeg, float rollDeg)
    {
        float p = pitchDeg * MathUtils.Deg2Rad;
        float y = yawDeg * MathUtils.Deg2Rad;
        float r = rollDeg * MathUtils.Deg2Rad;

        float cp = MathF.Cos(p), sp = MathF.Sin(p);
        float cy = MathF.Cos(y), sy = MathF.Sin(y);
        float cr = MathF.Cos(r), sr = MathF.Sin(r);

        // M = Rz(roll) * Ry(yaw) * Rx(pitch)
        float XX = cr * cy;
        float XY = sp * sy * cr + sr * cp;
        float XZ = sp * sr - sy * cp * cr;

        float YX = -sr * cy;
        float YY = -sp * sr * sy + cp * cr;
        float YZ = sp * cr + sr * sy * cp;

        float ZX = sy;
        float ZY = -sp * cy;
        float ZZ = cp * cy;

        return new Iso4(
            XX, XY, XZ,
            YX, YY, YZ,
            ZX, ZY, ZZ,
            location.X, location.Y, location.Z
        );
    }

    static NPlugItem_SVariantList CreateVariantList()
    {
        var variantList = new NPlugItem_SVariantList()
        {
            Version = 1,
        };
        return variantList;
    }


    // ─────────────────────────────────────────────
    // Prefab
    // ─────────────────────────────────────────────
    public ToolResult<CPlugPrefab> BuildMixedPrefab(NormalizedItem normalizedItem, BuildSettings buildSettings)
    {
        var item = GbxTemplateLibrary.CreateMovingItemTemplate().Value;

        List<EntRef> ents = [];

        var staticGroups = buildSettings.GroupSettings.Where(gs => gs.Type == GroupType.StaticObject).ToArray();

        for (int i = 0; i < staticGroups.Length; ++i)
        {
            var groupSetting = staticGroups[i];

            var staticObjectResult = BuildStaticObjectModel(normalizedItem, groupSetting, buildSettings, forceStaticShape: true);
            if(staticObjectResult.IsFailure)
                return ToolResult.Fail(staticObjectResult);

            var ent = CreateEntRef();
            ent.Model = staticObjectResult.Value;
            ent.Position = groupSetting.Position;
            ent.Rotation = groupSetting.Rotation;
            ents.Add(ent);

        }

        var dynamicGroups = buildSettings.GroupSettings.Where(gs => gs.Type == GroupType.DynaObject).ToArray();

        for (int i = 0; i < dynamicGroups.Length; ++i)
        {
            var groupSetting = dynamicGroups[i];

            var dynamicObjectResult = BuildDynaObjectModelEntRef(normalizedItem, groupSetting, buildSettings);
            if (dynamicObjectResult.IsFailure)
                return ToolResult.Fail(dynamicObjectResult);

            var kinematicConstraintResult = BuildKinematicConstraint(normalizedItem, groupSetting, buildSettings);
            if(kinematicConstraintResult.IsFailure)
                return ToolResult.Fail(kinematicConstraintResult);


            var dynaEnt = dynamicObjectResult.Value;
            var kinematicConstraintEnt = CreateEntRef();
            kinematicConstraintEnt.Model = kinematicConstraintResult.Value;
            kinematicConstraintEnt.Params = new NPlugDyna_SPrefabConstraintParams()
            {
                Ent1 = -1,
                Ent2 = 0,
                Pos1 = Vec3.Zero,
                Pos2 = Vec3.Zero,
            };
            var dynaPrefab = CreateCPlugPrefab();
            dynaPrefab.Ents = [dynaEnt, kinematicConstraintEnt];
            var ent = CreateEntRef();
            ent.Model = dynaPrefab;
            ent.Position = groupSetting.Position;
            ent.Rotation = groupSetting.Rotation;
            ents.Add(ent);
        }


        var triggerSpecialGroups = buildSettings.GroupSettings.Where(gs => gs.Type == GroupType.Trigger_Special).ToArray();

        for (int i = 0; i < triggerSpecialGroups.Length; ++i)
        {
            var groupSetting = triggerSpecialGroups[i];

            var triggerResult = BuildTriggerSpecial(normalizedItem, groupSetting.TriggerGameplayId.HasValue ? groupSetting.TriggerGameplayId.Value : LegacyGameplayId.None, groupSetting, buildSettings);
            if (triggerResult.IsFailure)
                return ToolResult.Fail(triggerResult);

            var ent = CreateEntRef();
            ent.Model = triggerResult.Value;
            ent.Position = groupSetting.Position;
            ent.Rotation = groupSetting.Rotation;
            ents.Add(ent);

        }

        var triggerWaypointGroups = buildSettings.GroupSettings.Where(gs => gs.Type == GroupType.Trigger_Waypoint).ToArray();

        for (int i = 0; i < triggerWaypointGroups.Length; ++i)
        {
            var groupSetting = triggerWaypointGroups[i];

            var triggerResult = BuildTriggerWaypoint(normalizedItem, groupSetting, buildSettings);
            if (triggerResult.IsFailure)
                return ToolResult.Fail(triggerResult);

            var ent = CreateEntRef();
            ent.Model = triggerResult.Value;
            ent.Position = groupSetting.Position;
            ent.Rotation = groupSetting.Rotation;
            ents.Add(ent);

            if (groupSetting.WaypointType != EGameItemWaypointType.Finish)
            {
                var spawnModelResult = BuildSpawnModel(normalizedItem, groupSetting, buildSettings);
                if (spawnModelResult.IsFailure)
                    return ToolResult.Fail(spawnModelResult);
                spawnModelResult.Value.DefaultGravitySpawn = new Vec3(0, 0, 50);
                spawnModelResult.Value.TorqueDuration = TimeInt32.FromMilliseconds(500);
                spawnModelResult.Value.TorqueX = 0.5f;
                var spawnEnt = CreateEntRef();
                spawnEnt.Model = spawnModelResult.Value;
                var pos = spawnModelResult.Value.Loc.GetPosition();
                var rot = spawnModelResult.Value.Loc.GetPitchYawRoll(inDegrees: false);
                spawnEnt.Position = pos.ToVector3();
                spawnEnt.Rotation = Quaternion.CreateFromYawPitchRoll(rot.Y, rot.X, rot.Z );
                ents.Add(spawnEnt);
            }


        }


        /*
        if(ents.Count == 1 && ents[0].Model is CPlugPrefab singleNestedPrefab)
        {
            var position = ents[0].Position;
            var rotation = ents[0].Rotation;
            ents = singleNestedPrefab.Ents.ToList();
            foreach(var nestedEnt in ents)
            {
                nestedEnt.Position = position;
                nestedEnt.Rotation = rotation;
            }
        }
        */
        var prefab = (item.EntityModel as CPlugPrefab);
        prefab.FileWriteTime = DateTime.Now;
        prefab.Ents = ents.ToArray();

        return ToolResult.Success(prefab, nameof(MeshBuilder));

    }

    public ToolResult<CMwNod> BuildMixedEntityModel(NormalizedItem normalizedItem, BuildSettings buildSettings)
    {
        var prefabResult = BuildMixedPrefab(normalizedItem, buildSettings);
        if (prefabResult.IsFailure)
            return ToolResult.Fail(prefabResult);
        var prefab = prefabResult.Value;

        // convert to CGameCommonEntityModel if possible (single static object with no dynamic objects and at most one trigger)
        if (buildSettings.Optimization.HasFlag(MeshBuilderOptimization.PreferMeshCollision))
        {
            var staticShapes = buildSettings.GroupSettings.Where(gs => gs.Type == GroupType.StaticObject);
            var dynamicShapes = buildSettings.GroupSettings.Where(gs => gs.Type == GroupType.DynaObject);
            var triggerSpecials = buildSettings.GroupSettings.Where(gs => gs.Type == GroupType.Trigger_Special);
            var waypointTriggers = buildSettings.GroupSettings.Where(gs => gs.Type == GroupType.Trigger_Waypoint);

            if (staticShapes.Count() == 1 && dynamicShapes.Count() == 0 && (triggerSpecials.Count() + waypointTriggers.Count() <= 1))
            {
                bool isMeshCollidable = IsMeshCollidable(staticShapes.First(), buildSettings);
                if (isMeshCollidable) 
                {
                    var commonEntityModel = GbxTemplateLibrary.CreateCommonItemEntityModelTemplate().Value;

                    commonEntityModel.StaticObject = prefab.Ents.First(e => e.Model is CPlugStaticObjectModel).Model as CPlugStaticObjectModel;
                    commonEntityModel.StaticObject.Shape = null;
                    commonEntityModel.StaticObject.IsMeshCollidable = true;

                    if (prefab.Ents.Any(t => t.Model is NPlugTrigger_SSpecial))
                    {
                        commonEntityModel.TriggerShape = (prefab.Ents.First(t=>t.Model is NPlugTrigger_SSpecial).Model as NPlugTrigger_SSpecial).TriggerShape;
                    }
                    if (prefab.Ents.Any(t => t.Model is NPlugTrigger_SWaypoint))
                    {
                        commonEntityModel.TriggerShape = (prefab.Ents.First(t => t.Model is NPlugTrigger_SWaypoint).Model as NPlugTrigger_SWaypoint).TriggerShape;
                    }

                    return ToolResult.Success(commonEntityModel as CMwNod, nameof(MeshBuilder));
                }
            }
        }

        return ToolResult.Success(prefab as CMwNod, nameof(MeshBuilder));
    }


    // ─────────────────────────────────────────────
    // VariantList
    // ─────────────────────────────────────────────


    public ToolResult<NPlugItem_SVariantList> BuildVariantList(NormalizedItem normalizedItem, BuildSettings buildSettings)
    {

        List<NPlugItem_SVariant> variants = [];
        for (int vi = 0; vi < buildSettings.VariantSettings.Count; ++vi)
        {
            var variantGroup = buildSettings.VariantSettings[vi];
            var variant = new NPlugItem_SVariant()
            {
                Tags = variantGroup.Tags.ToDictionary(),
                HiddenInManualCycle = variantGroup.HiddenInManualCycle,
            };

            List<EntRef> ents = [];

            var staticGroups = buildSettings.GroupSettings
                .Where(gs=>gs.VariantId == vi)
                .Where(gs => gs.Type == GroupType.StaticObject).ToArray();

            for (int i = 0; i < staticGroups.Length; ++i)
            {
                var groupSetting = staticGroups[i];

                var staticObjectResult = BuildStaticObjectModel(normalizedItem, groupSetting, buildSettings, forceStaticShape: true);
                if (staticObjectResult.IsFailure)
                    return ToolResult.Fail(staticObjectResult);

                var ent = CreateEntRef();
                ent.Model = staticObjectResult.Value;
                ent.Position = groupSetting.Position;
                ent.Rotation = groupSetting.Rotation;
                ents.Add(ent);

            }

            var dynamicGroups = buildSettings.GroupSettings
                .Where(gs => gs.VariantId == vi)
                .Where(gs => gs.Type == GroupType.DynaObject).ToArray();

            for (int i = 0; i < dynamicGroups.Length; ++i)
            {
                var groupSetting = dynamicGroups[i];

                var dynamicObjectResult = BuildDynaObjectModelEntRef(normalizedItem, groupSetting, buildSettings);
                if (dynamicObjectResult.IsFailure)
                    return ToolResult.Fail(dynamicObjectResult);

                var kinematicConstraintResult = BuildKinematicConstraint(normalizedItem, groupSetting, buildSettings);
                if (kinematicConstraintResult.IsFailure)
                    return ToolResult.Fail(kinematicConstraintResult);


                var dynaEnt = dynamicObjectResult.Value;
                var kinematicConstraintEnt = CreateEntRef();
                kinematicConstraintEnt.Model = kinematicConstraintResult.Value;
                kinematicConstraintEnt.Params = new NPlugDyna_SPrefabConstraintParams()
                {
                    Ent1 = -1,
                    Ent2 = 0,
                    Pos1 = Vec3.Zero,
                    Pos2 = Vec3.Zero,
                };
                var dynaPrefab = CreateCPlugPrefab();
                dynaPrefab.Ents = [dynaEnt, kinematicConstraintEnt];
                var ent = CreateEntRef();
                ent.Model = dynaPrefab;
                ent.Position = groupSetting.Position;
                ent.Rotation = groupSetting.Rotation;
                ents.Add(ent);
            }


            var triggerSpecialGroups = buildSettings.GroupSettings
                .Where(gs => gs.VariantId == vi)
                .Where(gs => gs.Type == GroupType.Trigger_Special).ToArray();

            for (int i = 0; i < triggerSpecialGroups.Length; ++i)
            {
                var groupSetting = triggerSpecialGroups[i];

                var triggerResult = BuildTriggerSpecial(normalizedItem, groupSetting.TriggerGameplayId.HasValue ? groupSetting.TriggerGameplayId.Value : LegacyGameplayId.None, groupSetting, buildSettings);
                if (triggerResult.IsFailure)
                    return ToolResult.Fail(triggerResult);

                var ent = CreateEntRef();
                ent.Model = triggerResult.Value;
                ent.Position = groupSetting.Position;
                ent.Rotation = groupSetting.Rotation;
                ents.Add(ent);

            }

            var triggerWaypointGroups = buildSettings.GroupSettings
                .Where(gs => gs.VariantId == vi)
                .Where(gs => gs.Type == GroupType.Trigger_Waypoint).ToArray();

            for (int i = 0; i < triggerWaypointGroups.Length; ++i)
            {
                var groupSetting = triggerWaypointGroups[i];

                var triggerResult = BuildTriggerWaypoint(normalizedItem, groupSetting, buildSettings);
                if (triggerResult.IsFailure)
                    return ToolResult.Fail(triggerResult);

                var ent = CreateEntRef();
                ent.Model = triggerResult.Value;
                ent.Position = groupSetting.Position;
                ent.Rotation = groupSetting.Rotation;
                ents.Add(ent);

                if (groupSetting.WaypointType != EGameItemWaypointType.Finish)
                {
                    var spawnModelResult = BuildSpawnModel(normalizedItem, groupSetting, buildSettings);
                    if (spawnModelResult.IsFailure)
                        return ToolResult.Fail(spawnModelResult);
                    spawnModelResult.Value.DefaultGravitySpawn = new Vec3(0, 0, 50);
                    spawnModelResult.Value.TorqueDuration = TimeInt32.FromMilliseconds(500);
                    spawnModelResult.Value.TorqueX = 0.5f;
                    var spawnEnt = CreateEntRef();
                    spawnEnt.Model = spawnModelResult.Value;
                    var pos = spawnModelResult.Value.Loc.GetPosition();
                    var rot = spawnModelResult.Value.Loc.GetPitchYawRoll(inDegrees: false);
                    spawnEnt.Position = pos.ToVector3();
                    spawnEnt.Rotation = Quaternion.CreateFromYawPitchRoll(rot.Y, rot.X, rot.Z);
                    ents.Add(spawnEnt);
                }


            }

            var prefab = CreateCPlugPrefab();
            prefab.FileWriteTime = DateTime.Now;
            prefab.Ents = ents.ToArray();

            variant.EntityModel = prefab;
            variants.Add(variant);
        }

        var variantList = CreateVariantList();
        variantList.Variants = variants.ToArray();

        return ToolResult.Success(variantList, nameof(MeshBuilder));

    }

    // ─────────────────────────────────────────────
    // other helpers
    // ─────────────────────────────────────────────

    BoxAligned BuildBoxAligned(NormalizedMesh subMesh)
    {
        Vec3 min = subMesh.Positions[0];
        Vec3 max = subMesh.Positions[0];

        foreach (var p in subMesh.Positions)
        {
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }
        var center = (min + max) * 0.5f;
        var extent = (max - min) * 0.5f;

        var box = new BoxAligned(
            center.X, center.Y, center.Z,
            extent.X, extent.Y, extent.Z
        );
        return box;
    }
    bool TryGetBoundingBox(NormalizedItem mesh, out BoxAligned boundingBox)
    {
        boundingBox = default;
        if (mesh.SourceData is CPlugSolid2Model solid2Model)
        {
            if (solid2Model.Visuals.Count() == 1)
            {
                boundingBox = solid2Model.Visuals[0].BoundingBox;
                return true;
            }
        }
        return false;
    }
    EntRef CreateEntRef()
    {
        var entRef = new EntRef
        {
            Model = null,
            Position = Vec3.Zero,
            Rotation = Quat.Identity,
            Params = null,
            ModelFile = null,
            U01 = "",
        };
        return entRef;
    }
    CPlugPrefab CreateCPlugPrefab()
    {
        var prefab = new CPlugPrefab()
        {
            Ents = [],
            FileWriteTime = DateTime.Now,
            Version = 11,
        };
        return prefab;
    }

    // Deduplicates a fully-split position array and returns a remapping table:
    // remap[oldIndex] = newIndex into the returned unique positions list.
    static Vec3[] WeldPositions(Vec3[] positions, out int[] remap)
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

    int[] FindSolidCollidableGeometries(NormalizedItem mesh)
    {
        List<int> geometries = [];
        for (int i = 0; i < mesh.Meshes.Length; ++i)
            if (mesh.Meshes[i].Type == MeshType.Mesh)
                geometries.Add(i);

        return geometries.ToArray();
    }
    int[] FindSubmeshes(NormalizedItem mesh, params ReadOnlySpan<MeshType> types)
    {
        List<int> geometries = [];
        for (int i = 0; i < mesh.Meshes.Length; ++i)
            if (types.Contains(mesh.Meshes[i].Type))
                geometries.Add(i);

        return geometries.ToArray();
    }
    string LodMaskToString(int lodMask, int lodCount)
    {
        return Convert.ToString(lodMask, 2).PadLeft(lodCount, '0');
    }
    void FillItemDataFromMesh(CGameItemModel item, NormalizedItem normalizedItem)
    {
        item.Name = string.IsNullOrWhiteSpace(normalizedItem.Name) ? "New Item" : normalizedItem.Name;
        ChunkSafeItemOperations.SetIcon(item, normalizedItem.Icon, normalizedItem.IconWebP);
        item.Description = string.IsNullOrWhiteSpace(normalizedItem.Description) ? "No Description" : normalizedItem.Description;
        item.Ident = ident;
        item.DefaultPlacement = GbxTemplateLibrary.CreatePlacementParamTemplateWithPlacementClass();
        if(item.EntityModel is CPlugPrefab prefab)
            prefab.FileWriteTime = DateTime.Now;
        
    }
    void FixItemChunks(CGameItemModel item, NormalizedItem normalizedItem)
    {
        var c1 = item.GetChunk<CGameItemModel.Chunk2E00201F>();
        //c1.U08 = 1;
        //item.GetChunk<CGameItemModel.Chunk2E002020>().U03 = !string.IsNullOrEmpty(item.IconFid);
    }

    Vec2 QuantizeLightmapCoord(Vec2 coord)
    {
        return new Vec2((ushort)MathF.Round(coord.X * ushort.MaxValue) / (float)ushort.MaxValue, 
            (ushort)MathF.Round(coord.Y * ushort.MaxValue) / (float)ushort.MaxValue);
    }

    void SetItemWaypointIfWaypointSetting(CGameItemModel item, BuildSettings buildSettings)
    {
        EWaypointType? waypointType = (EWaypointType?)buildSettings.GroupSettings.FirstOrDefault(gs => gs.WaypointType.HasValue).WaypointType;
        if (waypointType.HasValue)
            item.WaypointType = waypointType.Value;
    }

    //public ToolResult<CGameItemModel> BuildStaticObjectModelItem(NormalizedItem mesh, BuildSettings buildOptions)
    //{
    //    return ToolResult.Fail(nameof(MeshBuilder), "");

    //    //var item = ObjectCloner.DeepCloneObject(EntityModelTemplate);
    //    //var staticObjectResult = BuildStaticObjectModel(mesh, buildOptions);
    //    //if (staticObjectResult.IsFailure)
    //    //    return ToolResult.Fail(staticObjectResult);
    //    //(item.EntityModel as CGameCommonItemEntityModel).StaticObject = staticObjectResult.Value;
    //    //FillItemDataFromMesh(item, mesh);
    //    //return ToolResult.Success(item, nameof(MeshBuilder));
    //}

    public ToolResult<CGameItemModel> BuildCrystalItem(NormalizedItem normalizedItem, BuildSettings buildOptions)
    {
        var item = GbxTemplateLibrary.CreateCommonItemEntityModelEditionItemTemplate().Value;
        var crystalResult = BuildCrystal(normalizedItem, buildOptions);
        if (crystalResult.IsFailure)
            return ToolResult.Fail(crystalResult);
        (item.EntityModelEdition as CGameCommonItemEntityModelEdition).MeshCrystal = crystalResult.Value;
        SetItemWaypointIfWaypointSetting(item, buildOptions);
        FillItemDataFromMesh(item, normalizedItem);
        FixItemChunks(item, normalizedItem);
        return ToolResult.Success(item, nameof(MeshBuilder));
    }
    public ToolResult<CGameItemModel> BuildCrystalWaypointItem(NormalizedItem normalizedItem, EWaypointType waypointType, BuildSettings buildOptions)
    {
        var item = BuildCrystalItem(normalizedItem, buildOptions);
        if (item.IsFailure)
            return ToolResult.Fail(item);
        item.Value.WaypointType = waypointType;
        return item;
    }

    //public ToolResult<CGameItemModel> BuildMovingItem(NormalizedItem mesh, BuildSettings buildOptions)
    //{
    //    return ToolResult.Fail(nameof(MeshBuilder), "");

    //    //var item = ObjectCloner.DeepCloneObject(MovingItemTemplate);
    //    //ItemExtensions.TryGetDynaModelEntRef(item, out var entRef);
    //    //var dynaResult = BuildDynaObjectModel(mesh, buildOptions);
    //    //if (dynaResult.IsFailure)
    //    //    return ToolResult.Fail(dynaResult);
    //    //entRef.Model = dynaResult.Value;
    //    //FillItemDataFromMesh(item, mesh);
    //    //return ToolResult.Success(item, nameof(MeshBuilder));
    //}

    //public ToolResult<CGameItemModel> BuildTriggerSpecialItem(NormalizedItem mesh, LegacyGameplayId gameplayId, BuildSettings buildOptions)
    //{
    //    return ToolResult.Fail(nameof(MeshBuilder), "");

    //    //var item = ObjectCloner.DeepCloneObject(TriggerItemTemplate);
    //    //ItemExtensions.TryGetStaticModelEntRef(item, out var entRef);
    //    //if (buildOptions.IsMeshCollidable)
    //    //    buildOptions = buildOptions with { StaticShapes = buildOptions.Geometries, IsMeshCollidable = false }; // need to create static shape for plug-prefab

    //    //var staticResult = BuildStaticObjectModel(mesh, buildOptions);
    //    //if (staticResult.IsFailure)
    //    //    return ToolResult.Fail(staticResult);
    //    //entRef.Model = staticResult.Value;
    //    //ItemExtensions.TryGetTriggerSpecialEntRef(item, out var triggerSpecialEntRef);
    //    //var triggerResult = BuildTriggerSpecial(mesh, gameplayId, buildOptions);
    //    //if (triggerResult.IsFailure)
    //    //    return ToolResult.Fail(triggerResult);

    //    //triggerSpecialEntRef.Model = triggerResult.Value;
    //    //item.Chunks.Get<CGameItemModel.Chunk2E00201F>().U08 = 0;
    //    //FillItemDataFromMesh(item, mesh);
    //    //return ToolResult.Success(item, nameof(MeshBuilder));
    //}



   
    public ToolResult<CGameItemModel> BuildGeneralItem(NormalizedItem normalizedItem, BuildSettings buildSettings)
    {
        var item = GbxTemplateLibrary.CreateMovingItemTemplate().Value;

        var mixedPrefabResult = BuildMixedEntityModel(normalizedItem, buildSettings);
        if (mixedPrefabResult.IsFailure)
            return ToolResult.Fail(mixedPrefabResult);

        item.EntityModel = mixedPrefabResult.Value;

        SetItemWaypointIfWaypointSetting(item, buildSettings);

        FillItemDataFromMesh(item, normalizedItem);
        FixItemChunks(item, normalizedItem);
        return ToolResult.Success(item, nameof(MeshBuilder));
    }

    public ToolResult<CGameItemModel> BuildGeneralItem(NormalizedItem normalizedItem, EWaypointType overwriteWaypointType, BuildSettings buildSettings)
    {
        var result = BuildGeneralItem(normalizedItem, buildSettings);
        if (result.IsFailure)
            return ToolResult.Fail(result);
        result.Value.WaypointType = overwriteWaypointType;
        return result;
    }
    public ToolResult<CGameItemModel> BuildGeneralItem(NormalizedItem normalizedItem, LegacyGameplayId overwriteGameplayid, BuildSettings buildSettings)
    {
        var result = BuildGeneralItem(normalizedItem, buildSettings);
        if (result.IsFailure)
            return ToolResult.Fail(result);
        if(!ItemTriggerEffectConverter.TryConvertEffect(overwriteGameplayid, result.Value))
            return ToolResult.Fail(nameof(MeshBuilder), ErrorCodes.MeshBuilder.MissingTrigger);
        return result;
    }

    public ToolResult<CGameItemModel> BuildVariantItem(NormalizedItem normalizedItem, BuildSettings buildSettings)
    {
        var item = GbxTemplateLibrary.CreateVariantsItemTemplate().Value;

        var variantListResult = BuildVariantList(normalizedItem, buildSettings);
        if (variantListResult.IsFailure)
            return ToolResult.Fail(variantListResult);

        item.EntityModel = variantListResult.Value;

        SetItemWaypointIfWaypointSetting(item, buildSettings);

        FillItemDataFromMesh(item, normalizedItem);
        FixItemChunks(item, normalizedItem);
        return ToolResult.Success(item, nameof(MeshBuilder));
    }

    public ToolResult<CGameItemModel> BuildItem(NormalizedItem normalizedItem, BuildSettings buildSettings)
    {
        switch(buildSettings.TargetModel)
        {
            case ItemModel.General:
                return BuildGeneralItem(normalizedItem, buildSettings);
            case ItemModel.MeshModeler:
                return BuildCrystalItem(normalizedItem, buildSettings);
            case ItemModel.VariantList:
                return BuildVariantItem(normalizedItem, buildSettings);
            default:
                return ToolResult.Fail(nameof(MeshBuilder), ErrorCodes.MeshBuilder.UnsupportedType);
        }
    }

}