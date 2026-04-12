using TM_GenericMapping.Common.IO;

namespace TM_GenericMapping.Common;

public static class TemplateLoader
{
    public static Stream GetTemplate(string fileName)
    {
        var assembly = typeof(TemplateLoader).Assembly;
        var name = $"{assembly.GetName().Name}.Templates.{fileName}";
        return assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Embedded template not found: {name}");
    }
}
