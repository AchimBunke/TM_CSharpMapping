namespace TM_GenericMapping.Templating;

public class GbxTemplate<T>(T Value)
  where T : class
{
    public T Value { get; } = Value;

    /// <summary>
    /// Updates this template's value with the data from the source object using the registered injector for type T.
    /// </summary>
    /// <param name="source">The source object from which to inject data.</param>
    /// <returns>The updated template instance.</returns>
    public GbxTemplate<T> InjectData(T source)
    {
        GbxTemplateInjectorRegistry.GetInjector<T>().InjectData(source, Value);
        return this;
    }

    public static implicit operator T(GbxTemplate<T> template) => template.Value;

}
