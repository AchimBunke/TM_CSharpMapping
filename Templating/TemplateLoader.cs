using GBX.NET;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.Plug;
using TM_GenericMapping.Common;
using TM_GenericMapping.Common.IO;
using TM_GenericMapping.Items;

namespace TM_GenericMapping.Templating;

public static class TemplateLoader
{
    public static IEnumerable<string> GetTemplateResources()
    {
        var assembly = typeof(TemplateLoader).Assembly;
        var directory = $"{assembly.GetName().Name}.Templates";

        return assembly
            .GetManifestResourceNames()
            .Where(x => x.StartsWith(directory, StringComparison.OrdinalIgnoreCase));
    }
    public static Stream GetTemplate(string fileName)
    {
        var assembly = typeof(TemplateLoader).Assembly;
        var name = $"{assembly.GetName().Name}.Templates.{fileName}";
        return assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Embedded template not found: {name}");
    }
}

public static class GbxTemplateLibrary
{
    // Placement

    static CGameItemPlacementParam cGameItemPlacementParam = null!;
    public static GbxTemplate<CGameItemPlacementParam> CreatePlacementParamTemplate() 
        => new GbxTemplate<CGameItemPlacementParam>(ObjectCloner.DeepCloneObject(cGameItemPlacementParam ??= Gbx.Parse<CGameItemPlacementParam>(TemplateLoader.GetTemplate("PlacementParamTemplate.PlaceParam.Gbx"))));

    public static GbxTemplate<CGameItemPlacementParam> CreatePlacementParamTemplateWithPlacementClass()
    {
        var placementParams = CreatePlacementParamTemplate();
        var placementClass = CreateItemPlacementClassTemplate();
        ItemPlacementUtils.SetItemPlacementClass(placementParams, placementClass);
        return placementParams;
    }

    static NPlugItemPlacement_SClass nPlugItemPlacement_SClass = null!;
    public static GbxTemplate<NPlugItemPlacement_SClass> CreateItemPlacementClassTemplate()
        => new GbxTemplate<NPlugItemPlacement_SClass>(ObjectCloner.DeepCloneObject(nPlugItemPlacement_SClass ??= Gbx.Parse<NPlugItemPlacement_SClass>(TemplateLoader.GetTemplate("PlacementClassTemplate.ItemPlacementClass.Gbx"))));


    // CommonEntityModel & Mesh
   
    static CGameItemModel commonItemEntityModelItemTemplate = null!;
    public static GbxTemplate<CGameItemModel> CreateCommonItemEntityModelItemTemplate()
        => new GbxTemplate<CGameItemModel>(ObjectCloner.DeepCloneObject(commonItemEntityModelItemTemplate ??= Gbx.Parse<CGameItemModel>(TemplateLoader.GetTemplate("CommonEntityModelItemTemplate.Item.Gbx"))));


    static CPlugStaticObjectModel staticObjectModelTemplate = null!;
    public static GbxTemplate<CPlugStaticObjectModel> CreateStaticObjectModelTemplate()
       => new GbxTemplate<CPlugStaticObjectModel>(ObjectCloner.DeepCloneObject(staticObjectModelTemplate ??= Gbx.Parse<CPlugStaticObjectModel>(TemplateLoader.GetTemplate("StaticObjectModelTemplate.ObjectModel.Gbx"))));

    static CPlugSolid2Model solid2ModelTemplate = null!;
    public static GbxTemplate<CPlugSolid2Model> CreateCPlugSolid2ModelTemplate()
       => new GbxTemplate<CPlugSolid2Model>(ObjectCloner.DeepCloneObject(solid2ModelTemplate ??= Gbx.Parse<CPlugSolid2Model>(TemplateLoader.GetTemplate("Solid2ModelTemplate.Mesh.Gbx"))));

    public static GbxTemplate<CPlugVisualIndexedTriangles> CreateCPlugVisualIndexedTrianglesTemplate()
       => new GbxTemplate<CPlugVisualIndexedTriangles>(CreateCPlugSolid2ModelTemplate().Value.Visuals[0] as CPlugVisualIndexedTriangles);

    public static GbxTemplate<CPlugVertexStream> CreateCPlugVertexStreamTemplate()
      => new GbxTemplate<CPlugVertexStream>(CreateCPlugVisualIndexedTrianglesTemplate().Value.VertexStreams[0] as CPlugVertexStream);

    public static GbxTemplate<CPlugIndexBuffer> CreateCPlugIndexBufferTemplate()
    => new GbxTemplate<CPlugIndexBuffer>(CreateCPlugVisualIndexedTrianglesTemplate().Value.IndexBuffer as CPlugIndexBuffer);


    // ModelEdition & MeshModeler
    static CGameItemModel commonEntityModelEditionItemTemplate = null!;
    public static GbxTemplate<CGameItemModel> CreateCommonItemEntityModelEditionItemTemplate()
        => new GbxTemplate<CGameItemModel>(ObjectCloner.DeepCloneObject(commonEntityModelEditionItemTemplate ??= Gbx.Parse<CGameItemModel>(TemplateLoader.GetTemplate("CommonEntityModelEditionItemTemplate.Item.Gbx"))));

    static CGameCommonItemEntityModel commonItemEntityModelTemplate = null!;
    public static GbxTemplate<CGameCommonItemEntityModel> CreateCommonItemEntityModelTemplate()
       => new GbxTemplate<CGameCommonItemEntityModel>(ObjectCloner.DeepCloneObject(commonItemEntityModelTemplate ??= Gbx.Parse<CGameCommonItemEntityModel>(TemplateLoader.GetTemplate("CommonItemEntityModelTemplate.EntityModel.Gbx"))));

    static CPlugCrystal crystalTemplate = null!;
    public static GbxTemplate<CPlugCrystal> CreateCPlugCrystalTemplate()
        => new GbxTemplate<CPlugCrystal>(ObjectCloner.DeepCloneObject(crystalTemplate ??= Gbx.Parse<CPlugCrystal>(TemplateLoader.GetTemplate("CrystalTemplate.Mesh.Gbx"))));


    public static GbxTemplate<CPlugCrystal.GeometryLayer> CreateGeometryLayerTemplate()
        => new GbxTemplate<CPlugCrystal.GeometryLayer>(CreateCPlugCrystalTemplate().Value.Layers[0] as CPlugCrystal.GeometryLayer);
    public static GbxTemplate<CPlugCrystal.Crystal> CreateGeometryCrystalTemplate()
    => new GbxTemplate<CPlugCrystal.Crystal>(CreateGeometryLayerTemplate().Value.Crystal! as CPlugCrystal.Crystal);

    public static GbxTemplate<CPlugCrystal.TriggerLayer> CreateTriggerLayerTemplate()
       => new GbxTemplate<CPlugCrystal.TriggerLayer>(CreateCPlugCrystalTemplate().Value.Layers[1]! as CPlugCrystal.TriggerLayer);
    public static GbxTemplate<CPlugCrystal.Crystal> CreateTriggerCrystalTemplate()
     => new GbxTemplate<CPlugCrystal.Crystal>(CreateTriggerLayerTemplate().Value.Crystal! as CPlugCrystal.Crystal);


    // Moving & mesh
    static CGameItemModel movingItemTemplate = null!;
    public static GbxTemplate<CGameItemModel> CreateMovingItemTemplate()
        => new GbxTemplate<CGameItemModel>(ObjectCloner.DeepCloneObject(movingItemTemplate ??= Gbx.Parse<CGameItemModel>(TemplateLoader.GetTemplate("MovingItemTemplate.Item.Gbx"))));

    static CPlugDynaObjectModel dynaObjectModelTemplate = null!;
    public static GbxTemplate<CPlugDynaObjectModel> CreateDynaObjectModelTemplate()
        => new GbxTemplate<CPlugDynaObjectModel>((CreateMovingItemTemplate().Value.EntityModel as CPlugPrefab).Ents[0].Model as CPlugDynaObjectModel);

    static CPlugSurface surfaceTemplate = null!;
    public static GbxTemplate<CPlugSurface> CreateSurfaceTemplate()
        => new GbxTemplate<CPlugSurface>(ObjectCloner.DeepCloneObject(surfaceTemplate ??= Gbx.Parse<CPlugSurface>(TemplateLoader.GetTemplate("SurfaceTemplate.Mesh.Gbx"))));

    public static GbxTemplate<CPlugSurface.Mesh> CreateSurfaceMeshTemplate()
    => new GbxTemplate<CPlugSurface.Mesh>(CreateSurfaceTemplate().Value.Surf as CPlugSurface.Mesh);

    public static GbxTemplate<NPlugDyna_SKinematicConstraint> CreateKinematicConstraintTemplate()
        => new GbxTemplate<NPlugDyna_SKinematicConstraint>((CreateMovingItemTemplate().Value.EntityModel as CPlugPrefab).Ents[1].Model as NPlugDyna_SKinematicConstraint);


    public static GbxTemplate<CPlugPrefab> CreatePrefabTemplate()
    {
        var prefab = new CPlugPrefab()
        {
            Ents = [],
            FileWriteTime = DateTime.Now,
            Version = 11,
        };
        return new GbxTemplate<CPlugPrefab>(prefab);
    }

    // trigger

    static CGameItemModel triggerItemModelTemplate = null!;
    public static GbxTemplate<CGameItemModel> CreateTriggerItemTemplate()
        => new GbxTemplate<CGameItemModel>(ObjectCloner.DeepCloneObject(triggerItemModelTemplate ??= Gbx.Parse<CGameItemModel>(TemplateLoader.GetTemplate("TriggerItemTemplate.Item.Gbx"))));
}


