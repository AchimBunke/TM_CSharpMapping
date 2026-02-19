
using GBX.NET;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Plug;
using System.Numerics;
using TM_GenericMapping.Common;
using static GBX.NET.Engines.Plug.CPlugCrystal;
using static GBX.NET.Engines.Plug.CPlugSurface;

namespace TM_GenericMapping.Items
{
    public struct TriangleToItemConverterSettings
    {
        public string TemplatePath;
        public bool VisibleGeometry;
        public bool UseProvidedRenderer;
    }
    /// <summary>
    /// Converts TriangleObjects to items.
    /// </summary>
    public static class TriangleToItemConverter
    {
        public const string NotCollidable = "NotCollidable";
        public const string RoadTech = "RoadTech";
        public const string TrackBorders = "TrackBorders";
        static Dictionary<string, string> NameToLink = new()
        {
            [""] = "",
            [RoadTech] = @"Stadium\Media\Material\RoadTech",
            [TrackBorders] = @"Stadium\Media\Material\TrackBorders",
        };



        public static CGameItemModel Convert(TriangleObject triangleData, TriangleToItemConverterSettings settings)
        {
            var item = Gbx.Parse<CGameItemModel>(settings.TemplatePath).Node;
            var meshCrystal = (item.EntityModelEdition as CGameCommonItemEntityModelEdition)!.MeshCrystal;

            HideVisibleRoot(meshCrystal); 

            var meshData = ParseTriangleObjectData(triangleData, out var blockName);
            CreateGeometryLayers(meshCrystal, triangleData, settings, meshData);
         
            item.Name = blockName;

            return item;
        }

        static void HideVisibleRoot(CPlugCrystal meshCrystal)
        {
            var layer = meshCrystal.Layers.Find(l => l.LayerName == TrianglesUtils.VisibleRootLayerName) as GeometryLayer;
            var crystal = layer.Crystal;
            if (layer == null)
                return;
            for (int i = 0; i < crystal.Positions.Length; ++i)
            {
                crystal.Positions[i] *= 0.001f;
            }
        }

        static void CreateGeometryLayers(CPlugCrystal meshCrystal, TriangleObject triangleData, TriangleToItemConverterSettings settings, Dictionary<string, List<TriangleObject>> meshData)
        {
            var layerTemplate = meshCrystal.Layers.Find(l => l.LayerName == "Geometry") as GeometryLayer;

            meshCrystal.Layers.Remove(layerTemplate);
            meshCrystal.Materials.RemoveAt(meshCrystal.Materials.Count - 1);

            Face faceTemplate = layerTemplate.Crystal.Faces[0];
            CPlugMaterialUserInst materialUserInstTemplate = ItemExtensions.DeepCopyCPlugMaterialUserInst(faceTemplate.Material.MaterialUserInst);
            Vertex vertexTemplate = faceTemplate.Vertices[0];

            Dictionary<string, CPlugMaterialUserInst> materialDic = [];
            foreach (var kv in meshData)
            {
                string material = kv.Key;
                var geometries = kv.Value;


                var layer = ItemExtensions.DeepCopyGeometryLayer(layerTemplate);
                var crystal = layer.Crystal;
                layer.IsVisible = settings.VisibleGeometry;
                layer.LayerName = material;

                List<Vec3> positions = [];
                List<Face> faces = [];
                if (!materialDic.TryGetValue(material, out var materialUserInst))
                {
                    materialUserInst = ItemExtensions.DeepCopyCPlugMaterialUserInst(materialUserInstTemplate);
                    materialUserInst.SurfacePhysicId = NameToMaterialId(material);
                    if (NameToLink.TryGetValue(material, out var link))
                        materialUserInst.Link = link;
                    materialDic[material] = materialUserInst;
                }

                var cPlugCrystalMaterial = new Material() { MaterialUserInst = materialUserInst };
                foreach (var geometry in geometries)
                {
                    int vertexIdxOffset = positions.Count;
                    positions.AddRange(geometry.Vertices.Select(v =>
                    {
                        if (settings.UseProvidedRenderer && geometry.Renderer is Triangle3DRenderer renderer3D)
                        {
                            return renderer3D.ApplyRenderingPostProcessing(Vector3.Transform(v, geometry.LocalToWorldTRS)).ToVec3();  
                        }
                        else
                        {
                            return Vector3.Transform(v, geometry.LocalToWorldTRS).ToVec3();
                        }
                    }));
                    for (int i = 0; i < geometry.Triangles.Length; i ++)
                    {
                        var tris = geometry.Triangles[i];
                        var face = faceTemplate with
                        {
                            Vertices =
                            [
                                vertexTemplate with { Index = vertexIdxOffset + tris.X, LightmapCoord = (0.5f,0.5f), TexCoord = (0,0)},
                                vertexTemplate with { Index = vertexIdxOffset + tris.Y, LightmapCoord = (0.5f,0.5f), TexCoord = (0,0)},
                                vertexTemplate with { Index = vertexIdxOffset + tris.Z, LightmapCoord = (0.5f,0.5f), TexCoord = (0,0)},
                            ],
                            Material = cPlugCrystalMaterial,
                        };
                        faces.Add(face);
                    }
                }
                crystal.Faces = faces.ToArray();
                crystal.Positions = positions.ToArray();
                meshCrystal.Layers.Add(layer);
                meshCrystal.Materials.Add(cPlugCrystalMaterial);
            }
           
        }

        static Dictionary<string, List<TriangleObject>> ParseTriangleObjectData(TriangleObject triangleData, out string blockName)
        {
            blockName = triangleData.Name;
            Dictionary<string, List<TriangleObject>> dic = [];
            foreach (var subObject in triangleData.SubObjects.OfType<TriangleObject>())
            {
                string name = subObject.Name;
                if (name == NotCollidable)
                    continue;
                var geometries = FlattenRelevantData(subObject);
                if (geometries.Count() == 0)
                    continue;
                if (!dic.TryGetValue(name, out var l))
                    dic[name] = l = [];
                l.AddRange(geometries);
            }
            return dic;
        }
        static IEnumerable<TriangleObject> FlattenRelevantData(TriangleObject o)
        {
            List<TriangleObject> obj = [];
            if (o.Vertices.Length > 0)
                obj.Add(o);
            foreach (var s in o.SubObjects.OfType<TriangleObject>())
                obj.AddRange(FlattenRelevantData(s));
            return obj;
        }

        static MaterialId NameToMaterialId(string name)
            => name switch
            {
                RoadTech => MaterialId.Asphalt,
                TrackBorders => MaterialId.Rubber,
                _ => Enum.TryParse(typeof(MaterialId), name, out var result) ? (MaterialId)result : MaterialId.Asphalt,
            };

        public static void MakeInvisible(CGameItemModel item)
        {
            var meshCrystal = (item.EntityModelEdition as CGameCommonItemEntityModelEdition)!.MeshCrystal;

            foreach(var layer in meshCrystal.Layers.Where(l => l.LayerName != TrianglesUtils.VisibleRootLayerName).OfType<GeometryLayer>())
            {
                layer.IsVisible = false;
            }
            item.Name = item.Name + "_Invis";
        }

    }
}
