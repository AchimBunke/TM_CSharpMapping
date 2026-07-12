using GBX.NET.Engines.MwFoundations;
using GBX.NET.Serialization.Chunking;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace TM_GenericMapping.Common;

public static class ObjectCloner
{
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
