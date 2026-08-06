using GBX.NET.Engines.Plug;
using System.Drawing;
using System.Xml.Serialization;
using TM_GenericMapping.Common;

namespace TM_GenericMapping.Items.FbxConverter;

[XmlRoot("MeshParams")]
public sealed class MeshParamsXml
{
    [XmlIgnore]
    public string FilePath { get; set; } = string.Empty;

    [XmlAttribute("MeshType")]
    public string MeshType { get; set; } = "Static";

    [XmlAttribute("Collection")]
    public string Collection { get; set; } = "Stadium";

    [XmlAttribute("Scale")]
    public float ScaleValue { get; set; }

    [XmlIgnore]
    public float? Scale
    {
        get => ScaleSpecified ? ScaleValue : null;
        set
        {
            ScaleSpecified = value.HasValue;
            ScaleValue = value ?? 0;
        }
    }

    [XmlIgnore]
    public bool ScaleSpecified { get; set; }

    [XmlAttribute("FbxFile")]
    public string? FbxFile { get; set; }

    [XmlElement("Materials")]
    public MaterialsXml Materials { get; set; } = new();
}

public sealed class MaterialsXml
{
    [XmlElement("Material")]
    public List<MaterialXml> Items { get; set; } = new();
}
public sealed class MaterialXml
{
    [XmlAttribute("Name")]
    public string Name { get; set; } = "";

    [XmlAttribute("Link")]
    public string Link { get; set; } = "";

    [XmlAttribute("Color")]
    public string? ColorValue { get; set; }
    [XmlIgnore]
    public Color Color
    {
        get => ColorValue != null ? ColorUtils.FromRGBHex(ColorValue) : System.Drawing.Color.Black;
        set => ColorValue = value.ToRGBHex();
    }
    public bool OverrideColor => ColorValue != null;

    [XmlAttribute("PhysicsId")]
    public string? PhysicsIdValue { get; set; }
    public CPlugSurface.MaterialId PhysicsId
    {
        get => Enum.TryParse(typeof(CPlugSurface.MaterialId), PhysicsIdValue, out var result) ? (CPlugSurface.MaterialId)result : CPlugSurface.MaterialId.Concrete;
        set => PhysicsIdValue = value.ToString();
    }
    public bool OverridePhysicsId => PhysicsIdValue != null;

    [XmlAttribute("GameplayId")]
    public string? GameplayIdValue { get; set; }
    public CPlugMaterialUserInst.GameplayId GameplayId
    {
        get => Enum.TryParse(typeof(CPlugMaterialUserInst.GameplayId), GameplayIdValue, out var result) ? (CPlugMaterialUserInst.GameplayId)result : CPlugMaterialUserInst.GameplayId.None;
        set => GameplayIdValue = value.ToString();
    }
    public bool OverrideGameplayId => GameplayIdValue != null;
}