using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.Plug;
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
}
