using GBX.NET;
using GBX.NET.Engines.Game;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Plug;
using System.Numerics;
using TM_GenericMapping.Materials;
using TM_GenericMapping.Common;
using static GBX.NET.Engines.Plug.CPlugCrystal;
using static GBX.NET.Engines.Plug.CPlugMaterialUserInst;
using Color = System.Drawing.Color;

namespace TM_GenericMapping.Items
{
    /// <summary>
    /// Converts items to TriangleObjects.
    /// Important: Cannot simply convert blocks. Use Openplanet plugin "BlockToItemExports" to create item variants first.
    /// </summary>
    public static class ItemToTriangleObjectConverter
    {
        class TriangleData
        {
            public List<Vector3> vertices = new();
            public List<Int3> triangles = new();
            public List<Color> colors = new();
        }

        /// <summary>
        /// Defines grouping of vertex data into a single TriangleObject during conversion.
        /// </summary>
        [Flags]
        public enum TriangleGrouping
        {
            None = 0,
            Layers = 1,
            Materials = 2,
        }
        public struct ItemToTriangleObjectConverterSettings
        {
            public bool UniformColor;
            public Color Color;
            public bool IncludeInvisibleLayers;
            public bool IncludeVisibleRoot;

            public TriangleGrouping Grouping;

            public static ItemToTriangleObjectConverterSettings Default
                => new ItemToTriangleObjectConverterSettings()
                {
                    UniformColor = false,
                    Color = Color.Black,
                    Grouping = TriangleGrouping.Layers,
                    IncludeInvisibleLayers = false,
                    IncludeVisibleRoot = false,
                };
        }

        public static string ItemDirectoryPath { get; set; } = WindowsUtils.ItemDirectoryPath;
        public static string BlockToItemExportDirectory { get; set; } = Path.Combine(WindowsUtils.MyDocumentsPath, @"Trackmania\Items\BlockToItemExports\Nadeo");
        public static bool TryConvert(CGameItemModel itemModel, MaterialLibrary materialLibrary, ItemToTriangleObjectConverterSettings settings, out TriangleObject triangleObj)
        {
            try
            {
                var ident = itemModel.Ident;
                Logger.Debug($"Converting Item: {ident.Id}");
                triangleObj = new TriangleObject() { Name = ident.Id };
                var entityModel = itemModel.EntityModel;

                if (entityModel is CPlugPrefab cPlugPrefabEntityModel)
                {
                    var dynaModel = cPlugPrefabEntityModel.Ents[0].Model as CPlugDynaObjectModel;

                    var mesh = dynaModel.Mesh;
                    ReadMesh(mesh, triangleObj, materialLibrary, settings);


                }
                else if (entityModel is CGameCommonItemEntityModel commonItemEntityModel)
                {
                    if (commonItemEntityModel.StaticObject != null && commonItemEntityModel.StaticObject.Mesh != null)
                    {
                        ReadMesh(commonItemEntityModel.StaticObject.Mesh, triangleObj, materialLibrary, settings);
                    }
                    else
                    {
                        if (!Directory.Exists(BlockToItemExportDirectory))
                            return false;

                        var extraItemModel = FindExtraItemModel(new DirectoryInfo(BlockToItemExportDirectory), Path.GetFileName(ident.Id)).Node;
                        if (extraItemModel.EntityModelEdition == null ||
                            extraItemModel.EntityModelEdition is not CGameCommonItemEntityModelEdition commonItemEntityModelEdition)
                            return false;
                        var meshCrystal = commonItemEntityModelEdition.MeshCrystal;
                        ReadMeshCrystal(meshCrystal, triangleObj, materialLibrary, settings);
                    }
                }
                else if (entityModel == null &&
                    itemModel.EntityModelEdition != null &&
                    itemModel.EntityModelEdition is CGameCommonItemEntityModelEdition commonItemEntityModelEdition)
                {
                    var meshCrystal = commonItemEntityModelEdition.MeshCrystal;
                    ReadMeshCrystal(meshCrystal, triangleObj, materialLibrary, settings);
                }
                else
                {
                    return false;
                }
                foreach (var item in ShapeUtils.GetFlattenedHierarchyObjects(triangleObj).OfType<TriangleObject>())
                {
                    item.CanShareBlock = true;
                }

                return true;

            }
            catch (Exception e)
            {
                triangleObj = null!;
                Logger.Error($"Failed to convert {itemModel.Ident.Id}: {e.Message}");
            }
            return false;
        }
        public static bool TryConvert(CGameCtnAnchoredObject anchoredItem, MaterialLibrary materialLibrary, ItemToTriangleObjectConverterSettings settings, out TriangleObject triangleObj)
        {
            try
            {
                var ident = anchoredItem.ItemModel;
                Logger.Debug($"Loading CGameItemModel: {ident.Id}");
                var itemModel = Gbx.Parse<CGameItemModel>(System.IO.Path.Combine(ItemDirectoryPath, ident.Id)).Node;
                return TryConvert(itemModel, materialLibrary, settings, out triangleObj);
            }
            catch (Exception)
            {
                triangleObj = null;
                return false;
            }
        }
        static Gbx<CGameItemModel> FindExtraItemModel(DirectoryInfo searchDirectory, string itemName)
        {
            var fileInfos = searchDirectory.GetFiles(itemName, SearchOption.AllDirectories);
            return fileInfos.Length > 0 ? Gbx.Parse<CGameItemModel>(fileInfos[0].FullName) : null;
        }
        static void ReadMeshCrystal(CPlugCrystal crystal, TriangleObject obj, MaterialLibrary materialLibrary, ItemToTriangleObjectConverterSettings settings)
        {
            foreach (var layer in crystal.Layers)
            {
                if (layer is GeometryLayer geometryLayer)
                {
                    if ((!geometryLayer.IsVisible && !settings.IncludeInvisibleLayers) || !layer.IsEnabled)
                    {
                        Logger.Trace($"Skipping crystal layer: {layer.LayerName} because its not enabled or visible");
                        continue;
                    }
                    if(layer.LayerName == TrianglesUtils.VisibleRootLayerName && !settings.IncludeVisibleRoot)
                    {
                        Logger.Trace($"Skipping crystal layer: {layer.LayerName}");
                        continue;
                    }
                    ReadLayer(geometryLayer, obj, materialLibrary, settings);
                }
            }
        }
        static void ReadLayer(GeometryLayer layer, TriangleObject obj, MaterialLibrary materialLibrary, ItemToTriangleObjectConverterSettings settings)
        {
            Logger.Trace($"Converting crystal layer: {layer.LayerName}");
            var crystal = layer.Crystal;

            if (settings.Grouping.HasFlag(TriangleGrouping.Materials))
            {
                List<Vector3> vertices = new();
                List<Int3> triangles = new();


                for (int i = 0; i < crystal.Positions.Length; ++i)
                {
                    var v = crystal.Positions[i];
                    vertices.Add(new Vector3(v.X, v.Y, v.Z));
                }
                Color[] colors = new Color[vertices.Count];

                for (int i = 0; i < crystal.Faces.Length; ++i)
                {
                    var face = crystal.Faces[i];
                    var materialLink = face.Material?.MaterialUserInst?.Link ?? string.Empty;
                    float textureSizeInMeters = face?.Material?.MaterialUserInst?.TextureSizeInMeters ?? 1;
                    var tilingU = face?.Material?.MaterialUserInst?.TilingU ?? CPlugMaterialUserInst.ETexAddress.Wrap;
                    var tilingV = face?.Material?.MaterialUserInst?.TilingV ?? CPlugMaterialUserInst.ETexAddress.Wrap;
                    var v0 = face.Vertices[0];
                    var v1 = face.Vertices[1];
                    var v2 = face.Vertices[2];
                    Materials.Material material = null!;
                    if (settings.UniformColor)
                    {
                        colors[v0.Index] = settings.Color;
                        colors[v1.Index] = settings.Color;
                        colors[v2.Index] = settings.Color;
                    }
                    else if (materialLibrary.TryGetMaterial(TextureSampling.MaterialLinkToMaterialName(materialLink), out material))
                    {
                        colors[v0.Index] = TextureSampling.SampleColor(material, v0.TexCoord.X, v0.TexCoord.Y, textureSizeInMeters, tilingU, tilingV);
                        colors[v1.Index] = TextureSampling.SampleColor(material, v1.TexCoord.X, v1.TexCoord.Y, textureSizeInMeters, tilingU, tilingV);
                        colors[v2.Index] = TextureSampling.SampleColor(material, v2.TexCoord.X, v2.TexCoord.Y, textureSizeInMeters, tilingU, tilingV);
                    }
                    triangles.Add(new Int3(v0.Index, v1.Index, v2.Index));
                    if (face.Vertices.Length == 4)
                    {
                        var v3 = face.Vertices[3];
                        if (settings.UniformColor)
                            colors[v3.Index] = settings.Color;
                        else if (material != null)
                            colors[v3.Index] = TextureSampling.SampleColor(material, v3.TexCoord.X, v3.TexCoord.Y, textureSizeInMeters, tilingU, tilingV);
                        triangles.Add(new Int3(v2.Index, v3.Index, v0.Index));
                    }
                }
                var triangleObject = new TriangleObject(
                   points: vertices.ToArray(),
                   triangles: triangles.ToArray(),
                   colors: colors.ToArray(),
                   uniqueVertices: false)
                {
                    Name = $"-{layer.LayerName}",
                    CanShareBlock = settings.Grouping.HasFlag(TriangleGrouping.Layers),
                };
                obj.AddSubObjects(triangleObject);
            }
            else
            {
                Dictionary<string, (List<Face> faces, float textureSizeInMeters, ETexAddress tilingU, ETexAddress tilingV)> materialFaceMap = [];
                for (int i = 0; i < crystal.Faces.Length; ++i)
                {
                    var face = crystal.Faces[i];
                    var materialLink = face.Material?.MaterialUserInst?.Link ?? string.Empty;
                    string materialName = TextureSampling.MaterialLinkToMaterialName(materialLink);
                    if (!materialFaceMap.TryGetValue(materialName, out var data))
                    {
                        data = new();
                        data.faces = new();
                        data.textureSizeInMeters = face.Material.MaterialUserInst.TextureSizeInMeters;
                        data.tilingU = face.Material.MaterialUserInst.TilingU;
                        data.tilingV = face.Material.MaterialUserInst.TilingV;
                        materialFaceMap[materialName] = data;
                    }
                    data.faces.Add(face);
                }
                foreach (var matData in materialFaceMap)
                {
                    var materialName = matData.Key;
                    var data = matData.Value;
                    List<Vector3> vertices = [];
                    List<Color> colors = [];
                    List<Int3> triangles = [];
                    Dictionary<int, int> positionToVertexIdx = [];
                    foreach (var face in data.faces)
                    {
                        var v0 = face.Vertices[0];
                        var v1 = face.Vertices[1];
                        var v2 = face.Vertices[2];
                        Color c0, c1, c2;
                        c0 = c1 = c2 = Color.Black;
                        Materials.Material material = null!;
                        if (settings.UniformColor)
                        {
                            c0 = settings.Color;
                            c1 = settings.Color;
                            c2 = settings.Color;
                        }
                        else if (materialLibrary.TryGetMaterial(materialName, out material))
                        {
                            c0 = TextureSampling.SampleColor(material, v0.TexCoord.X, v0.TexCoord.Y, data.textureSizeInMeters, data.tilingU, data.tilingV);
                            c1 = TextureSampling.SampleColor(material, v1.TexCoord.X, v1.TexCoord.Y, data.textureSizeInMeters, data.tilingU, data.tilingV);
                            c2 = TextureSampling.SampleColor(material, v2.TexCoord.X, v2.TexCoord.Y, data.textureSizeInMeters, data.tilingU, data.tilingV);
                        }
                        if (!positionToVertexIdx.TryGetValue(v0.Index, out var v0Idx))
                            positionToVertexIdx[v0.Index] = v0Idx = vertices.Count;
                        var p0 = crystal.Positions[v0.Index];
                        vertices.Add(new Vector3(p0.X, p0.Y, p0.Z));
                        colors.Add(c0);

                        if (!positionToVertexIdx.TryGetValue(v1.Index, out var v1Idx))
                            positionToVertexIdx[v1.Index] = v1Idx = vertices.Count;
                        var p1 = crystal.Positions[v1.Index];
                        vertices.Add(new Vector3(p1.X, p1.Y, p1.Z));
                        colors.Add(c1);

                        if (!positionToVertexIdx.TryGetValue(v2.Index, out var v2Idx))
                            positionToVertexIdx[v2.Index] = v2Idx = vertices.Count;
                        var p2 = crystal.Positions[v2.Index];
                        vertices.Add(new Vector3(p2.X, p2.Y, p2.Z));
                        colors.Add(c2);

                        triangles.Add(new Int3(v0Idx, v1Idx, v2Idx));

                        if (face.Vertices.Length == 4)
                        {
                            var v3 = face.Vertices[3];
                            if (!positionToVertexIdx.TryGetValue(v3.Index, out var v3Idx))
                                positionToVertexIdx[v3.Index] = v3Idx = vertices.Count;
                            var p3 = crystal.Positions[v3.Index];
                            vertices.Add(new Vector3(p3.X, p3.Y, p3.Z));
                            colors.Add(TextureSampling.SampleColor(material, v3.TexCoord.X, v3.TexCoord.Y, data.textureSizeInMeters, data.tilingU, data.tilingV));

                            triangles.Add(new Int3(v2Idx, v3Idx, v0Idx));
                        }
                    }

                    var triangleObject = new TriangleObject(
                   points: vertices.ToArray(),
                   triangles: triangles.ToArray(),
                   colors: colors.ToArray(),
                   uniqueVertices: false)
                    {
                        Name = $"-{materialName}",
                        CanShareBlock = false,
                    };
                    obj.AddSubObjects(triangleObject);
                }
            }
        }
        static void ReadMesh(CPlugSolid2Model mesh, TriangleObject obj, MaterialLibrary materialLibrary, ItemToTriangleObjectConverterSettings settings)
        {
            Dictionary<string, TriangleData> materialGroups = [];
            for (int i = 0; i < mesh.Visuals.Length; ++i)
            {
                var visual = mesh.Visuals[i] as CPlugVisualIndexedTriangles;
                var materialInstance = mesh.CustomMaterials[i].MaterialUserInst;
                if (visual == null)
                    continue;

                var tData = new TriangleData();
                ReadVisual(visual, tData, materialInstance, i, materialLibrary, settings);
                if (!settings.Grouping.HasFlag(TriangleGrouping.Materials))
                {
                    string matName = TextureSampling.MaterialLinkToMaterialName(materialInstance.Link);
                    if (!materialGroups.TryGetValue(matName, out var materialTriangleData))
                    {
                        materialTriangleData = new();
                        materialGroups[matName] = materialTriangleData;
                    }
                    var offset = materialTriangleData.vertices.Count;
                    materialTriangleData.triangles.AddRange(tData.triangles.Select(t => t + new Int3(offset, offset, offset)));
                    materialTriangleData.vertices.AddRange(tData.vertices);
                    materialTriangleData.colors.AddRange(tData.colors);
                }
                else
                {
                    var triangleObject = new TriangleObject(
                       points: tData.vertices.ToArray(),
                       triangles: tData.triangles.ToArray(),
                       colors: tData.colors.ToArray(),
                       uniqueVertices: false)
                    {
                        Name = $"-Visual[{i}]",
                        CanShareBlock = settings.Grouping.HasFlag(TriangleGrouping.Layers),
                    };
                    obj.AddSubObjects(triangleObject);
                }
            }
            if (!settings.Grouping.HasFlag(TriangleGrouping.Materials))
            {
                foreach (var matData in materialGroups)
                {
                    var triangleObject = new TriangleObject(
                     points: matData.Value.vertices.ToArray(),
                     triangles: matData.Value.triangles.ToArray(),
                     colors: matData.Value.colors.ToArray(),
                     uniqueVertices: false)
                    {
                        Name = $"-{matData.Key}",
                        CanShareBlock = false,
                    };
                    obj.AddSubObjects(triangleObject);
                }
            }
        }
        static void ReadVisual(CPlugVisualIndexedTriangles visual, TriangleData triangleData, CPlugMaterialUserInst materialInstance, int visualIdx, MaterialLibrary materialLibrary, ItemToTriangleObjectConverterSettings settings)
        {
            Logger.Trace($"Converting Mesh Visual {visualIdx}");

            string materialName = TextureSampling.MaterialLinkToMaterialName(materialInstance.Link);
            if (!materialLibrary.TryGetMaterial(materialName, out var material))
                material = new Materials.Material();

            for (int i = 0; i < visual.VertexStreams[0].Positions.Length; ++i)
            {
                var vertex = visual.VertexStreams[0].Positions[i];
                var uv = visual.VertexStreams[0].UVs[0][i];
                triangleData.vertices.Add(new Vector3(vertex.X, vertex.Y, vertex.Z));
                if (settings.UniformColor)
                    triangleData.colors.Add(settings.Color);
                else
                    triangleData.colors.Add(TextureSampling.SampleColor(material, uv.X, uv.Y, materialInstance.TextureSizeInMeters, materialInstance.TilingU, materialInstance.TilingV));
            }
            for (int i = 0; i < visual.IndexBuffer.Indices.Length; i += 3)
            {
                triangleData.triangles.Add(new Int3(visual.IndexBuffer.Indices[i], visual.IndexBuffer.Indices[i + 1], visual.IndexBuffer.Indices[i + 2]));
            }
        }

    }
}
