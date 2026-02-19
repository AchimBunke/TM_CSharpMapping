using System.Collections;
using System.Reflection;
using System.Text;

namespace TM_GenericMapping.Common
{
    /// <summary>
    /// Memberwise comparison of arbitrary objects using reflection 
    /// </summary>
    public static class ObjectComparer
    {
        public enum Comparison
        {
            Structural,
            Logical
        }
        public static List<string> GetDifferences<T>(T obj1, T obj2, Comparison comparison = Comparison.Structural)
        {
            var differences = new List<string>();
            AreEqualRecursive(obj1, obj2, new HashSet<(object, object)>(), differences, "root", comparison);
            return differences;
        }

        public static void WriteToConsole(List<string> differences)
        {
            StringBuilder sb = new StringBuilder();

            sb.AppendLine($"---- Object Comparer Differences: {differences.Count} ----");
            foreach(var dif in differences)
            {
                sb.AppendLine(dif);
            }
            sb.AppendLine($"----------------------------------------");
            Console.WriteLine(sb.ToString());
        }

        private static bool AreEqualRecursive(object obj1, object obj2, HashSet<(object, object)> visited, List<string> differences, string path, Comparison comparison = Comparison.Structural)
        {
            //Console.WriteLine($"Checking: {path}");
            int initialDifferencesCount = differences.Count;
            if (ReferenceEquals(obj1, obj2)) return true;
            if (obj1 == null || obj2 == null)
            {
                differences.Add($"{path}: One is null, other is not");
                return false;
            }
            if (obj1.GetType() != obj2.GetType())
            {
                differences.Add($"{path}: Type mismatch ({obj1.GetType()} vs {obj2.GetType()})");
                return false;
            }
            // Use Equals method for all types
            if (obj1.GetType().IsPrimitive || obj1 is string)
            {
                if (!obj1.Equals(obj2))
                {
                    differences.Add($"{path}: {obj1} != {obj2}");
                    return false;
                }
                return true;
            }

            Type type = obj1.GetType();

            // Handle IEquatable<T>
            var equatableType = typeof(IEquatable<>).MakeGenericType(type);
            if (equatableType.IsAssignableFrom(type))
            {
                var equalsMethod = equatableType.GetMethod("Equals", new[] { type });
                var result = (bool)equalsMethod.Invoke(obj1, new[] { obj2 });
                if (!result)
                {
                    differences.Add($"{path}: {obj1} != {obj2} | (IEquatable)");
                    return false;
                }
                return true;
            }


            // Prevent circular references
            if (visited.Contains((obj1, obj2))) return true;
            visited.Add((obj1, obj2));


            // Compare fields
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                object value1 = field.GetValue(obj1);
                object value2 = field.GetValue(obj2);
                if (!CompareCollections(value1, value2, visited, differences, $"{path}.{field.Name}", comparison) && !AreEqualRecursive(value1, value2, visited, differences, $"{path}.{field.Name}", comparison))
                {
                    //differences.Add($"{path}.{field.Name}: {value1} != {value2} | (Nested)");
                }
            }

            // Compare properties
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (!property.CanRead) continue;
                if (comparison == Comparison.Structural)
                    continue; // skip computed/custom properties on structural comparisons
                if (property.GetIndexParameters().Length > 0)
                {
                    // Handle indexers
                    for (int i = 0; i < 10; i++) // Arbitrary limit to avoid excessive looping
                    {
                        try
                        {
                            object value1 = property.GetValue(obj1, new object[] { i });
                            object value2 = property.GetValue(obj2, new object[] { i });
                            if (!AreEqualRecursive(value1, value2, visited, differences, $"{path}.{property.Name}[{i}]", comparison) && value1 != null && value2 != null)
                            {
                                //differences.Add($"{path}.{property.Name}[{i}]: {value1} != {value2} | (Nested)");
                            }
                        }
                        catch
                        {
                            break; // Stop checking indexers if out of range
                        }
                    }
                }
                else
                {
                    object value1 = property.GetValue(obj1);
                    object value2 = property.GetValue(obj2);
                    if (!CompareCollections(value1, value2, visited, differences, $"{path}.{property.Name}", comparison) && !AreEqualRecursive(value1, value2, visited, differences, $"{path}.{property.Name}", comparison))
                    {
                        //differences.Add($"{path}.{property.Name}: {value1} != {value2} | (Nested)");
                    }
                }
            }

            return differences.Count == initialDifferencesCount;
        }

        static bool IsAutoPropertyBackingField(FieldInfo field)
        {
            return field.Name.Contains("k__BackingField") &&
                   field.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), inherit: false);
        }
        static bool IsAutoProperty(PropertyInfo prop)
        {
            var backingFieldName = $"<{prop.Name}>k__BackingField";
            return prop.DeclaringType
                       .GetField(backingFieldName, BindingFlags.Instance | BindingFlags.NonPublic) != null;
        }

        private static bool CompareCollections(object obj1, object obj2, HashSet<(object, object)> visited, List<string> differences, string path, Comparison comparison = Comparison.Structural)
        {
            if (obj1 is IEnumerable enumerable1 && obj2 is IEnumerable enumerable2 && obj1 is not string)
            {
                var enumerator1 = enumerable1.GetEnumerator();
                var enumerator2 = enumerable2.GetEnumerator();
                int index = 0;
                while (enumerator1.MoveNext() && enumerator2.MoveNext())
                {
                    if (!AreEqualRecursive(enumerator1.Current, enumerator2.Current, visited, differences, $"{path}[{index}]", comparison) && enumerator1.Current != null && enumerator2.Current != null)
                    {
                        //differences.Add($"{path}[{index}]: {enumerator1.Current} != {enumerator2.Current}");
                    }
                    index++;
                }
                if (enumerator1.MoveNext() || enumerator2.MoveNext())
                {
                    differences.Add($"{path}: Collection length mismatch");
                    return false;
                }
                return true;
            }
            return false;
        }
    }
}
