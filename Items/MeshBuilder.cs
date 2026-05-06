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

    CGameItemModel movingItemTemplate;
    CGameItemModel entityModelEditionTemplate;
    CGameItemModel entityModelTemplate;

    CGameCommonItemEntityModel CommonItemEntityModelTemplate => (entityModelTemplate.EntityModel as CGameCommonItemEntityModel);
    CPlugSolid2Model Solid2ModelTemplate => (CommonItemEntityModelTemplate.StaticObject.Mesh);
    CPlugVisualIndexedTriangles IndexedTrianglesTemplate => (Solid2ModelTemplate.Visuals[0] as CPlugVisualIndexedTriangles);
    CPlugVertexStream VertexStreamTemplate => IndexedTrianglesTemplate.VertexStreams[0];
    CPlugIndexBuffer IndexBufferTemplate => IndexedTrianglesTemplate.IndexBuffer;
    CGameItemPlacementParam PlacementParamTemplate => entityModelEditionTemplate.DefaultPlacement;

    CGameCommonItemEntityModelEdition CommonItemEntityModelEditionTemplate => (entityModelEditionTemplate.EntityModelEdition as CGameCommonItemEntityModelEdition);
    CPlugCrystal MeshCrystalTemplate => CommonItemEntityModelEditionTemplate.MeshCrystal;
    CPlugCrystal.GeometryLayer LayerTemplate => MeshCrystalTemplate.Layers[0] as CPlugCrystal.GeometryLayer;
    CPlugCrystal.Crystal CrystalTemplate => LayerTemplate.Crystal;

    CPlugPrefab CPlugPrefabTemplate => (movingItemTemplate.EntityModel as CPlugPrefab);
    CPlugDynaObjectModel DynaObjectModelTemplate => ItemExtensions.TryGetDynaObjectModel(movingItemTemplate, out var dyna) ? dyna : null;
    CPlugSurface SurfaceTemplate => DynaObjectModelTemplate.DynaShape;
    CPlugSurface.Mesh SurfaceMeshTemplate => SurfaceTemplate.Surf as CPlugSurface.Mesh;

    Ident ident;

    MeshBuilderSettings _settings;
    public MeshBuilder() : this(new()) { }
    public MeshBuilder(MeshBuilderSettings settings)
    {
        movingItemTemplate = Gbx.Parse<CGameItemModel>(TemplateLoader.GetTemplate(MovingItemTemplatePath));
        entityModelEditionTemplate = Gbx.Parse<CGameItemModel>(TemplateLoader.GetTemplate(EntityModelEditionTemplatePath));
        entityModelTemplate = Gbx.Parse<CGameItemModel>(TemplateLoader.GetTemplate(EntityModelTemplatePath));
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
        var surface = ItemExtensions.DeepCloneObject(SurfaceTemplate);


        var surfMesh = ItemExtensions.DeepCloneObject(SurfaceMeshTemplate);
        surfMesh.Vertices = mesh.Positions;
        surfMesh.Triangles = BuildSurfaceTriangles(mesh);

        surface.Surf = surfMesh; 
        var chunk = surface.GetChunk<Chunk0900C003>();
        chunk.U02 = mesh.Submeshes.Select(submesh => (ushort)submesh.Material.SurfacePhysicId).ToArray();
        return surface;
    }

    // Same mesh for both — simplest valid approach for dynamic too
    // Replace with convex hull later if physics behavior needs it
    public CPlugSurface BuildDynaSurface(NormalizedMesh mesh)
        => BuildSurface(mesh);

    CPlugSurface.Mesh.Triangle[] BuildSurfaceTriangles(NormalizedMesh mesh)
    {
        var tris = new List<CPlugSurface.Mesh.Triangle>(mesh.Indices.Length / 3);

        foreach(var subMesh in mesh.Submeshes)
        {
            for (int i = subMesh.IndexStart; i < subMesh.IndexCount; i+=3)
            {
                tris.Add(new CPlugSurface.Mesh.Triangle
                {
                    Indices = new Int3(mesh.Indices[i], mesh.Indices[i + 1], mesh.Indices[i + 2]),
                    SurfaceIndex = 0,
                    U02 = (byte)subMesh.Material.SurfacePhysicId,
                    U03 = 0
                });
            }
        }

        return tris.ToArray();
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
                MaterialUserInst = submesh.Material
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
            solid = ItemExtensions.DeepCloneObject(source);
        else
            solid = ItemExtensions.DeepCloneObject(Solid2ModelTemplate);
        
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
        target.StaticShape = BuildSurface(mesh);
    }

    // Option B: build new DynaObjectModel using target as chunk donor
    public CPlugDynaObjectModel BuildDynaObjectModel(NormalizedMesh mesh)
    {
        CPlugDynaObjectModel dyna;
        if (mesh.SourceData is CPlugDynaObjectModel source)
            dyna = ItemExtensions.DeepCloneObject(source);
        else
            dyna = ItemExtensions.DeepCloneObject(DynaObjectModelTemplate);
        PopulateDynaObjectModel(dyna, mesh);
        return dyna;
    }

    // ─────────────────────────────────────────────
    // CommonItemEntityModelEdition (Crystal)
    // ─────────────────────────────────────────────
    public CPlugCrystal BuildCrystal(NormalizedMesh mesh)
    {
        CPlugCrystal crystal;
        if (mesh.SourceData is CPlugCrystal source)
            crystal = ItemExtensions.DeepCloneObject(source);
        else
            crystal = ItemExtensions.DeepCloneObject(MeshCrystalTemplate);

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
            var layer = BuildLayer(mesh, submesh, material);
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

    CPlugVisualIndexedTriangles BuildIndexedTrianglesVisual(NormalizedMesh mesh, NormalizedSubmesh submesh)
    {
        // extract the vertex range used by this submesh's indices
        // remap global indices to local (0-based) for this submesh
        var globalIndices = mesh.Indices
            .Skip(submesh.IndexStart)
            .Take(submesh.IndexCount)
            .ToArray();

        // collect unique vertex indices used by this submesh
        var uniqueIndices = globalIndices.Distinct().Order().ToArray();
        var remapTable = uniqueIndices
            .Select((globalIdx, localIdx) => (globalIdx, localIdx))
            .ToDictionary(x => x.globalIdx, x => x.localIdx);

        var uVs = new SortedDictionary<int, Vec2[]>();

        if (mesh.TexCoords.Length == mesh.Positions.Length)
            uVs[0] = uniqueIndices.Select(i => mesh.TexCoords[i]).ToArray();   
        if(mesh.LightmapCoords.Length == mesh.Positions.Length)
            uVs[1] = uniqueIndices.Select(i => mesh.LightmapCoords[i]).ToArray();

        var vertexStream = ItemExtensions.DeepCloneObject(VertexStreamTemplate);
        vertexStream.Positions = uniqueIndices.Select(i => mesh.Positions[i]).ToArray();
        vertexStream.Normals = uniqueIndices.Select(i => mesh.Normals[i]).ToArray();
        vertexStream.UVs = uVs;

        // fix data decl
        var dataDeclField = typeof(CPlugVertexStream).GetField("dataDecls",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var dataDecls = (dataDeclField.GetValue(vertexStream) as CPlugVertexStream.DataDecl[]).ToList();
        if (uVs.Count <= 0)
            dataDecls.RemoveAll(decl => decl.WeightCount == CPlugVertexStream.EPlugVDcl.TexCoord0);
        if (uVs.Count <= 1)
            dataDecls.RemoveAll(decl => decl.WeightCount == CPlugVertexStream.EPlugVDcl.TexCoord1);

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

        var indexBuffer = ItemExtensions.DeepCloneObject(IndexBufferTemplate);
        indexBuffer.Indices = globalIndices.Select(i => remapTable[i]).ToArray();
        indexBuffer.Flags = 2;

        var indexedTriangles = ItemExtensions.DeepCloneObject(IndexedTrianglesTemplate);
        indexedTriangles.VertexStreams = [vertexStream];
        indexedTriangles.IndexBuffer = indexBuffer;
        if (TryGetBoundingBox(mesh, out var bb))
            indexedTriangles.BoundingBox = bb;
        else
            indexedTriangles.BoundingBox = BuildBoxAligned(mesh);

        var countProperty= typeof(CPlugVisualIndexedTriangles).GetProperty("Count",
            BindingFlags.NonPublic | BindingFlags.Instance);
        countProperty?.SetValue(indexedTriangles, vertexStream.Positions.Length);

        return indexedTriangles;
    }

    // ─────────────────────────────────────────────
    // GeometryLayer builder (shared)
    // ─────────────────────────────────────────────
    CPlugCrystal.GeometryLayer BuildLayer(NormalizedMesh mesh, NormalizedSubmesh submesh, CPlugCrystal.Material material)
    {
        var layer = ItemExtensions.DeepCloneObject(LayerTemplate);
        layer.LayerName = "Geometry";

        var crystal = ItemExtensions.DeepCloneObject(CrystalTemplate);
        crystal.Positions = mesh.Positions;
        var group = crystal.Groups[0];
        group.Name = "part";
        crystal.Groups = [group];
        List<CPlugCrystal.Face> faces = [];

        for (int i = submesh.IndexStart; i < submesh.IndexStart + submesh.IndexCount; i += 3)
        {
            CPlugCrystal.Vertex[] vertices = new CPlugCrystal.Vertex[3];
            for (int v = 0; v < 3; ++v)
            {
                var idx = mesh.Indices[i + v];
                var lightmapCoord = mesh.LightmapCoords.Length == mesh.Positions.Length ? mesh.LightmapCoords[idx] : Vec2.Zero;
                var texCoord = mesh.TexCoords.Length == mesh.Positions.Length ? mesh.TexCoords[idx] : Vec2.Zero;
                var vertex = new CPlugCrystal.Vertex(idx, texCoord, lightmapCoord);
                vertices[v] = vertex;
            }
            var face = new CPlugCrystal.Face(vertices, group, material, null);
            faces.Add(face);
        }

        crystal.Faces = faces.ToArray();
        layer.Crystal = crystal;
        return layer;
    }

    // ─────────────────────────────────────────────
    // other helpers
    // ─────────────────────────────────────────────
    CGameItemPlacementParam BuildPlacementParam(NormalizedMesh mesh)
    {
        if(mesh.PlacementParam != null)
            return mesh.PlacementParam;
        var placementParam = ItemExtensions.DeepCloneObject(PlacementParamTemplate);
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
    BoxAligned BuildBoxAligned(NormalizedMesh mesh)
    {
        Vec3 min = mesh.Positions[0];
        Vec3 max = mesh.Positions[0];

        foreach (var p in mesh.Positions)
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

    void CopyAllPublicProperties(object source, object target)
    {
        var properties = source.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public);
        foreach (var property in properties)
        {
            if (property.CanRead && property.CanWrite)
            {
                var value = property.GetValue(source);
                property.SetValue(target, value);
            }
        }
    }



    public CGameItemModel BuildSolid2ModelItem(NormalizedMesh mesh)
    {
        var item = ItemExtensions.DeepCloneObject(entityModelTemplate);
        item.Name = "New Item";
        item.IconWebP = mesh.IconWebP;
        item.Description = "No Description";
        item.Ident = ident;
        (item.EntityModel as CGameCommonItemEntityModel).StaticObject.Mesh = BuildSolid2Model(mesh);
        item.DefaultPlacement = BuildPlacementParam(mesh);
        return item;
    }

    public CGameItemModel BuildCrystalItem(NormalizedMesh mesh)
    {
        var item = ItemExtensions.DeepCloneObject(entityModelEditionTemplate);
        item.Name = "New Item";
        item.IconWebP = mesh.IconWebP;
        item.Description = "No Description";
        item.Ident = ident;
        (item.EntityModelEdition as CGameCommonItemEntityModelEdition).MeshCrystal = BuildCrystal(mesh);
        item.DefaultPlacement = BuildPlacementParam(mesh);
        return item;
    }

    public CGameItemModel BuildMovingItem(NormalizedMesh mesh)
    {
        var item = ItemExtensions.DeepCloneObject(movingItemTemplate);
        item.Name = "New Item";
        item.IconWebP = mesh.IconWebP;
        item.Description = "No Description";
        item.Ident = ident;
        ItemExtensions.TryGetDynaModelEntRef(item, out var entRef);
        entRef.Model = BuildDynaObjectModel(mesh);
        item.DefaultPlacement = BuildPlacementParam(mesh);
        return item;
    }




}