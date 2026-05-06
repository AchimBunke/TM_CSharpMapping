using System.Collections;
using System.Reflection;
using System.Text;

namespace TM_GenericMapping.Common;

public static class ObjectComparer
{
    [Flags]
    public enum ObjectComparerFlags
    {
        None = 0,
        PrivateFields = 1,
    }
    public static List<string> GetDifferences<T>(T obj1, T obj2, ObjectComparerFlags flags = ObjectComparerFlags.None)
    {
        var differences = new List<string>();
        Compare(obj1, obj2, new HashSet<(object, object)>(ReferenceEqualityComparer), differences, "root", flags);
        return differences;
    }

    private static readonly IEqualityComparer<(object, object)> ReferenceEqualityComparer =
        EqualityComparer<(object, object)>.Create(
            (a, b) => ReferenceEquals(a.Item1, b.Item1) && ReferenceEquals(a.Item2, b.Item2),
            a => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(a.Item1)
               ^ System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(a.Item2)
        );

    private static void Compare(object obj1, object obj2, HashSet<(object, object)> visited, List<string> differences, string path, ObjectComparerFlags flags)
    {
        // Both null — equal
        if (obj1 == null && obj2 == null) return;

        if (obj1 == null || obj2 == null)
        {
            differences.Add($"{path}: one is null, other is {(obj1 ?? obj2)}");
            return;
        }

        Type type = obj1.GetType();

        if (obj2.GetType() != type)
        {
            differences.Add($"{path}: type mismatch ({type.Name} vs {obj2.GetType().Name})");
            return;
        }

        // Primitives, strings, enums, decimals — use Equals directly
        if (type.IsPrimitive || type.IsEnum || obj1 is string || obj1 is decimal)
        {
            if (!obj1.Equals(obj2))
                differences.Add($"{path}: {obj1} != {obj2}");
            return;
        }

        // Value types that override Equals (e.g. DateTime, Guid, Vector3, etc.)
        // If the type overrides Equals, trust it rather than reflecting into fields
        if (type.IsValueType && type.GetMethod("Equals", new[] { typeof(object) })!.DeclaringType != typeof(ValueType))
        {
            if (!obj1.Equals(obj2))
                differences.Add($"{path}: {obj1} != {obj2}");
            return;
        }

        // Circular reference guard (reference types only)
        if (!type.IsValueType)
        {
            if (ReferenceEquals(obj1, obj2)) return;
            if (!visited.Add((obj1, obj2))) return;
        }

        // Collections
        if (obj1 is IEnumerable enum1 && obj2 is IEnumerable enum2)
        {
            CompareEnumerables(enum1, enum2, visited, differences, path, flags);
            return; // don't also reflect into collection fields
        }

        // Reflect fields only (properties are usually computed from fields;
        // reflecting both causes double-reporting and can trigger side-effects)
        if (flags.HasFlag(ObjectComparerFlags.PrivateFields))
        {
            var currentType = type;
            while (currentType != null && currentType != typeof(object))
            {
                foreach (var field in currentType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    object v1 = field.GetValue(obj1);
                    object v2 = field.GetValue(obj2);

                    // Use the property name for compiler-generated backing fields (<PropName>k__BackingField)
                    // so the path is readable
                    string fieldName = field.Name;
                    if (fieldName.StartsWith("<") && fieldName.EndsWith(">k__BackingField"))
                        fieldName = fieldName[1..fieldName.IndexOf('>')]; // strip to just PropName

                    Compare(v1, v2, visited, differences, $"{path}.{fieldName}", flags);
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

                object v1 = field.GetValue(obj1);
                object v2 = field.GetValue(obj2);
                Compare(v1, v2, visited, differences, $"{path}.{field.Name}", flags);
            }
        }

        // Public readable, non-indexed properties
        if (!flags.HasFlag(ObjectComparerFlags.PrivateFields))
        {
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!prop.CanRead || prop.GetIndexParameters().Length > 0) continue;

                object v1, v2;
                try { v1 = prop.GetValue(obj1); }
                catch (Exception ex) { differences.Add($"{path}.{prop.Name}: could not read (obj1): {ex.Message}"); continue; }
                try { v2 = prop.GetValue(obj2); }
                catch (Exception ex) { differences.Add($"{path}.{prop.Name}: could not read (obj2): {ex.Message}"); continue; }

                Compare(v1, v2, visited, differences, $"{path}.{prop.Name}", flags);
            }
        }
    }

    private static void CompareEnumerables(IEnumerable enum1, IEnumerable enum2, HashSet<(object, object)> visited, List<string> differences, string path, ObjectComparerFlags flags)
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
                    differences.Add($"{path}: length mismatch (diverges at index {index})");
                    break;
                }

                Compare(e1.Current, e2.Current, visited, differences, $"{path}[{index}]", flags);
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
