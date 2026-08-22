namespace TM_GenericMapping.Templating;

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public class GbxTemplateDataInjectorAttribute : Attribute
{
    public Type TargetType { get; }
    public GbxTemplateDataInjectorAttribute(Type targetType)
    {
        TargetType = targetType;
    }
}

public static class GbxTemplateInjectorRegistry
{
    private static readonly Dictionary<Type, object> _injectors = new();
    private static bool _initialized = false;

    internal static void RegisterInjectorInternal(Type targetType, object injector)
    {
        _injectors[targetType] = injector;
    }
    internal static void Initialize()
    {

        var injectorTypes = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.GetCustomAttributes(typeof(GbxTemplateDataInjectorAttribute), false).Length > 0);
        foreach (var injectorType in injectorTypes)
        {
            var attribute = (GbxTemplateDataInjectorAttribute)injectorType.GetCustomAttributes(typeof(GbxTemplateDataInjectorAttribute), false).First();
            var targetType = attribute.TargetType;
            var injectorInstance = Activator.CreateInstance(injectorType);
            RegisterInjectorInternal(targetType, injectorInstance);
        }
        _initialized = true;
    }

    public static void RegisterInjector<T>(IGbxTemplateDataInjector<T> injector)
    where T : class
    {
        if (!_initialized)
            Initialize();
        RegisterInjectorInternal(typeof(T), injector);
    }
    public static IGbxTemplateDataInjector<T> GetInjector<T>()
        where T : class
    {
        if (!_initialized)
            Initialize();

        if (_injectors.TryGetValue(typeof(T), out var injector))
            return (IGbxTemplateDataInjector<T>)injector;

        return new ReflectionTemplateDataInjector<T>();
    }

}
