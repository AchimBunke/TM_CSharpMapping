using GBX.NET;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.MwFoundations;
using GBX.NET.Engines.Plug;
using GBX.NET.Serialization;
using System.ComponentModel;
using System.Numerics;
using System.Reflection;
using TM_GenericMapping.Common;
using TM_GenericMapping.Messaging;
using static GBX.NET.Engines.GameData.CGameItemModel;
using static GBX.NET.Engines.Plug.CPlugCrystal;
using static GBX.NET.Engines.Plug.CPlugMaterialUserInst;
using static GBX.NET.Engines.Plug.CPlugPrefab;
using static GBX.NET.Engines.Plug.CPlugSurface;
using static TM_GenericMapping.Items.MeshBuilder;

namespace TM_GenericMapping.Items;

public class MeshBuilder
{
    public struct MeshBuilderSettings
    {
        public string Author;
    };

 
    public struct BuildOptions
    {
        public BuildOptions()
        {

        }

        public HashSet<int> Geometries = new HashSet<int>();
        public HashSet<int> NonCollidables = new HashSet<int>();
        public HashSet<int> Invisibles = new HashSet<int>();

        public HashSet<int> Triggers = new HashSet<int>();

        public HashSet<int> DynaShapes = new HashSet<int>();
        public HashSet<int> StaticShapes = new HashSet<int>();


        public static BuildOptions DefaultFromMesh(NormalizedMesh mesh)
        {
            var options = new BuildOptions();
            for (int i = 0; i < mesh.Submeshes.Length; ++i)
            {
                var submesh = mesh.Submeshes[i];
                if (submesh.Properties.HasFlag(SubmeshProperties.Disabled))
                    continue;
               
                switch (submesh.Type)
                {
                    case SubmeshType.Mesh:
                        options.Geometries.Add(i);
                        break;
                    case SubmeshType.Trigger_Waypoint:
                    case SubmeshType.Trigger_Special:
                        options.Triggers.Add(i);
                        break;
                    case SubmeshType.Dyna_Shape:
                        options.DynaShapes.Add(i);
                        break;
                    case SubmeshType.Static_Shape:
                        options.StaticShapes.Add(i);
                        break;
                }
                if (submesh.Properties.HasFlag(SubmeshProperties.Invisible))
                    options.Invisibles.Add(i);
                if (submesh.Properties.HasFlag(SubmeshProperties.NonCollidable))
                    options.NonCollidables.Add(i);
              
            }
            
            

            return options;
        }

    }

    const string MovingItemTemplatePath = @"MovingItemTemplate.Item.Gbx";
    const string EntityModelEditionTemplatePath = @"EntityModelEditionTemplate.Item.Gbx";
    const string EntityModelTemplatePath = @"EntityModelTemplate.Item.Gbx";
    const string TriggerItemTemplatePath = @"TriggerItemTemplate.Item.Gbx";
    const string TriggerLayerTemplatePath = @"TriggerLayerTemplate.Item.Gbx";

    CGameItemModel movingItemTemplate;
    CGameItemModel entityModelEditionTemplate;
    CGameItemModel entityModelTemplate;
    CGameItemModel triggerItemTemplate;
    CGameItemModel triggerLayerTemplate;

    CGameItemModel MovingItemTemplate => (movingItemTemplate ??= Gbx.Parse<CGameItemModel>(TemplateLoader.GetTemplate(MovingItemTemplatePath)));
    CGameItemModel EntityModelEditionTemplate => (entityModelEditionTemplate ??= Gbx.Parse<CGameItemModel>(TemplateLoader.GetTemplate(EntityModelEditionTemplatePath)));
    CGameItemModel EntityModelTemplate => (entityModelTemplate ??= Gbx.Parse<CGameItemModel>(TemplateLoader.GetTemplate(EntityModelTemplatePath)));
    CGameItemModel TriggerItemTemplate => (triggerItemTemplate ??= Gbx.Parse<CGameItemModel>(TemplateLoader.GetTemplate(TriggerItemTemplatePath)));
    CGameItemModel TriggerLayerTemplateItem => (triggerLayerTemplate ??= Gbx.Parse<CGameItemModel>(TemplateLoader.GetTemplate(TriggerLayerTemplatePath)));

    CGameCommonItemEntityModel CommonItemEntityModelTemplate => (EntityModelTemplate.EntityModel as CGameCommonItemEntityModel);
    CPlugSolid2Model Solid2ModelTemplate => (CommonItemEntityModelTemplate.StaticObject.Mesh);
    CPlugVisualIndexedTriangles IndexedTrianglesTemplate => (Solid2ModelTemplate.Visuals[0] as CPlugVisualIndexedTriangles);
    CPlugVertexStream VertexStreamTemplate => IndexedTrianglesTemplate.VertexStreams[0];
    CPlugIndexBuffer IndexBufferTemplate => IndexedTrianglesTemplate.IndexBuffer;
    CGameItemPlacementParam PlacementParamTemplate => EntityModelEditionTemplate.DefaultPlacement;

    CGameCommonItemEntityModelEdition CommonItemEntityModelEditionTemplate => (EntityModelEditionTemplate.EntityModelEdition as CGameCommonItemEntityModelEdition);
    CPlugCrystal MeshCrystalTemplate => CommonItemEntityModelEditionTemplate.MeshCrystal;
    CPlugCrystal.GeometryLayer GeometryLayerTemplate => MeshCrystalTemplate.Layers[0] as CPlugCrystal.GeometryLayer;
    CPlugCrystal.Crystal GeometryCrystalTemplate => GeometryLayerTemplate.Crystal;

    CPlugPrefab CPlugPrefabTemplate => (MovingItemTemplate.EntityModel as CPlugPrefab);
    CPlugDynaObjectModel DynaObjectModelTemplate => ItemExtensions.TryGetDynaObjectModel(MovingItemTemplate, out var dyna) ? dyna : null;
    CPlugSurface SurfaceTemplate => DynaObjectModelTemplate.DynaShape;
    CPlugSurface.Mesh SurfaceMeshTemplate => SurfaceTemplate.Surf as CPlugSurface.Mesh;

    CPlugStaticObjectModel StaticObjectModelTemplate => ItemExtensions.TryGetStaticObjectModel(TriggerItemTemplate, out var staticObj) ? staticObj : null;
    NPlugTrigger_SSpecial TriggerSpecialTemplate => ItemExtensions.TryGetTriggerSpecial(TriggerItemTemplate, out var triggerSpecial) ? triggerSpecial : null;

    CPlugCrystal.TriggerLayer TriggerLayerTemplate => (TriggerLayerTemplateItem.EntityModelEdition as CGameCommonItemEntityModelEdition).MeshCrystal.Layers[0] as CPlugCrystal.TriggerLayer;
    CPlugCrystal.Crystal TriggerCrystalTemplate => TriggerLayerTemplate.Crystal;


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
    public ToolResult<CPlugSurface> BuildSurface(NormalizedMesh mesh, BuildOptions buildOptions, ReadOnlySpan<int> geometries, ReadOnlySpan<int> surfaces)
    {
        var surface = ObjectCloner.DeepCloneObject(SurfaceTemplate);


        var surfMesh = ObjectCloner.DeepCloneObject(SurfaceMeshTemplate);

        // merge all submesh positions and indices into one surface
        var allPositions = new List<Vec3>();
        var allTriangles = new List<CPlugSurface.Mesh.Triangle>();

        foreach (var subMeshIndex in surfaces)
        {
            var subMesh = mesh.Submeshes[subMeshIndex];

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
        chunk.U02 = geometries.ToArray()
            .Where(g => !buildOptions.Invisibles.Contains(g))// only visibles
            .Select(idx => (idx, mesh.Submeshes[idx]))
            .Select(d => buildOptions.NonCollidables.Contains(d.idx) ? (ushort)MaterialId.NotCollidable : (ushort)d.Item2.Material.SurfacePhysicId) // non-collidables get different material
            .ToArray();
        return ToolResult.Success(surface, nameof(MeshBuilder));
    }

    public ToolResult<CPlugSurface> ReconstructSurface(NormalizedSubmesh surfaceSubmesh, NormalizedMesh mesh, BuildOptions buildOptions, int[] geometries)
    {
        return BuildSurface(mesh, buildOptions, geometries, [mesh.Submeshes.IndexOf(surfaceSubmesh)]);
    }

    // Same mesh for both — simplest valid approach for dynamic too
    // Replace with convex hull later if physics behavior needs it
    public ToolResult<CPlugSurface> BuildDynaSurface(NormalizedMesh mesh, BuildOptions buildOptions)
    {
        if(buildOptions.DynaShapes.Count == 0)
            return ToolResult.Fail(nameof(MeshBuilder), ErrorCodes.MeshBuilder.MissingDynaShape);
        return BuildSurface(mesh, buildOptions, buildOptions.Geometries.ToArray(), buildOptions.DynaShapes.ToArray());
    }
    public ToolResult<CPlugSurface> BuildStaticSurface(NormalizedMesh mesh, BuildOptions buildOptions)
    {
        if (buildOptions.StaticShapes.Count == 0)
            return ToolResult.Fail(nameof(MeshBuilder), ErrorCodes.MeshBuilder.MissingStaticShape);
        return BuildSurface(mesh, buildOptions, buildOptions.Geometries.ToArray(), buildOptions.StaticShapes.ToArray());
    }


    // ─────────────────────────────────────────────
    // Solid2Model — Option A: mutate existing
    // ─────────────────────────────────────────────

    public ToolResult<None> PopulateSolid2Model(CPlugSolid2Model target, NormalizedMesh mesh, BuildOptions buildOptions)
    {
        List<CPlugVisual> visuals = [];
        List<CPlugSolid2Model.Material> materials = [];
        List<CPlugSolid2Model.ShadedGeom> shadedGeom = [];

        foreach (var subMeshIdx in buildOptions.Geometries)
        {
            var submesh = mesh.Submeshes[subMeshIdx];

            if(buildOptions.Invisibles.Contains(subMeshIdx))
                continue;

            var indexedTriangles = BuildIndexedTrianglesVisual(mesh, submesh);

            int visualIndex = visuals.Count;
            int materialIndex = materials.Count;

            visuals.Add(indexedTriangles);

            var materialInstance = ObjectCloner.DeepCloneObject(submesh.Material);
            if (buildOptions.NonCollidables.Contains(subMeshIdx))
                materialInstance.SurfacePhysicId = MaterialId.NotCollidable;
            materials.Add(new CPlugSolid2Model.Material
            {
                MaterialUserInst = materialInstance,
                MaterialName = ""//submesh.Material.MaterialName
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
        return ToolResult.Success(nameof(MeshBuilder));
    }

    // ─────────────────────────────────────────────
    // Solid2Model — Option B: build from scratch
    // ─────────────────────────────────────────────

    public ToolResult<CPlugSolid2Model> BuildSolid2Model(NormalizedMesh mesh, BuildOptions buildOptions)
    {
        // grab source solid from SourceData if available as chunk donor
        // otherwise construct empty (may be missing required chunks)
        CPlugSolid2Model solid;
        if (mesh.SourceData is CPlugSolid2Model source)
            solid = ObjectCloner.DeepCloneObject(source);
        else
            solid = ObjectCloner.DeepCloneObject(Solid2ModelTemplate);

        var result = PopulateSolid2Model(solid, mesh, buildOptions);
        if (result.IsFailure)
            return ToolResult.Fail(result);


        return ToolResult.Success(solid, nameof(MeshBuilder));
    }

    // ─────────────────────────────────────────────
    // DynaObjectModel
    // ─────────────────────────────────────────────

    // Option A: mutate existing DynaObjectModel (preferred — avoids chunk issues)
    public ToolResult<None> PopulateDynaObjectModel(CPlugDynaObjectModel target, NormalizedMesh mesh, BuildOptions buildOptions)
    {
        var meshResult = BuildSolid2Model(mesh, buildOptions);
        if(meshResult.IsFailure)
            return ToolResult.Fail(meshResult);
        target.Mesh = meshResult.Value;

        var dynaShapeResult = BuildDynaSurface(mesh, buildOptions);
        if (dynaShapeResult.IsFailure)
            return ToolResult.Fail(dynaShapeResult);
        target.DynaShape = dynaShapeResult.Value;

        var staticShapeResult = BuildStaticSurface(mesh, buildOptions);
        if (staticShapeResult.IsFailure)
            return ToolResult.Fail(staticShapeResult);
        target.StaticShape = staticShapeResult.Value;
        return ToolResult.Success(nameof(MeshBuilder));
    }

    // Option B: build new DynaObjectModel using target as chunk donor
    public ToolResult<CPlugDynaObjectModel> BuildDynaObjectModel(NormalizedMesh mesh, BuildOptions buildOptions)
    {
        CPlugDynaObjectModel dyna;
        if (mesh.SourceData is CPlugDynaObjectModel source)
            dyna = ObjectCloner.DeepCloneObject(source);
        else
            dyna = ObjectCloner.DeepCloneObject(DynaObjectModelTemplate);
        var result = PopulateDynaObjectModel(dyna, mesh, buildOptions);
        if (result.IsFailure)
            return ToolResult.Fail(result);
        return ToolResult.Success(dyna, nameof(MeshBuilder));
    }


    // StaticObjectModel
    // ─────────────────────────────────────────────

    public ToolResult<None> PopulateStaticObjectModel(CPlugStaticObjectModel target, NormalizedMesh mesh, BuildOptions buildOptions)
    {
        var meshResult = BuildSolid2Model(mesh, buildOptions);
        if (meshResult.IsFailure)
            return ToolResult.Fail(meshResult);
        target.Mesh = meshResult.Value;

        var staticShapeResult = BuildStaticSurface(mesh, buildOptions);
        if (staticShapeResult.IsFailure)
            return ToolResult.Fail(staticShapeResult);
        target.Shape = staticShapeResult.Value;
        return ToolResult.Success(nameof(MeshBuilder));
    }

    public ToolResult<CPlugStaticObjectModel> BuildStaticObjectModel(NormalizedMesh mesh, BuildOptions buildOptions)
    {
        CPlugStaticObjectModel staticObj;
        if (mesh.SourceData is CPlugStaticObjectModel source)
            staticObj = ObjectCloner.DeepCloneObject(source);
        else
            staticObj = ObjectCloner.DeepCloneObject(StaticObjectModelTemplate);
        var result = PopulateStaticObjectModel(staticObj, mesh, buildOptions);
        if (result.IsFailure)
            return ToolResult.Fail(result);
        return ToolResult.Success(staticObj, nameof(MeshBuilder));
    }

    // ─────────────────────────────────────────────
    // CommonItemEntityModelEdition (Crystal)
    // ─────────────────────────────────────────────
    public ToolResult<CPlugCrystal> BuildCrystal(NormalizedMesh mesh, BuildOptions buildOptions)
    {
        CPlugCrystal crystal;
        if (mesh.SourceData is CPlugCrystal source)
            crystal = ObjectCloner.DeepCloneObject(source);
        else
            crystal = ObjectCloner.DeepCloneObject(MeshCrystalTemplate);

        var result = PopulateMeshCrystal(crystal, mesh, buildOptions);
        if (result.IsFailure)
            return ToolResult.Fail(result);
        return ToolResult.Success(crystal, nameof(MeshBuilder));
    }

    public ToolResult<None> PopulateMeshCrystal(CPlugCrystal target, NormalizedMesh mesh, BuildOptions buildOptions)
    {
        List<CPlugCrystal.Layer> layers = [];
        List<CPlugCrystal.Material> materials = [];
        Dictionary<CPlugMaterialUserInst, CPlugMaterialUserInst> materialMap = [];

        int layerIdx = 0;
        foreach (var submeshIdx in buildOptions.Geometries)
        {
            var submesh = mesh.Submeshes[submeshIdx];
            if(!materialMap.TryGetValue(submesh.Material, out var materialInstance))
                materialMap[submesh.Material] = materialInstance = ObjectCloner.DeepCloneObject(submesh.Material);
            var material = new CPlugCrystal.Material
            {
                MaterialUserInst = materialInstance,
                MaterialName = string.Empty,
            };
            materials.Add(material);
            var layer = BuildGeometryLayer(submesh, material);
            layer.Crystal.U02 = layerIdx;
            layer.LayerId = $"Layer{layerIdx}";
            layer.IsVisible = !buildOptions.Invisibles.Contains(submeshIdx);
            layer.Collidable = !buildOptions.NonCollidables.Contains(submeshIdx);
            layers.Add(layer);
            layerIdx++;
        }
        foreach (var submeshIdx in buildOptions.Triggers)
        {
            var submesh = mesh.Submeshes[submeshIdx];
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
        target.Layers = layers;
        target.Materials = materials;
        var chunk = target.Chunks.Get<CPlugCrystal.Chunk09003007>();
        chunk.U01 = Enumerable.Repeat(2, layers.OfType<CPlugCrystal.GeometryLayer>().Sum(l => l.Crystal.Faces.Length)).ToArray();
        return ToolResult.Success(nameof(MeshBuilder));
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
        //if(subMesh.TangentUs == null)
        //    dataDecls.RemoveAll(decl => decl.WeightCount == CPlugVertexStream.EPlugVDcl.TangentU);
        //if (subMesh.TangentVs == null)
        //    dataDecls.RemoveAll(decl => decl.WeightCount == CPlugVertexStream.EPlugVDcl.TangentV);

        if (colors.Count > 0)
        {
            dataDecls.FirstOrDefault(d=>d.WeightCount == CPlugVertexStream.EPlugVDcl.Position).Flags1 = 9438208;

            dataDecls.FirstOrDefault(d => d.WeightCount == CPlugVertexStream.EPlugVDcl.Normal).Flags1 = 277879813;

            dataDecls.FirstOrDefault(d => d.WeightCount == CPlugVertexStream.EPlugVDcl.Color0).Flags1 = 546310152;

            var tex0Decl = dataDecls.FirstOrDefault(d => d.WeightCount == CPlugVertexStream.EPlugVDcl.TexCoord0);
            tex0Decl.Flags1 = 546308618;
            tex0Decl.Flags2 = 80;
            tex0Decl.Offset = 20;

            var tangentUDecl = dataDecls.FirstOrDefault(d => d.WeightCount == CPlugVertexStream.EPlugVDcl.TangentU);
            if(tangentUDecl != null)
            {
                tangentUDecl.Flags1 = 277879826;
                tangentUDecl.Flags2 = 112;
                tangentUDecl.Offset = 28;
            }

            var tangentVDecl = dataDecls.FirstOrDefault(d => d.WeightCount == CPlugVertexStream.EPlugVDcl.TangentV);
            if(tangentVDecl != null)
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
        if (subMesh.TangentUs != null)
        {
            tangentUs.SetValue(vertexStream, subMesh.TangentUs);
        }
        else
        {
            tangentUs.SetValue(vertexStream, new Vec3[vertexStream.Positions.Length]);
        }
        var tangentVs = typeof(CPlugVertexStream).GetField("tangentVs",
             BindingFlags.NonPublic | BindingFlags.Instance)!;
        if (subMesh.TangentVs != null)
        {
            tangentVs.SetValue(vertexStream, subMesh.TangentVs);
        }
        else
        {
            tangentVs.SetValue(vertexStream, new Vec3[vertexStream.Positions.Length]);
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
    CPlugCrystal.GeometryLayer BuildGeometryLayer(NormalizedSubmesh submesh, CPlugCrystal.Material material)
    {
        var layer = ObjectCloner.DeepCloneObject(GeometryLayerTemplate);
        layer.LayerName = $"Geometry {submesh.Name}";

        var crystal = ObjectCloner.DeepCloneObject(GeometryCrystalTemplate);
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
    CPlugCrystal.TriggerLayer BuildTriggerLayer(NormalizedSubmesh submesh, CPlugCrystal.Material material)
    {
        var layer = ObjectCloner.DeepCloneObject(TriggerLayerTemplate);
        layer.LayerName = $"Trigger {submesh.Name}";

        var crystal = ObjectCloner.DeepCloneObject(TriggerCrystalTemplate);
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

    public ToolResult<None> PopulateTriggerSpecial(NPlugTrigger_SSpecial target, NormalizedMesh mesh, BuildOptions buildOptions)
    {
        if (buildOptions.Triggers.Count == 0)
            return ToolResult.Fail(nameof(MeshBuilder), ErrorCodes.MeshBuilder.MissingTrigger);

        var surfaceResult = BuildSurface(mesh, buildOptions, buildOptions.Geometries.ToArray(), buildOptions.Triggers.ToArray());
        if (surfaceResult.IsFailure)
            return ToolResult.Fail(surfaceResult);
        CPlugSurface surface = surfaceResult.Value;
        surface.Surf!.GameplayMainDir = new Vec3(0, 0, 1);
        target.TriggerShape = surface;
        return ToolResult.Success(nameof(MeshBuilder));
    }
    public ToolResult<NPlugTrigger_SSpecial> BuildTriggerSpecial(NormalizedMesh mesh, LegacyGameplayId gameplayId, BuildOptions buildOption)
    {
        NPlugTrigger_SSpecial triggerSpecial = ObjectCloner.DeepCloneObject(TriggerSpecialTemplate);
        
        var result = PopulateTriggerSpecial(triggerSpecial, mesh, buildOption);
        if (result.IsFailure)
            return ToolResult.Fail(result);

        ItemTriggerEffectConverter.ConvertEffect(gameplayId, triggerSpecial);

        return ToolResult.Success(triggerSpecial, nameof(MeshBuilder));
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

    int[] FindSolidCollidableGeometries(NormalizedMesh mesh)
    {
        List<int> geometries = [];
        for (int i = 0; i < mesh.Submeshes.Length; ++i)
            if (mesh.Submeshes[i].Type == SubmeshType.Mesh)
                geometries.Add(i);

        return geometries.ToArray();
    }
    int[] FindSubmeshes(NormalizedMesh mesh, params ReadOnlySpan<SubmeshType> types)
    {
        List<int> geometries = [];
        for (int i = 0; i < mesh.Submeshes.Length; ++i)
            if (types.Contains(mesh.Submeshes[i].Type))
                geometries.Add(i);

        return geometries.ToArray();
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

    public ToolResult<CGameItemModel> BuildSolid2ModelItem(NormalizedMesh mesh, BuildOptions buildOptions)
    {
        var item = ObjectCloner.DeepCloneObject(EntityModelTemplate);
        var meshResult = BuildSolid2Model(mesh, buildOptions);
        if (meshResult.IsFailure)
            return ToolResult.Fail(meshResult);
        (item.EntityModel as CGameCommonItemEntityModel).StaticObject.Mesh = meshResult.Value;
        FillItemDataFromMesh(item, mesh);
        return ToolResult.Success(item, nameof(MeshBuilder));
    }

    public ToolResult<CGameItemModel> BuildCrystalItem(NormalizedMesh mesh, BuildOptions buildOptions)
    {
        var item = ObjectCloner.DeepCloneObject(EntityModelEditionTemplate);
        var crystalResult = BuildCrystal(mesh, buildOptions);
        if (crystalResult.IsFailure)
            return ToolResult.Fail(crystalResult);
        (item.EntityModelEdition as CGameCommonItemEntityModelEdition).MeshCrystal = crystalResult.Value;
        FillItemDataFromMesh(item, mesh);
        return ToolResult.Success(item, nameof(MeshBuilder));
    }
    public ToolResult<CGameItemModel> BuildCrystalWaypointItem(NormalizedMesh mesh, EWaypointType waypointType, BuildOptions buildOptions)
    {
        var item = BuildCrystalItem(mesh, buildOptions);
        if (item.IsFailure)
            return ToolResult.Fail(item);
        item.Value.WaypointType = waypointType;
        return item;
    }

    public ToolResult<CGameItemModel> BuildMovingItem(NormalizedMesh mesh, BuildOptions buildOptions)
    {
        var item = ObjectCloner.DeepCloneObject(MovingItemTemplate);
        ItemExtensions.TryGetDynaModelEntRef(item, out var entRef);
        var dynaResult = BuildDynaObjectModel(mesh, buildOptions);
        if (dynaResult.IsFailure)
            return ToolResult.Fail(dynaResult);
        entRef.Model = dynaResult.Value;
        FillItemDataFromMesh(item, mesh);
        return ToolResult.Success(item, nameof(MeshBuilder));
    }

    public ToolResult<CGameItemModel> BuildTriggerSpecialItem(NormalizedMesh mesh, LegacyGameplayId gameplayId, BuildOptions buildOptions)
    {
        var item = ObjectCloner.DeepCloneObject(TriggerItemTemplate);
        ItemExtensions.TryGetStaticModelEntRef(item, out var entRef);
        var staticResult = BuildStaticObjectModel(mesh, buildOptions);
        if (staticResult.IsFailure)
            return ToolResult.Fail(staticResult);
        entRef.Model = staticResult.Value;
        ItemExtensions.TryGetTriggerSpecialEntRef(item, out var triggerSpecialEntRef);
        var triggerResult = BuildTriggerSpecial(mesh, gameplayId, buildOptions);
        if (triggerResult.IsFailure)
            return ToolResult.Fail(triggerResult);

        triggerSpecialEntRef.Model = triggerResult.Value;
        item.Chunks.Get<CGameItemModel.Chunk2E00201F>().U08 = 0;
        FillItemDataFromMesh(item, mesh);
        return ToolResult.Success(item, nameof(MeshBuilder));
    }

    public ToolResult<CGameItemModel> Test(NormalizedMesh mesh)
    {
        var source = ObjectCloner.DeepCloneObject(MovingItemTemplate);
        ItemExtensions.TryGetDynaModelEntRef(source, out var dynaEnt);
        var dynaModel = dynaEnt.Model as CPlugDynaObjectModel;
        dynaModel.Mesh = BuildSolid2Model(mesh, new BuildOptions { Geometries = [0,1] }).Value;
        dynaModel.DynaShape = BuildDynaSurface(mesh, new BuildOptions { DynaShapes = [1], Geometries = [0,1] }).Value;
        dynaModel.StaticShape = BuildStaticSurface(mesh, new BuildOptions { StaticShapes = [2], Geometries = [0,1] }).Value;



        return ToolResult.Success(source, nameof(MeshBuilder));
    }

}