using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.MwFoundations;
using GBX.NET.Engines.Plug;
using GBX.NET.Serialization.Chunking;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Schema;

namespace TM_GenericMapping.Items;

public static class ItemExtensions
{
    public static bool TryGetNPlugDyna_SKinematicConstraint(CGameItemModel itemModel,
        out NPlugDyna_SKinematicConstraint nPlugDyna_SKinematicConstraint)
    {
        nPlugDyna_SKinematicConstraint = default!;
        var entityModel = itemModel.EntityModel as CPlugPrefab;
        if (entityModel == null)
            return false;
        
        var ents = entityModel.Ents.ToList();
        foreach (var ent in ents)
        {
            if (ent.Model is NPlugDyna_SKinematicConstraint c)
            {
                nPlugDyna_SKinematicConstraint = c;
                return true;
            }
        }
        return false;
    }

    public static CPlugMaterialUserInst DeepCopyCPlugMaterialUserInst(CPlugMaterialUserInst mat)
    {
        var cpy = new CPlugMaterialUserInst()
        {
            Model = mat.Model,
            SurfaceGameplayId = mat.SurfaceGameplayId,
            BaseTexture = mat.BaseTexture,
            SurfacePhysicId = mat.SurfacePhysicId,
            TextureSizeInMeters = mat.TextureSizeInMeters,
            Color = mat.Color,
            UvAnims = mat.UvAnims,
            Csts = mat.Csts,
            HidingGroup = mat.HidingGroup,
            IsNatural = mat.IsNatural,
            IsUsingGameMaterial = mat.IsUsingGameMaterial,
            Link = mat.Link,
            MaterialName = mat.MaterialName,
            TilingU = mat.TilingU,
            TilingV = mat.TilingV,
            UserTextures = mat.UserTextures,
        };
        foreach (var chunk in mat.Chunks)
        {
            cpy.Chunks.Add(chunk);
        }
        return cpy;
    }

    public static CPlugCrystal.GeometryLayer DeepCopyGeometryLayer(CPlugCrystal.GeometryLayer geometryLayer)
    {
        var cpy = new CPlugCrystal.GeometryLayer()
        {
            Collidable = geometryLayer.Collidable,
            CrystalEnabled = geometryLayer.CrystalEnabled,
            GeometryVersion = geometryLayer.GeometryVersion,
            IsEnabled = geometryLayer.IsEnabled,
            IsVisible = geometryLayer.IsVisible,
            LayerId = geometryLayer.LayerId,
            LayerName = geometryLayer.LayerName,
            Ver = geometryLayer.Ver,
            U02 = [..geometryLayer.U02 ?? []],
            Crystal = geometryLayer.Crystal != null ? DeepCopyCrystal(geometryLayer.Crystal) : null,
        };

        return cpy;
    }
    public static CPlugCrystal.Crystal DeepCopyCrystal(CPlugCrystal.Crystal crystal)
    {
        var cpy = new CPlugCrystal.Crystal()
        {
            U01 = crystal.U01,
            U02 = crystal.U02,
            U03 = crystal.U03,
            U04 = crystal.U04,
            U05 = crystal.U05,
            U06 = crystal.U06,
            U07 = crystal.U07,
            Version = crystal.Version,
            IsEmbeddedCrystal = crystal.IsEmbeddedCrystal,
            AnchorInfos = [..crystal.AnchorInfos],
            Edges = [..crystal.Edges],
            Faces = [..crystal.Faces],
            Groups = [.. crystal.Groups],
            Positions = [.. crystal.Positions],
            VisualLevels = [.. crystal.VisualLevels],
        };
        return cpy;
    }

    public static void CopyAnimationsTo(this CGameItemModel sourceItemModel, CGameItemModel targetItemModel)
    {
        if (!ItemExtensions.TryGetNPlugDyna_SKinematicConstraint(sourceItemModel, out var sourceKinematicConstraint))
            return;
        if (!ItemExtensions.TryGetNPlugDyna_SKinematicConstraint(targetItemModel, out var targetKinematicConstraint))
            return;

        targetKinematicConstraint.AngleMaxDeg = sourceKinematicConstraint.AngleMaxDeg;
        targetKinematicConstraint.AngleMinDeg = sourceKinematicConstraint.AngleMinDeg;
        targetKinematicConstraint.RotAnimFunc = sourceKinematicConstraint.RotAnimFunc;
        targetKinematicConstraint.RotAxis = sourceKinematicConstraint.RotAxis;

        targetKinematicConstraint.TransAnimFunc = sourceKinematicConstraint.TransAnimFunc;
        targetKinematicConstraint.TransAxis = sourceKinematicConstraint.TransAxis;
        targetKinematicConstraint.TransMin = sourceKinematicConstraint.TransMin;
        targetKinematicConstraint.TransMax = sourceKinematicConstraint.TransMax;


    }

    public static bool TryGetCrystal(this CGameItemModel item, out CPlugCrystal crystal)
    {
        crystal = null; 
        if (item.EntityModelEdition is CGameCommonItemEntityModelEdition commonItemEntityModelEdition)
        {
            crystal = commonItemEntityModelEdition.MeshCrystal;
        }
        return crystal != null;
    }
    public static bool TryGetSolid2Model(this CGameItemModel item, out CPlugSolid2Model solid)
    {
        solid = null;
        if (item.EntityModel is CGameCommonItemEntityModel commonItemEntityModel)
        {
            solid = commonItemEntityModel?.StaticObject?.Mesh;
        }
        else if(item.EntityModel is CPlugPrefab prefab)
        {
            if (TryGetStaticObjectModel(item, out var staticObjectModel))
                solid = staticObjectModel.Mesh;
        }
        return solid != null;
    }
    public static bool TryGetStaticObjectModel(this CGameItemModel item, out CPlugStaticObjectModel staticObjectModel)
    {
        staticObjectModel = null;
        if (TryGetStaticModelEntRef(item, out var entRef))
            staticObjectModel = entRef.Model as CPlugStaticObjectModel;
        else if (item.EntityModel is CGameCommonItemEntityModel commonItemEntityModel)
            staticObjectModel = commonItemEntityModel.StaticObject;
        return staticObjectModel != null;
    }
    public static bool TryGetDynaObjectModel(this CGameItemModel item, out CPlugDynaObjectModel dynaObjectModel)
    {
        dynaObjectModel = null;
        if(TryGetDynaModelEntRef(item, out var entRef))
            dynaObjectModel = entRef.Model as CPlugDynaObjectModel;
        return dynaObjectModel != null;
    }
    public static bool TryGetTriggerSpecial(this CGameItemModel item, out NPlugTrigger_SSpecial triggerSpecial)
    {
        triggerSpecial = null;
        if (TryGetTriggerSpecialEntRef(item, out var entRef))
            triggerSpecial = entRef.Model as NPlugTrigger_SSpecial;
        return triggerSpecial != null;
    }
    public static bool TryGetTriggerShape(this CGameItemModel item, out CPlugSurface triggerShape)
    {
        triggerShape = null;
        if(item.EntityModel is CGameCommonItemEntityModel commonItemEntityModel)
            triggerShape = commonItemEntityModel.TriggerShape as CPlugSurface;
        else if (TryGetTriggerSpecialEntRef(item, out var entRef))
            triggerShape = entRef.Model as CPlugSurface;
        return triggerShape != null;
    }
    public static bool TryGetTriggerWaypoint(this CGameItemModel item, out NPlugTrigger_SWaypoint waypoint)
    {
        waypoint = null;
        if (TryGetTriggerWaypointEntRef(item, out var entRef))
            waypoint = entRef.Model as NPlugTrigger_SWaypoint;
        return waypoint != null;
    }
    public static bool TryGetDynaModelEntRef(this CGameItemModel item, out CPlugPrefab.EntRef entRef)
        => TryGetEntRef<CPlugDynaObjectModel>(item, out entRef);
    public static bool TryGetStaticModelEntRef(this CGameItemModel item, out CPlugPrefab.EntRef entRef)
        => TryGetEntRef<CPlugStaticObjectModel>(item, out entRef);
    public static bool TryGetTriggerSpecialEntRef(this CGameItemModel item, out CPlugPrefab.EntRef entRef)
        => TryGetEntRef<NPlugTrigger_SSpecial>(item, out entRef);
    public static bool TryGetTriggerWaypointEntRef(this CGameItemModel item, out CPlugPrefab.EntRef entRef)
        => TryGetEntRef<NPlugTrigger_SWaypoint>(item, out entRef);
    public static bool TryGetEntRef<T>(this CGameItemModel item, out CPlugPrefab.EntRef entRef) where T : CMwNod
    {
        entRef = null;
        if (item.EntityModel is CPlugPrefab prefab)
        {
            var ents = prefab.Ents.ToList();
            foreach (var ent in ents)
            {
                if (ent.Model is T)
                {
                    entRef = ent;
                    break;
                }
            }
        }
        return entRef != null;
    }

    static void ShallowCloneAllFields(object source, object target)
    {
        var currentType = source.GetType();
        while (currentType != null && currentType != typeof(object))
        {
            foreach (var field in currentType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                var value = field.GetValue(source);
                field.SetValue(target, value);
            }

            currentType = currentType.BaseType;
        }
    }
    public static T ShallowClone<T>(T template) where T : new()
    {
        //return template;
        var shell = new T();
        ShallowCloneAllFields(template, shell);
        if (shell is CMwNod shellNod && template is CMwNod templateNod)
        {
            foreach (var chunk in templateNod.Chunks)
            {
                shellNod.Chunks.Add(chunk);
            }
        }
        return shell;
    }




}
