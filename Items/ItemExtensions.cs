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
        return solid != null;
    }
    public static bool TryGetStaticObjectModel(this CGameItemModel item, out CPlugStaticObjectModel staticObjectModel)
    {
        staticObjectModel = null;
        if (TryGetStaticModelEntRef(item, out var entRef))
            staticObjectModel = entRef.Model as CPlugStaticObjectModel;
        if (item.EntityModel is CGameCommonItemEntityModel commonItemEntityModel)
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
    public static bool TryGetDynaModelEntRef(this CGameItemModel item, out CPlugPrefab.EntRef entRef)
    {
        entRef = null;
        if (item.EntityModel is CPlugPrefab prefab)
        {
            var ents = prefab.Ents.ToList();
            foreach (var ent in ents)
            {
                if (ent.Model is CPlugDynaObjectModel dyna)
                {
                    entRef = ent;
                    break;
                }
            }
        }
        return entRef != null;
    }
    public static bool TryGetStaticModelEntRef(this CGameItemModel item, out CPlugPrefab.EntRef entRef)
    {
        entRef = null;
        if (item.EntityModel is CPlugPrefab prefab)
        {
            var ents = prefab.Ents.ToList();
            foreach (var ent in ents)
            {
                if (ent.Model is CPlugStaticObjectModel staticObject)
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

    static void DeepCloneAllFields(object source, object target, Dictionary<object, object> visited)
    {
        var currentType = source.GetType();
        while (currentType != null && currentType != typeof(object))
        {
            foreach (var field in currentType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                var value = field.GetValue(source);
                field.SetValue(target, DeepCloneValue(value, visited));
            }
            currentType = currentType.BaseType;
        }
    }
    static object? DeepCloneValue(object? value, Dictionary<object, object> visited)
    {
        if (value == null) return null;

        var type = value.GetType();

        // Primitives, enums, strings — immutable, safe to share
        if (type.IsPrimitive || type.IsEnum || value is string) return value;

        // Avoid circular references
        if (visited.TryGetValue(value, out var existing)) return existing;

        // Arrays
        if (type.IsArray)
        {
            var source = (Array)value;
            var elementType = type.GetElementType()!;

            // Multi-dimensional (e.g. float[,] or float[,,])
            if (source.Rank > 1)
            {
                var lengths = Enumerable.Range(0, source.Rank)
                    .Select(source.GetLength)
                    .ToArray();
                var clone = Array.CreateInstance(elementType, lengths);
                visited[value] = clone;

                // Walk every index combination
                var indices = new int[source.Rank];
                void CopyRecursive(int dimension)
                {
                    for (int i = 0; i < source.GetLength(dimension); i++)
                    {
                        indices[dimension] = i;
                        if (dimension == source.Rank - 1)
                            clone.SetValue(DeepCloneValue(source.GetValue(indices), visited), indices);
                        else
                            CopyRecursive(dimension + 1);
                    }
                }
                CopyRecursive(0);
                return clone;
            }

            // 1D (includes jagged T[][])
            var clone1d = Array.CreateInstance(elementType, source.Length);
            visited[value] = clone1d;
            for (int i = 0; i < source.Length; i++)
                clone1d.SetValue(DeepCloneValue(source.GetValue(i), visited), i);
            return clone1d;
        }

        // Value types (structs) that aren't primitive — copy by field
        if (type.IsValueType)
        {
            // Box a copy, clone its fields in place
            object boxed = RuntimeHelpers.GetUninitializedObject(type);
            DeepCloneAllFields(value, boxed, visited);
            return boxed;
        }

        // CMwNod subclasses — use the same chunk-aware path
        if (value is CMwNod nod)
        {
            var clone = (CMwNod)RuntimeHelpers.GetUninitializedObject(type);
            visited[value] = clone;
            DeepCloneAllFields(nod, clone, visited);

            // CopyAllFields already copied the Chunks backing fields (source refs),
            // so wipe it clean before adding the deep-cloned versions
            clone.Chunks.Clear();

            foreach (var chunk in nod.Chunks)
            {
                var chunkClone = (IChunk)DeepCloneValue(chunk, visited)!;
                clone.Chunks.Add(chunkClone);
            }
            return clone;
        }

        // Generic objects
        var obj = RuntimeHelpers.GetUninitializedObject(type);
        visited[value] = obj;
        DeepCloneAllFields(value, obj, visited);
        return obj;
    }

    public static T DeepCloneObject<T>(T template) where T : class
    {
        var visited = new Dictionary<object, object>(ReferenceEqualityComparer.Instance);
        return (T)DeepCloneValue(template, visited)!;
    }

}
