using GBX.NET.Engines.Plug;
using System.Text.RegularExpressions;

namespace TM_GenericMapping.Items.FbxConverter.Serialization;

public class DMaterialLibrary
{
    public Dictionary<string, DMaterial> Materials { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);


}

public class DMaterial
{
    public string Name { get; set; } = "";

    public string LinkFull { get; set; } = "";
    public string SurfaceIdValue { get; set; } = "";
    public CPlugSurface.MaterialId SurfaceId => Enum.Parse<CPlugSurface.MaterialId>(SurfaceIdValue);
    public string GameplayIdValue { get; set; } = "";
    public CPlugMaterialUserInst.GameplayId GameplayId => Enum.Parse<CPlugMaterialUserInst.GameplayId>(GameplayIdValue);
 

    public List<DMaterialUvLayer> UvLayers { get; set; } = [];

    public bool HasTexUvLayer => UvLayers.Any(l => l.Name == "BaseMaterial");
    public bool HasLightmapUvlayer => UvLayers.Any(l => l.Name == "Lightmap");

    public int GetTexChannel => UvLayers.IndexOf(UvLayers.FirstOrDefault(l => l.Name == "BaseMaterial", null)!);
    public int GetLightmapChannel => UvLayers.IndexOf(UvLayers.FirstOrDefault(l => l.Name == "Lightmap", null)!);
    public string? Color0 { get; set; }
    public bool HasColor0 { get; set; }
}

public class DMaterialUvLayer
{
    public string Name { get; set; } = "";
    public int Index { get; set; }
}

public static class DMaterialLibraryParser
{
    private static readonly Regex EntryRegex =
        new(@"(?<key>\w+)\s*\((?<value>.*)\)");

    public static DMaterialLibrary Parse(string text)
    {
        var library = new DMaterialLibrary();
        DMaterial? currentMaterial = null;
        string currentLibraryPrefix = "";

        foreach (var rawLine in text.Split('\n'))
        {
            string line = rawLine.Trim();

            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                continue;

            if (line.StartsWith("DLibrary"))
            {
                currentLibraryPrefix = ExtractValue(line);
                continue;
            }

            if (line.StartsWith("DMaterial"))
            {
                currentMaterial = new DMaterial
                {
                    Name = ExtractValue(line)
                };

                library.Materials[currentMaterial.Name] = currentMaterial;
                continue;
            }

            if (currentMaterial == null)
                continue;

            var match = EntryRegex.Match(line);
            if (!match.Success)
                continue;

            string key = match.Groups["key"].Value;
            string value = match.Groups["value"].Value;

            switch (key)
            {
                case "DLinkFull":
                    currentMaterial.LinkFull = $"{currentLibraryPrefix}\\{value}";
                    break;

                case "DSurfaceId":
                    currentMaterial.SurfaceIdValue = value;
                    break;

                case "DGameplayId":
                    currentMaterial.GameplayIdValue = value;
                    break;

                case "DColor0":
                    currentMaterial.Color0 = value;
                    currentMaterial.HasColor0 = true;
                    break;

                case "DUvLayer":
                    currentMaterial.UvLayers.Add(ParseUvLayer(value));
                    break;
            }
        }

        return library;
    }
    public static DMaterialLibrary Parse(Stream stream)
    {
        using var reader = new StreamReader(stream, leaveOpen: true);
        string text = reader.ReadToEnd();

        return Parse(text);
    }


    private static DMaterialUvLayer ParseUvLayer(string value)
    {
        var parts = value.Split(',', 2, StringSplitOptions.TrimEntries);

        return new DMaterialUvLayer
        {
            Name = parts[0],
            Index = parts.Length > 1 && int.TryParse(parts[1], out var index)
                ? index
                : 0
        };
    }

    private static string ExtractValue(string line)
    {
        int start = line.IndexOf('(');
        int end = line.LastIndexOf(')');

        return line.Substring(start + 1, end - start - 1);
    }
}
