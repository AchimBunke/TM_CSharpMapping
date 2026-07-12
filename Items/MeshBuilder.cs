using GBX.NET;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.MwFoundations;
using GBX.NET.Engines.Plug;
using GBX.NET.Serialization;
using System.ComponentModel;
using System.Numerics;
using System.Reflection;
using TM_GenericMapping.Common;
using static GBX.NET.Engines.Plug.CPlugSurface;

namespace TM_GenericMapping.Items;

public class MeshBuilder
{
    public struct MeshBuilderSettings
    {
        public string Author;
    };

    const string MovingItemTemplatePath = @"MovingItemTemplate.Item.Gbx";
    const string EntityModelEditionTemplatePath = @"EntityModelEditionTemplate.Item.Gbx";
    const string EntityModelTemplatePath = @"EntityModelTemplate.Item.Gbx";
    const string TriggerItemTemplatePath = @"TriggerItemTemplate.Item.Gbx";

    CGameItemModel movingItemTemplate;
    CGameItemModel entityModelEditionTemplate;
    CGameItemModel entityModelTemplate;
    CGameItemModel triggerItemTemplate;

    CGameItemModel MovingItemTemplate => (movingItemTemplate ??= Gbx.Parse<CGameItemModel>(TemplateLoader.GetTemplate(MovingItemTemplatePath)));
    CGameItemModel EntityModelEditionTemplate => (entityModelEditionTemplate ??= Gbx.Parse<CGameItemModel>(TemplateLoader.GetTemplate(EntityModelEditionTemplatePath)));
    CGameItemModel EntityModelTemplate => (entityModelTemplate ??= Gbx.Parse<CGameItemModel>(TemplateLoader.GetTemplate(EntityModelTemplatePath)));
    CGameItemModel TriggerItemTemplate => (triggerItemTemplate ??= Gbx.Parse<CGameItemModel>(TemplateLoader.GetTemplate(TriggerItemTemplatePath)));

    CGameCommonItemEntityModel CommonItemEntityModelTemplate => (EntityModelTemplate.EntityModel as CGameCommonItemEntityModel);
    CPlugSolid2Model Solid2ModelTemplate => (CommonItemEntityModelTemplate.StaticObject.Mesh);
    CPlugVisualIndexedTriangles IndexedTrianglesTemplate => (Solid2ModelTemplate.Visuals[0] as CPlugVisualIndexedTriangles);
    CPlugVertexStream VertexStreamTemplate => IndexedTrianglesTemplate.VertexStreams[0];
    CPlugIndexBuffer IndexBufferTemplate => IndexedTrianglesTemplate.IndexBuffer;
    CGameItemPlacementParam PlacementParamTemplate => EntityModelEditionTemplate.DefaultPlacement;

    CGameCommonItemEntityModelEdition CommonItemEntityModelEditionTemplate => (EntityModelEditionTemplate.EntityModelEdition as CGameCommonItemEntityModelEdition);
    CPlugCrystal MeshCrystalTemplate => CommonItemEntityModelEditionTemplate.MeshCrystal;
    CPlugCrystal.GeometryLayer LayerTemplate => MeshCrystalTemplate.Layers[0] as CPlugCrystal.GeometryLayer;
    CPlugCrystal.Crystal CrystalTemplate => LayerTemplate.Crystal;

    CPlugPrefab CPlugPrefabTemplate => (MovingItemTemplate.EntityModel as CPlugPrefab);
    CPlugDynaObjectModel DynaObjectModelTemplate => ItemExtensions.TryGetDynaObjectModel(MovingItemTemplate, out var dyna) ? dyna : null;
    CPlugSurface SurfaceTemplate => DynaObjectModelTemplate.DynaShape;
    CPlugSurface.Mesh SurfaceMeshTemplate => SurfaceTemplate.Surf as CPlugSurface.Mesh;

    CPlugStaticObjectModel StaticObjectModelTemplate => ItemExtensions.TryGetStaticObjectModel(TriggerItemTemplate, out var staticObj) ? staticObj : null;
    NPlugTrigger_SSpecial TriggerSpecialTemplate => ItemExtensions.TryGetTriggerSpecial(TriggerItemTemplate, out var triggerSpecial) ? triggerSpecial : null;


    Ident ident;

    MeshBuilderSettings _settings;
    public MeshBuilder() : this(new()) { }
    public MeshBuilder(MeshBuilderSettings settings)
    {
        _settings = settings;
        ident = new Ident("", 26, _settings.Author ?? "");
    }

    // ─────────────────────────────────────────────
    // Surface
    // ─────────────────────────────────────────────

    // Easiest approach: raw triangle mesh for both static and dynamic
    // If dynamic needs a convex hull later, that's a separate concern
    public CPlugSurface BuildSurface(NormalizedMesh mesh)
    {
        var surface = ObjectCloner.DeepCloneObject(SurfaceTemplate);


        var surfMesh = ObjectCloner.DeepCloneObject(SurfaceMeshTemplate);

        // merge all submesh positions and indices into one surface
        var allPositions = new List<Vec3>();
        var allTriangles = new List<CPlugSurface.Mesh.Triangle>();

        foreach (var subMesh in mesh.Submeshes)
        {
            int vertOffset = allPositions.Count;
            allPositions.AddRange(subMesh.Positions);

            for (int i = 0; i < subMesh.Indices.Length; i += 3)
            {
                allTriangles.Add(new CPlugSurface.Mesh.Triangle
                {
                    Indices = new Int3(
                        vertOffset + subMesh.Indices[i],
                        vertOffset + subMesh.Indices[i + 1],
                        vertOffset + subMesh.Indices[i + 2]),
                    SurfaceIndex = 0,
                    U02 = 0/*(byte)subMesh.Material.SurfacePhysicId*/,
                    U03 = 0
                });
            }
        }

        surfMesh.Vertices = allPositions.ToArray();
        surfMesh.Triangles = allTriangles.ToArray();

        surface.Surf = surfMesh; 
        var chunk = surface.GetChunk<Chunk0900C003>();
        chunk.U02 = mesh.Submeshes.Select(submesh => (ushort)submesh.Material.SurfacePhysicId).ToArray();
        return surface;
    }

    // Same mesh for both — simplest valid approach for dynamic too
    // Replace with convex hull later if physics behavior needs it
    public CPlugSurface BuildDynaSurface(NormalizedMesh mesh)
    {
        return mesh.SourceData switch
        {
            CPlugDynaObjectModel dynaModel when dynaModel.DynaShape != null => ObjectCloner.DeepCloneObject(dynaModel.DynaShape),
            CPlugDynaObjectModel dynaModel when dynaModel.StaticShape != null => ObjectCloner.DeepCloneObject(dynaModel.StaticShape),
            CPlugStaticObjectModel staticModel when staticModel.Shape != null => ObjectCloner.DeepCloneObject(staticModel.Shape),
            _ => BuildSurface(mesh)
        };

    }
    public CPlugSurface BuildStaticSurface(NormalizedMesh mesh)
    {
        return mesh.SourceData switch
        {
            CPlugDynaObjectModel dynaModel when dynaModel.StaticShape != null => ObjectCloner.DeepCloneObject(dynaModel.StaticShape),
            CPlugStaticObjectModel staticModel when staticModel.Shape != null => ObjectCloner.DeepCloneObject(staticModel.Shape),
            CPlugDynaObjectModel dynaModel when dynaModel.DynaShape != null => ObjectCloner.DeepCloneObject(dynaModel.DynaShape),
            _ => BuildSurface(mesh)
        };

    }


    // ─────────────────────────────────────────────
    // Solid2Model — Option A: mutate existing
    // ─────────────────────────────────────────────

    public void PopulateSolid2Model(CPlugSolid2Model target, NormalizedMesh mesh)
    {
        List<CPlugVisual> visuals = [];
        List<CPlugSolid2Model.Material> materials = [];
        List<CPlugSolid2Model.ShadedGeom> shadedGeom = []; 

        foreach (var submesh in mesh.Submeshes)
        {
            var indexedTriangles = BuildIndexedTrianglesVisual(mesh, submesh);

            int visualIndex = visuals.Count;
            int materialIndex = materials.Count;

            visuals.Add(indexedTriangles);
            materials.Add(new CPlugSolid2Model.Material
            {
                MaterialUserInst = submesh.Material,
                MaterialName = submesh.Material.MaterialName
            });
            shadedGeom.Add(new CPlugSolid2Model.ShadedGeom
            {
                VisualIndex = visualIndex,
                MaterialIndex = materialIndex,
                LodMask = 1,
                U01 = -1,
            });
        }

        target.Visuals = visuals.ToArray();
        target.CustomMaterials = materials.ToArray();
        target.ShadedGeoms = shadedGeom.ToArray();
        target.FileWriteTime = DateTime.Now;
    }

    // ─────────────────────────────────────────────
    // Solid2Model — Option B: build from scratch
    // ─────────────────────────────────────────────

    public CPlugSolid2Model BuildSolid2Model(NormalizedMesh mesh)
    {
        // grab source solid from SourceData if available as chunk donor
        // otherwise construct empty (may be missing required chunks)
        CPlugSolid2Model solid;
        if (mesh.SourceData is CPlugSolid2Model source)
            return ObjectCloner.DeepCloneObject(source);// immediate return
        else
            solid = ObjectCloner.DeepCloneObject(Solid2ModelTemplate);

        PopulateSolid2Model(solid, mesh);

        return solid;
    }

    // ─────────────────────────────────────────────
    // DynaObjectModel
    // ─────────────────────────────────────────────

    // Option A: mutate existing DynaObjectModel (preferred — avoids chunk issues)
    public void PopulateDynaObjectModel(CPlugDynaObjectModel target, NormalizedMesh mesh)
    {
        target.Mesh = BuildSolid2Model(mesh);
        target.DynaShape = BuildDynaSurface(mesh);
        target.StaticShape = BuildStaticSurface(mesh);
    }

    // Option B: build new DynaObjectModel using target as chunk donor
    public CPlugDynaObjectModel BuildDynaObjectModel(NormalizedMesh mesh)
    {
        CPlugDynaObjectModel dyna;
        if (mesh.SourceData is CPlugDynaObjectModel source)
            dyna = ObjectCloner.DeepCloneObject(source);
        else
            dyna = ObjectCloner.DeepCloneObject(DynaObjectModelTemplate);
        PopulateDynaObjectModel(dyna, mesh);
        return dyna;
    }


    // StaticObjectModel
    // ─────────────────────────────────────────────

    public void PopulateStaticObjectModel(CPlugStaticObjectModel target, NormalizedMesh mesh)
    {
        target.Mesh = BuildSolid2Model(mesh);
        target.Shape = BuildStaticSurface(mesh);
    }

    public CPlugStaticObjectModel BuildStaticObjectModel(NormalizedMesh mesh)
    {
        CPlugStaticObjectModel staticObj;
        if (mesh.SourceData is CPlugStaticObjectModel source)
            staticObj = ObjectCloner.DeepCloneObject(source);
        else
            staticObj = ObjectCloner.DeepCloneObject(StaticObjectModelTemplate);
        PopulateStaticObjectModel(staticObj, mesh);
        return staticObj;
    }

    // ─────────────────────────────────────────────
    // CommonItemEntityModelEdition (Crystal)
    // ─────────────────────────────────────────────
    public CPlugCrystal BuildCrystal(NormalizedMesh mesh)
    {
        CPlugCrystal crystal;
        if (mesh.SourceData is CPlugCrystal source)
            crystal = ObjectCloner.DeepCloneObject(source);
        else
            crystal = ObjectCloner.DeepCloneObject(MeshCrystalTemplate);

        PopulateMeshCrystal(crystal, mesh);
        return crystal;
    }

    public void PopulateMeshCrystal(CPlugCrystal target, NormalizedMesh mesh)
    {
        List<CPlugCrystal.Layer> layers = [];
        List<CPlugCrystal.Material> materials = [];
        List<CPlugSolid2Model.ShadedGeom> shadedGeom = [];

        foreach (var submesh in mesh.Submeshes)
        {
            var material = new CPlugCrystal.Material
            {
                MaterialUserInst = submesh.Material,
                MaterialName = string.Empty,
            };
            materials.Add(material);
            var layer = BuildLayer(submesh, material);
            layer.LayerId = $"Layer{layers.Count}";
            layers.Add(layer);
        
        }
        target.Layers = layers;
        target.Materials = materials;
        var chunk = target.Chunks.Get<CPlugCrystal.Chunk09003007>();
        chunk.U01 = Enumerable.Repeat(2, layers.Sum(l => (l as CPlugCrystal.GeometryLayer).Crystal.Faces.Length)).ToArray();
    }

    // ─────────────────────────────────────────────
    // Visual builder (shared)
    // ─────────────────────────────────────────────

    CPlugVisualIndexedTriangles BuildIndexedTrianglesVisual(NormalizedMesh mesh, NormalizedSubmesh subMesh)
    {
        var uvs = new SortedDictionary<int, Vec2[]>();
        var colors = new SortedDictionary<int, int[]>();

        if (subMesh.TexCoords is not null)
            uvs[0] = subMesh.TexCoords;
        if (subMesh.LightmapCoords is not null)
            uvs[1] = subMesh.LightmapCoords;
        if (subMesh.Colors is not null)
            colors[0] = subMesh.Colors;

        var vertexStream = ObjectCloner.DeepCloneObject(VertexStreamTemplate);
        vertexStream.Positions = subMesh.Positions.ToArray();
        vertexStream.Normals = subMesh.Normals.ToArray();
        vertexStream.UVs = uvs;
        vertexStream.Colors = colors;

        // fix data decl
        var dataDeclField = typeof(CPlugVertexStream).GetField("dataDecls",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var dataDecls = (dataDeclField.GetValue(vertexStream) as CPlugVertexStream.DataDecl[]).ToList();
        if (uvs.Count <= 0)
            dataDecls.RemoveAll(decl => decl.WeightCount == CPlugVertexStream.EPlugVDcl.TexCoord0);
        if (uvs.Count <= 1)
            dataDecls.RemoveAll(decl => decl.WeightCount == CPlugVertexStream.EPlugVDcl.TexCoord1);
        if (colors.Count <= 0)
            dataDecls.RemoveAll(decl => decl.WeightCount == CPlugVertexStream.EPlugVDcl.Color0);
        else
            dataDecls.Insert(2 ,new CPlugVertexStream.DataDecl() { Flags1 = 546310152, Flags2 = 64, Offset = 16});

        if(colors.Count > 0)
        {
            dataDecls.ElementAt(0).Flags1 = 9438208;

            dataDecls.ElementAt(1).Flags1 = 277879813;

            dataDecls.ElementAt(2).Flags1 = 546310152;

            dataDecls.ElementAt(3).Flags1 = 546308618;
            dataDecls.ElementAt(3).Flags2 = 80;
            dataDecls.ElementAt(3).Offset = 20;

            dataDecls.ElementAt(4).Flags1 = 277879826;
            dataDecls.ElementAt(4).Flags2 = 112;
            dataDecls.ElementAt(4).Offset = 28;

            dataDecls.ElementAt(5).Flags1 = 277879828;
            dataDecls.ElementAt(5).Flags2 = 128;
            dataDecls.ElementAt(5).Offset = 32;
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

        if(uvs.TryGetValue(0, out var uv0))
        {
            var tangentUs = typeof(CPlugVertexStream).GetField("tangentUs",
               BindingFlags.NonPublic | BindingFlags.Instance);
            if(subMesh.TangentUs != null)
                tangentUs?.SetValue(vertexStream, subMesh.TangentUs);
            else
                tangentUs?.SetValue(vertexStream, new Vec3[uv0.Length]);
        }
        if (uvs.TryGetValue(1, out var uv1))
        {
            var tangentVs = typeof(CPlugVertexStream).GetField("tangentVs",
               BindingFlags.NonPublic | BindingFlags.Instance);
            if(subMesh.TangentVs != null)
                tangentVs?.SetValue(vertexStream, subMesh.TangentVs);
            else
                tangentVs?.SetValue(vertexStream, new Vec3[uv1.Length]);
        }

        var indexBuffer = ObjectCloner.DeepCloneObject(IndexBufferTemplate);
        indexBuffer.Indices = subMesh.Indices.ToArray();
        indexBuffer.Flags = 2;

        var indexedTriangles = ObjectCloner.DeepCloneObject(IndexedTrianglesTemplate);
        indexedTriangles.VertexStreams = [vertexStream];
        indexedTriangles.IndexBuffer = indexBuffer;
        if (TryGetBoundingBox(mesh, out var bb))
            indexedTriangles.BoundingBox = bb;
        else
            indexedTriangles.BoundingBox = BuildBoxAligned(subMesh);

        var countProperty= typeof(CPlugVisualIndexedTriangles).GetProperty("Count",
            BindingFlags.NonPublic | BindingFlags.Instance);
        countProperty?.SetValue(indexedTriangles, vertexStream.Positions.Length);

        return indexedTriangles;
    }


    // ─────────────────────────────────────────────
    // GeometryLayer builder (shared)
    // ─────────────────────────────────────────────
    CPlugCrystal.GeometryLayer BuildLayer(NormalizedSubmesh submesh, CPlugCrystal.Material material)
    {
        var layer = ObjectCloner.DeepCloneObject(LayerTemplate);
        layer.LayerName = "Geometry";

        var crystal = ObjectCloner.DeepCloneObject(CrystalTemplate);
        crystal.Positions = submesh.Positions;

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

                vertices[v] = new CPlugCrystal.Vertex(idx, texCoord, lightmap);
            }

            faces.Add(new CPlugCrystal.Face(vertices, group, material, null));
        }

        crystal.Faces = faces.ToArray();
        layer.Crystal = crystal;
        return layer;
    }

    public void PopulateTriggerSpecial(NPlugTrigger_SSpecial target, NormalizedMesh mesh, int surfaceMeshIndex = 0)
    {
        var surfaceMesh = mesh.Submeshes[Math.Clamp(surfaceMeshIndex, 0, mesh.Submeshes.Length - 1)].AsMesh();
        var surface = BuildSurface(surfaceMesh);
        surface.Surf!.GameplayMainDir = new Vec3(0, 0, 1);
        target.TriggerShape = surface;
    }
    public NPlugTrigger_SSpecial BuildTriggerSpecial(NormalizedMesh mesh, LegacyGameplayId gameplayId, int surfaceMeshIndex = 0)
    {
        NPlugTrigger_SSpecial triggerSpecial = ObjectCloner.DeepCloneObject(TriggerSpecialTemplate);
        if (mesh.SurfaceData != null && mesh.SurfaceData is CPlugSurface sourceSurface)
            triggerSpecial.TriggerShape = ObjectCloner.DeepCloneObject(mesh.SurfaceData);
        else
            PopulateTriggerSpecial(triggerSpecial, mesh, surfaceMeshIndex);

        ItemTriggerEffectConverter.ConvertEffect(gameplayId, triggerSpecial);

        return triggerSpecial;
    }

    // ─────────────────────────────────────────────
    // other helpers
    // ─────────────────────────────────────────────
    CGameItemPlacementParam BuildPlacementParam(NormalizedMesh mesh)
    {
        if(mesh.PlacementParam != null)
            return mesh.PlacementParam;
        var placementParam = ObjectCloner.DeepCloneObject(PlacementParamTemplate);
        placementParam.AutoRotation = false;
        placementParam.CubeCenter = (0, 0, 0);
        placementParam.CubeSize = 0;
        placementParam.FlyVOffset = 0;
        placementParam.FlyVStep = 0;
        placementParam.GridSnapHOffset = 0;
        placementParam.GridSnapHStep = 1;
        placementParam.GridSnapVOffset = 0;
        placementParam.GridSnapVStep = 0;
        placementParam.PivotSnapDistance = -1;
        return placementParam;
    }
    BoxAligned BuildBoxAligned(NormalizedSubmesh subMesh)
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
    bool TryGetBoundingBox(NormalizedMesh mesh, out BoxAligned boundingBox)
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


    void FillItemDataFromMesh(CGameItemModel item, NormalizedMesh mesh)
    {
        item.Name = "New Item";
        ChunkSafeItemOperations.SetIcon(item, mesh.Icon, mesh.IconWebP);
        item.Description = "No Description";
        item.Ident = ident;
        item.DefaultPlacement = BuildPlacementParam(mesh);
        if(item.EntityModel is CPlugPrefab prefab)
            prefab.FileWriteTime = DateTime.Now;
        
    }

    Vec2 QuantizeLightmapCoord(Vec2 coord)
    {
        return new Vec2((ushort)MathF.Round(coord.X * ushort.MaxValue) / (float)ushort.MaxValue, 
            (ushort)MathF.Round(coord.Y * ushort.MaxValue) / (float)ushort.MaxValue);
    }

    public CGameItemModel BuildSolid2ModelItem(NormalizedMesh mesh)
    {
        var item = ObjectCloner.DeepCloneObject(EntityModelTemplate);
        (item.EntityModel as CGameCommonItemEntityModel).StaticObject.Mesh = BuildSolid2Model(mesh);
        FillItemDataFromMesh(item, mesh);
        return item;
    }

    public CGameItemModel BuildCrystalItem(NormalizedMesh mesh)
    {
        var item = ObjectCloner.DeepCloneObject(EntityModelEditionTemplate);
        (item.EntityModelEdition as CGameCommonItemEntityModelEdition).MeshCrystal = BuildCrystal(mesh);
        FillItemDataFromMesh(item, mesh);
        return item;
    }

    public CGameItemModel BuildMovingItem(NormalizedMesh mesh)
    {
        var item = ObjectCloner.DeepCloneObject(MovingItemTemplate);
        ItemExtensions.TryGetDynaModelEntRef(item, out var entRef);
        entRef.Model = BuildDynaObjectModel(mesh);
        FillItemDataFromMesh(item, mesh);
        return item;
    }

    public CGameItemModel BuildTriggerSpecialItem(NormalizedMesh mesh, LegacyGameplayId gameplayId, int surfaceMeshIndex = 0)
    {
        var item = ObjectCloner.DeepCloneObject(TriggerItemTemplate);
        ItemExtensions.TryGetStaticModelEntRef(item, out var entRef);
        entRef.Model = BuildStaticObjectModel(mesh);
        ItemExtensions.TryGetTriggerSpecialEntRef(item, out var triggerSpecialEntRef);
        triggerSpecialEntRef.Model = BuildTriggerSpecial(mesh, gameplayId, surfaceMeshIndex);
        item.Chunks.Get<CGameItemModel.Chunk2E00201F>().U08 = 0;
        FillItemDataFromMesh(item, mesh);
        return item;
    }


}