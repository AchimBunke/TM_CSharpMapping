using GBX.NET.Engines.GameData;
using System.Reflection;
using TM_GenericMapping.Common;

namespace TM_GenericMapping.Templating;

public interface IGbxTemplateDataInjector<T>
    where T : class
{
    void InjectData(T source, T target);
}

public class ReflectionTemplateDataInjector<T> : IGbxTemplateDataInjector<T>
    where T : class
{
    public void InjectData(T source, T target)
    {
        InjectObject(source, target);
    }
    private void InjectObject(object source, object target)
    {
        var sourceType = source.GetType();
        var targetType = target.GetType();

        foreach (var sourceField in GetFields(sourceType))
        {
            var targetField = FindField(targetType, sourceField);

            if (targetField == null)
                continue;

            var sourceValue = sourceField.GetValue(source);

            if (sourceValue == null)
            {
                targetField.SetValue(target, null);
                continue;
            }

            if (IsSimple(sourceField.FieldType))
            {
                targetField.SetValue(target, sourceValue);
                continue;
            }

            var targetValue = targetField.GetValue(target);

            if (targetValue == null)
                continue; // template has no object to populate

            targetField.SetValue(target, ObjectCloner.DeepCloneObject(sourceValue));
        }
    }
    private static IEnumerable<FieldInfo> GetFields(Type type)
    {
        for (var current = type;
             current != null;
             current = current.BaseType)
        {
            foreach (var field in current.GetFields(
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic))
            {
                if (!field.IsStatic)
                    yield return field;
            }
        }
    }
    private static FieldInfo? FindField(
        Type targetType,
        FieldInfo sourceField)
    {
        for (var current = targetType;
             current != null;
             current = current.BaseType)
        {
            var field = current.GetField(
                sourceField.Name,
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic);

            if (field != null)
                return field;
        }

        return null;
    }
    private static bool IsSimple(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;

        return type.IsPrimitive
            || type.IsEnum
            || type == typeof(string)
            || type == typeof(decimal)
            || type == typeof(DateTime)
            || type == typeof(Guid);
    }
}

