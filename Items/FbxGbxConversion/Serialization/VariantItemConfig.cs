using System.Text.Json.Serialization;

namespace TM_GenericMapping.Items.FbxGbxConversion.Serialization;

public class VariantItemConfig
{
    [JsonPropertyName("Name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("Tags")]
    public Dictionary<string, string> Tags { get; set; } = new();

    [JsonPropertyName("HiddenInManualCycle")]
    public bool HiddenInManualCycle { get; set; }
}
