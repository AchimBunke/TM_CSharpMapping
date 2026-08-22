using System.Collections;
using System.Reflection;
using System.Xml.Linq;

namespace TM_GenericMapping.Common;

[Flags]
public enum GbxObjectComparerFlags
{
    None = 0,
    PrivateFields = 1 << 1,
    IgnoreCustomComparers = 1 << 2,
    IgnoreCollectionOrder = 1 << 3,
}

public struct GbxObjectComparerOptions
{
    public GbxObjectComparerFlags Flags;
    public Dictionary<Type,IGbxStructureComparer> CustomComparers;

    public static GbxObjectComparerOptions Default => new GbxObjectComparerOptions
    {
        Flags = GbxObjectComparerFlags.PrivateFields,
        CustomComparers = [],
    };
}
public interface IGbxStructureComparer
{
    bool FullReplacement { get; }
    bool Equals(object obj1, object obj2, HashSet<(object, object)> visited, GbxObjectComparerOptions options);
    void AddHash(ref HashCode hash, object value, HashSet<object> visited,  GbxObjectComparerOptions options);

    bool EqualsField(FieldInfo fInfo, object obj1, object obj2, HashSet<(object, object)> visited, GbxObjectComparerOptions options);
    bool EqualsProperty(PropertyInfo pInfo, object obj1, object obj2, HashSet<(object, object)> visited, GbxObjectComparerOptions options);

    void AddHashField(ref HashCode hash, FieldInfo fInfo, object value, HashSet<object> visited, GbxObjectComparerOptions options);
    void AddHashProperty(ref HashCode hash, PropertyInfo pInfo, object value, HashSet<object> visited, GbxObjectComparerOptions options);
}
public abstract class GbxStructureComparerBase<T> : IGbxStructureComparer
{
    public virtual bool FullReplacement => false;

    public void AddHash(ref HashCode hash, object value, HashSet<object> visited, GbxObjectComparerOptions options)
        => AddHash(ref hash, (T)value, visited, options);
    public bool Equals(object obj1, object obj2, HashSet<(object, object)> visited, GbxObjectComparerOptions options)
        => Equals((T)obj1, (T)obj2, visited, options);
    protected virtual bool Equals(T obj1, T obj2, HashSet<(object, object)> visited, GbxObjectComparerOptions options) { throw new NotImplementedException(); }
    protected virtual void AddHash(ref HashCode hash, T value, HashSet<object> visited, GbxObjectComparerOptions options) { throw new NotImplementedException(); }

    public virtual void AddHashField(ref HashCode hash, FieldInfo fInfo, object value, HashSet<object> visited, GbxObjectComparerOptions options) 
    {
        GbxObjectComparer.AddHashField(ref hash, fInfo, value, visited, options);
    }
    public virtual void AddHashProperty(ref HashCode hash, PropertyInfo pInfo, object value, HashSet<object> visited, GbxObjectComparerOptions options)
    {
        GbxObjectComparer.AddHashProperty(ref hash, pInfo, value, visited, options);
    }

    public virtual bool EqualsField(FieldInfo fInfo, object obj1, object obj2, HashSet<(object, object)> visited, GbxObjectComparerOptions options)
    {
        return GbxObjectComparer.EqualsField(fInfo, obj1, obj2, visited, options);
    }

    public virtual bool EqualsProperty(PropertyInfo pInfo, object obj1, object obj2, HashSet<(object, object)> visited, GbxObjectComparerOptions options)
    {
        return GbxObjectComparer.EqualsProperty(pInfo, obj1, obj2, visited, options);
    }
}

public class CustomGbxStructureComparerAttribute : Attribute
{
    public required Type Type;
}

public static class GbxObjectComparer
{
    static Dictionary<Type, IGbxStructureComparer> customComparerRegistry = null!;
    static Dictionary<Type, IGbxStructureComparer> CustomComparerRegistry => customComparerRegistry ??= CreateCustomComparers();
    static Dictionary<Type, IGbxStructureComparer> CreateCustomComparers()
    {
        customComparerRegistry = new Dictionary<Type, IGbxStructureComparer>();
        var comparerTypes = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => typeof(IGbxStructureComparer).IsAssignableFrom(type) && !type.IsInterface && !type.IsAbstract && type.GetCustomAttribute<CustomGbxStructureComparerAttribute>() != null);
        foreach (var comparerType in comparerTypes)
        {
            var attribute = comparerType.GetCustomAttribute<CustomGbxStructureComparerAttribute>()!;
            var comparerInstance = (IGbxStructureComparer)Activator.CreateInstance(comparerType)!;
            customComparerRegistry[attribute.Type] = comparerInstance;
        }
        return customComparerRegistry;
    }

    private static readonly IEqualityComparer<(object, object)> ReferenceEqualityComparer =
    EqualityComparer<(object, object)>.Create(
        (a, b) => ReferenceEquals(a.Item1, b.Item1) && ReferenceEquals(a.Item2, b.Item2),
        a => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(a.Item1)
           ^ System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(a.Item2)
    );

    public static bool Equals(object obj1, object obj2, GbxObjectComparerFlags flags = GbxObjectComparerFlags.None)
       => Equals(obj1, obj2, GbxObjectComparerOptions.Default with { Flags = flags });

    public static bool Equals(object obj1, object obj2, GbxObjectComparerOptions options)
    {
        return Equals(obj1, obj2, new HashSet<(object, object)>(ReferenceEqualityComparer), options);
    }

    public static int GetHashCode(object obj, GbxObjectComparerFlags flags = GbxObjectComparerFlags.None)
        => GetHashCode(obj, GbxObjectComparerOptions.Default with { Flags = flags });
    public static int GetHashCode(object obj, GbxObjectComparerOptions options)
    {
        var hash = new HashCode();
        AddHash(ref hash, obj, new HashSet<object>(), options);
        return hash.ToHashCode();
    }

    private static bool Equals(object? obj1, object? obj2, HashSet<(object, object)> visited, GbxObjectComparerOptions options)
    {
        // Both null — equal
        if (obj1 == null && obj2 == null) return true;

        if (obj1 == null || obj2 == null) return false;

        Type type = obj1.GetType();

        if (obj2.GetType() != type) return false;

        // Primitives, strings, enums, decimals — use Equals directly
        if (type.IsPrimitive || type.IsEnum || obj1 is string || obj1 is decimal)
        {
            return obj1.Equals(obj2);
        }

        // Circular reference guard (reference types only)
        if (!type.IsValueType)
        {
            if (ReferenceEquals(obj1, obj2)) return true;
            if (!visited.Add((obj1, obj2))) return true;
        }

        // custom comparers
        IGbxStructureComparer? customComparer = null;
        if (!options.Flags.HasFlag(GbxObjectComparerFlags.IgnoreCustomComparers))
        {
            if (options.CustomComparers != null)
            {
                if (options.CustomComparers.TryGetValue(type, out customComparer))
                {
                    if(customComparer.FullReplacement)
                        return customComparer.Equals(obj1, obj2, visited, options);
                }
            }
            if (customComparer == null)
            {
                if (CustomComparerRegistry.TryGetValue(type, out customComparer))
                    if (customComparer.FullReplacement)
                        return customComparer.Equals(obj1, obj2, visited, options);
            }
        }
        

        // Value types that override Equals (e.g. DateTime, Guid, Vector3, etc.)
        // If the type overrides Equals, trust it rather than reflecting into fields
        if (type.IsValueType && type.GetMethod("Equals", new[] { typeof(object) })!.DeclaringType != typeof(ValueType))
        {
            return obj1.Equals(obj2);
        }

        // Collections
        if (obj1 is IEnumerable enum1 && obj2 is IEnumerable enum2)
        {
            if(!EnumerableEquals(enum1, enum2, visited, options))
                return false;

            // treat collection fields as equal if they are from System.Collections or System.Collections.Generic namespaces
            if (IsFrameworkCollection(type))
                return true;
        }

        // Reflect fields only (properties are usually computed from fields;
        // reflecting both causes double-reporting and can trigger side-effects)
        if (options.Flags.HasFlag(GbxObjectComparerFlags.PrivateFields))
        {
            var currentType = type;
            while (currentType != null && currentType != typeof(object))
            {
                foreach (var field in currentType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (customComparer != null)
                    {
                        if (!customComparer.EqualsField(field, obj1, obj2, visited, options))
                            return false;
                    } 
                    else if (!EqualsField(field, obj1, obj2, visited, options))
                        return false;
                }

                currentType = currentType.BaseType;
            }
        }
        else
        {
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                // Skip compiler-generated backing fields of auto-properties — we'll catch
                // those via the property loop below for cleaner path names
                if (field.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false))
                    continue;

                if (customComparer != null)
                {
                    if (!customComparer.EqualsField(field, obj1, obj2, visited, options))
                        return false;
                }
                else if (!EqualsField(field, obj1, obj2, visited, options))
                    return false;

            }
        }

        // Public readable, non-indexed properties
        if (!options.Flags.HasFlag(GbxObjectComparerFlags.PrivateFields))
        {
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!prop.CanRead || prop.GetIndexParameters().Length > 0) continue;

                if (customComparer != null)
                {
                    if (!customComparer.EqualsProperty(prop, obj1, obj2, visited, options))
                        return false;
                }
                else if (!EqualsProperty(prop, obj1, obj2, visited, options))
                    return false;
            }
        }
        return true;
    }

    public static bool EqualsField(FieldInfo field, object obj1, object obj2, HashSet<(object, object)> visited, GbxObjectComparerOptions options)
    {
        object? v1 = field.GetValue(obj1);
        object? v2 = field.GetValue(obj2);
        return Equals(v1, v2, visited, options);
    }
    public static bool EqualsProperty(PropertyInfo pInfo, object obj1, object obj2, HashSet<(object, object)> visited, GbxObjectComparerOptions options)
    {
        object? v1, v2;
        try { v1 = pInfo.GetValue(obj1); }
        catch (Exception) { return false; }
        try { v2 = pInfo.GetValue(obj2); }
        catch (Exception) { return false; }
        return Equals(v1, v2, visited, options);
    }

    private static bool EnumerableEquals(IEnumerable enum1, IEnumerable enum2, HashSet<(object, object)> visited, GbxObjectComparerOptions options)
    {
        if (options.Flags.HasFlag(GbxObjectComparerFlags.IgnoreCollectionOrder))
        {
            var list2 = enum2.Cast<object>().ToList();
            var matched = new bool[list2.Count];

            foreach (var item1 in enum1)
            {
                bool found = false;

                for (int i = 0; i < list2.Count; i++)
                {
                    if (!matched[i] && Equals(item1, list2[i], visited, options))
                    {
                        matched[i] = true;
                        found = true;
                        break;
                    }
                }

                if (!found)
                    return false;
            }

            return matched.All(x => x);
        }
        else
        {
            var e1 = enum1.GetEnumerator();
            var e2 = enum2.GetEnumerator();
            int index = 0;

            try
            {
                while (true)
                {
                    bool has1 = e1.MoveNext();
                    bool has2 = e2.MoveNext();

                    if (!has1 && !has2) break;

                    if (!has1 || !has2)
                    {
                        return false;
                    }

                    if (!Equals(e1.Current, e2.Current, visited, options))
                        return false;
                    index++;
                }
            }
            finally
            {
                (e1 as IDisposable)?.Dispose();
                (e2 as IDisposable)?.Dispose();
            }
            return true;
        }
    }


    private static void AddHash(ref HashCode hash, object? obj, HashSet<object> visited, GbxObjectComparerOptions options)
    {
        // Both null — equal
        if (obj == null) return;

        Type type = obj.GetType();
        hash.Add(type);

        // Primitives, strings, enums, decimals — use Equals directly
        if (type.IsPrimitive || type.IsEnum || obj is string || obj is decimal)
        {
            hash.Add(obj);
            return;
        }


        // Circular reference guard (reference types only)
        if (!type.IsValueType)
        {
            if (!visited.Add(obj)) return;
        }


        // custom comparers
        IGbxStructureComparer? customComparer = null;
        if (!options.Flags.HasFlag(GbxObjectComparerFlags.IgnoreCustomComparers))
        {
            if (options.CustomComparers != null)
            {
                if (options.CustomComparers.TryGetValue(type, out customComparer))
                {
                    if (customComparer.FullReplacement)
                    {
                        customComparer.AddHash(ref hash, obj, visited, options);
                        return;
                    }
                       
                }
            }
            if (customComparer == null)
            {
                if (CustomComparerRegistry.TryGetValue(type, out customComparer))
                    if (customComparer.FullReplacement)
                    {
                        customComparer.AddHash(ref hash, obj, visited, options);
                        return;
                    }
            }
        }

        // Value types that override Equals (e.g. DateTime, Guid, Vector3, etc.)
        // If the type overrides Equals, trust it rather than reflecting into fields
        if (type.IsValueType && type.GetMethod(nameof(GetHashCode))?.DeclaringType != typeof(ValueType))
        {
            hash.Add(obj);
            return;
        }

        // Collections
        if (obj is IEnumerable enum1)
        {
            EnumerableAddHash(ref hash, enum1, visited, options);
            if (IsFrameworkCollection(type))
                return;
        }

        // Reflect fields only (properties are usually computed from fields;
        // reflecting both causes double-reporting and can trigger side-effects)
        if (options.Flags.HasFlag(GbxObjectComparerFlags.PrivateFields))
        {
            var currentType = type;
            while (currentType != null && currentType != typeof(object))
            {
                foreach (var field in currentType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (customComparer != null)
                        customComparer.AddHashField(ref hash, field,  obj, visited, options);
                    else
                        AddHashField(ref hash, field, obj, visited, options);
                }

                currentType = currentType.BaseType;
            }
        }
        else
        {
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                // Skip compiler-generated backing fields of auto-properties — we'll catch
                // those via the property loop below for cleaner path names
                if (field.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false))
                    continue;

                if (customComparer != null)
                    customComparer.AddHashField(ref hash, field, obj, visited, options);
                else
                    AddHashField(ref hash, field, obj, visited, options);
            }
        }

        // Public readable, non-indexed properties
        if (!options.Flags.HasFlag(GbxObjectComparerFlags.PrivateFields))
        {
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!prop.CanRead || prop.GetIndexParameters().Length > 0) continue;

                if (customComparer != null)
                    customComparer.AddHashProperty(ref hash, prop, obj, visited, options);
                else
                    AddHashProperty(ref hash, prop, obj, visited, options);
            }
        }
    }
    public static void AddHashField(ref HashCode hash, FieldInfo field,object obj, HashSet<object> visited, GbxObjectComparerOptions options)
    {
        object? v1 = field.GetValue(obj);
        AddHash(ref hash, v1, visited, options);
    }
    public static void AddHashProperty(ref HashCode hash, PropertyInfo pInfo, object obj, HashSet<object> visited, GbxObjectComparerOptions options)
    {
        object? v1;
        try 
        {
            v1 = pInfo.GetValue(obj); 
            AddHash(ref hash, v1, visited, options); 
        }
        catch (Exception) {  }

    }

    private static void EnumerableAddHash(ref HashCode hash, IEnumerable enumerable, HashSet<object> visited, GbxObjectComparerOptions options)
    {
        if (options.Flags.HasFlag(GbxObjectComparerFlags.IgnoreCollectionOrder))
        {
            List<int> hashes = [];
            foreach (var item in enumerable)
            {
                var h = new HashCode();
                AddHash(ref h, item, visited, options);
                hashes.Add(h.ToHashCode());
            }
            hashes.Sort();
            foreach (var h in hashes)
            {
                hash.Add(h);
            }
        }
        else
        {
            foreach (var item1 in enumerable)
                AddHash(ref hash, item1, visited, options);

        }
    }

    private static bool IsFrameworkCollection(Type type)
    {
        return type.Namespace?.StartsWith("System.Collections") == true;
    }
    public static string GetFieldName(FieldInfo field)
    {
        var name = field.Name;

        if (name.StartsWith("<") && name.EndsWith(">k__BackingField"))
        {
            return name[1..name.IndexOf('>')];
        }

        return name;
    }


    public enum DifferenceType
    {
        Value,
        Type,
        MissingCollectionEntry,
        CollectionDivergence,
        OneIsNull,
        CustomDifference,

    }
    public record struct Difference(string Path1, string Path2, object? Value1, object Value2, DifferenceType Type)
    {
        public override string ToString() => Type switch
        {
            DifferenceType.Value => $"Value -> {Path1} != {Path2}: {Value1} != {Value2}",
            DifferenceType.Type => $"Type -> {Path1} != {Path2}: {Value1} != {Value2}",
            DifferenceType.MissingCollectionEntry => $"Miss -> {Path1} != {Path2}",
            DifferenceType.CollectionDivergence => $"Diverge -> {Path1} != {Path2}: len[{Value1}] != len[{Value2}]",
            DifferenceType.OneIsNull => $"Null -> {Path1} != {Path2}: {Value1} != {Value2}",
            DifferenceType.CustomDifference => $"Custom -> {Path1} != {Path2}: {Value1} != {Value2}",
        };
        public string ShortString(int pathLength = 50) => Type switch
        {
            DifferenceType.Value => $"Value -> {Shorten(Path1, pathLength)} != {Shorten(Path2, pathLength)}: {Value1} != {Value2}",
            DifferenceType.Type => $"Type -> {Shorten(Path1, pathLength)} != {Shorten(Path2, pathLength)}: {Value1} != {Value2}",
            DifferenceType.MissingCollectionEntry => $"Miss -> {Shorten(Path1, pathLength)} != {Shorten(Path2, pathLength)}",
            DifferenceType.CollectionDivergence => $"Diverge -> {Shorten(Path1, pathLength)} != {Shorten(Path2, pathLength)}: len[{Value1}] != len[{Value2}]",
            DifferenceType.OneIsNull => $"Null -> {Shorten(Path1, pathLength)} != {Shorten(Path2, pathLength)}: {Value1} != {Value2}",
            DifferenceType.CustomDifference => $"Custom -> {Shorten(Path1, pathLength)} != {Shorten(Path2, pathLength)}: {Value1} != {Value2}",
        };
        string Shorten(string s, int length) => s.Length <= length ? s : s[^length..];
    }
    public static List<Difference> Compare(object obj1, object obj2, GbxObjectComparerOptions options)
    {
        List<Difference> diffs = [];
        Compare(obj1, obj2, [], "Obj1", "Obj2", options, diffs);
        return diffs;
    }
    public static void PrintCompare(object obj1, object obj2, GbxObjectComparerOptions options, int pathlength = 100)
    {
        var diffs = Compare(obj1, obj2, options);
        foreach (var diff in diffs)
        {
            Console.WriteLine(diff.ShortString(pathlength));
        }
    }

    private static void Compare(object? obj1, object? obj2, HashSet<(object, object)> visited, string path1, string path2, GbxObjectComparerOptions options, List<Difference> differences)
    {
        // Both null — equal
        if (obj1 == null && obj2 == null) return;

        if (obj1 == null || obj2 == null)
        {
            differences.Add(new Difference(path1, path2, obj1, obj2, DifferenceType.OneIsNull));
            return;
        }

        Type type = obj1.GetType();

        if (obj2.GetType() != type)
        {
            differences.Add(new Difference(path1, path2, obj1.GetType().Name, obj2.GetType().Name, DifferenceType.Type));
            return;
        }

        // Circular reference guard (reference types only)
        if (!type.IsValueType)
        {
            if (ReferenceEquals(obj1, obj2)) return;
            if (!visited.Add((obj1, obj2))) return;
        }


        // custom comparers
        IGbxStructureComparer? customComparer = null;
        if (!options.Flags.HasFlag(GbxObjectComparerFlags.IgnoreCustomComparers))
        {
            if (options.CustomComparers != null)
            {
                if (options.CustomComparers.TryGetValue(type, out customComparer))
                {
                    if (customComparer.FullReplacement)
                    {
                        if (!customComparer.Equals(obj1, obj2, visited, options))
                        {
                            differences.Add(new Difference(path1, path2, obj1, obj2, DifferenceType.CustomDifference));
                        }
                        return;
                    }
                }
            }
            if (customComparer == null)
            {
                if (CustomComparerRegistry.TryGetValue(type, out customComparer))
                    if (customComparer.FullReplacement)
                    {
                        if (!customComparer.Equals(obj1, obj2, visited, options))
                        {
                            differences.Add(new Difference(path1, path2, obj1, obj2, DifferenceType.CustomDifference));
                        }
                        return;
                    }
            }
        }


        // Primitives, strings, enums, decimals — use Equals directly
        if (type.IsPrimitive || type.IsEnum || obj1 is string || obj1 is decimal)
        {
            if (!obj1.Equals(obj2))
            {
                differences.Add(new Difference(path1, path2, obj1, obj2, DifferenceType.Value));
            }
            return;
        }




        // Value types that override Equals (e.g. DateTime, Guid, Vector3, etc.)
        // If the type overrides Equals, trust it rather than reflecting into fields
        if (type.IsValueType && type.GetMethod("Equals", new[] { typeof(object) })!.DeclaringType != typeof(ValueType))
        {
            if(!obj1.Equals(obj2))
            {
                differences.Add(new Difference(path1, path2, obj1, obj2, DifferenceType.Value));
            }
            return;
        }

        // Collections
        if (obj1 is IEnumerable enum1 && obj2 is IEnumerable enum2)
        {
            EnumerablCompare(enum1, enum2, visited,path1, path2, options, differences);

            // treat collection fields as equal if they are from System.Collections or System.Collections.Generic namespaces
            if (IsFrameworkCollection(type))
                return;
        }

        // Reflect fields only (properties are usually computed from fields;
        // reflecting both causes double-reporting and can trigger side-effects)
        if (options.Flags.HasFlag(GbxObjectComparerFlags.PrivateFields))
        {
            var currentType = type;
            while (currentType != null && currentType != typeof(object))
            {
                foreach (var field in currentType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    object? v1 = field.GetValue(obj1);
                    object? v2 = field.GetValue(obj2);
                    string fieldName = field.Name;
                    if (fieldName.StartsWith("<") && fieldName.EndsWith(">k__BackingField"))
                        fieldName = fieldName[1..fieldName.IndexOf('>')]; // strip to just PropName
                    string fPath1 = $"{path1}.{fieldName}";
                    string fPath2 = $"{path2}.{fieldName}";

                    if (customComparer != null)
                    {
                        if (!customComparer.EqualsField(field, obj1, obj2, [], options))
                            differences.Add(new Difference(fPath1, fPath2, v1, v2, DifferenceType.CustomDifference));
                        
                    }
                    else
                        Compare(v1, v2, visited, fPath1, fPath2, options, differences);
                    
                }

                currentType = currentType.BaseType;
            }
        }
        else
        {
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                // Skip compiler-generated backing fields of auto-properties — we'll catch
                // those via the property loop below for cleaner path names
                if (field.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false))
                    continue;

                object? v1 = field.GetValue(obj1);
                object? v2 = field.GetValue(obj2);
                string fieldName = field.Name;
                if (fieldName.StartsWith("<") && fieldName.EndsWith(">k__BackingField"))
                    fieldName = fieldName[1..fieldName.IndexOf('>')]; // strip to just PropName
                string fPath1 = $"{path1}.{fieldName}";
                string fPath2 = $"{path2}.{fieldName}";

                if (customComparer != null)
                {
                    if (!customComparer.EqualsField(field, obj1, obj2, [], options))
                        differences.Add(new Difference(fPath1, fPath2, v1, v2, DifferenceType.CustomDifference));
                }
                else
                    Compare(v1, v2, visited, fPath1, fPath2, options, differences);
            }
        }

        // Public readable, non-indexed properties
        if (!options.Flags.HasFlag(GbxObjectComparerFlags.PrivateFields))
        {
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!prop.CanRead || prop.GetIndexParameters().Length > 0) continue;

                object? v1 = prop.GetValue(obj1);
                object? v2 = prop.GetValue(obj2);
                string propName = prop.Name;
                string fPath1 = $"{path1}.{propName}";
                string fPath2 = $"{path2}.{propName}";

                if (customComparer != null)
                {
                    if (!customComparer.EqualsProperty(prop, obj1, obj2, [], options))
                        differences.Add(new Difference(fPath1, fPath2, v1, v2, DifferenceType.CustomDifference));
                }
                else
                    Compare(v1, v2, visited, fPath1, fPath2, options, differences);
            }
        }
        return;
    }

    private static void EnumerablCompare(IEnumerable enum1, IEnumerable enum2, HashSet<(object, object)> visited, string path1, string path2, GbxObjectComparerOptions options, List<Difference> differences)
    {
        if (options.Flags.HasFlag(GbxObjectComparerFlags.IgnoreCollectionOrder))
        {
            var list1 = enum1.Cast<object?>().ToList();
            var list2 = enum2.Cast<object?>().ToList();

            var matchedRight = new bool[list2.Count];
            for (int left = 0; left < list1.Count; left++)
            {
                bool found = false;

                for (int right = 0; right < list2.Count; right++)
                {
                    if (matchedRight[right])
                        continue;

                    if (Equals(list1[left], list2[right], [], options))
                    {
                        matchedRight[right] = true;
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    int candidate = -1;

                    for (int right = 0; right < list2.Count; right++)
                    {
                        if (matchedRight[right])
                            continue;

                        if (list1[left]?.GetType() == list2[right]?.GetType())
                        {
                            candidate = right;
                            break;
                        }
                    }
                    if (candidate >= 0)
                    {
                        matchedRight[candidate] = true;
                        Compare(list1[left], list2[candidate], visited, $"{path1}[{left}]", $"{path2}[{candidate}]", options, differences);
                    }
                    else
                    {
                        differences.Add(new Difference($"{path1}[{left}]", $"{path2}[?]", list1[left], null, DifferenceType.MissingCollectionEntry));
                    }
                }
            }

            for (int right = 0; right < list2.Count; right++)
            {
                if (!matchedRight[right])
                {
                    differences.Add(new Difference($"{path1}[?]", $"{path2}[{right}]", null, list2[right], DifferenceType.MissingCollectionEntry));
                }
            }
        }
        else
        {
            var e1 = enum1.GetEnumerator();
            var e2 = enum2.GetEnumerator();
            int index = 0;
            try
            {
                while (true)
                {
                    bool has1 = e1.MoveNext();
                    bool has2 = e2.MoveNext();

                    if (!has1 && !has2) break;

                    if (!has1 || !has2)
                    {
                        int maxIdx = index;
                        if(has1)
                            while(e1.MoveNext()) { maxIdx++; }
                        else 
                            while(e2.MoveNext()) { maxIdx++; }

                        
                        differences.Add(new Difference($"{path1}[{(has1 ? $">={index}": "?")}]", $"{path2}[{(has2 ? $">={index}" : "?")}]", has1 ? maxIdx : index, has2 ? maxIdx : index, DifferenceType.CollectionDivergence));
                        break;
                    }

                    Compare(e1.Current, e2.Current, visited, $"{path1}[{index}]", $"{path2}[{index}]", options, differences);
                    index++;
                }
            }
            finally
            {
                (e1 as IDisposable)?.Dispose();
                (e2 as IDisposable)?.Dispose();
            }
        }
    }


}
