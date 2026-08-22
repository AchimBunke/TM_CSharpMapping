using GBX.NET.Engines.Plug;
using System.Drawing;
using System.Xml.Serialization;
using TM_GenericMapping.Common;

namespace TM_GenericMapping.Items.FbxGbxConversion.Serialization;


[XmlRoot("MeshParams")]
public sealed class MeshParamsXml
{

    [XmlAttribute("MeshType")]
    public string MeshType { get; set; } = "Static";

    [XmlAttribute("Collection")]
    public string Collection { get; set; } = "Stadium";

    [XmlAttribute("Scale")]
    public float Scale { get; set; } = 1f;



    [XmlAttribute("FbxFile")]
    public string? FbxFile { get; set; }

    [XmlArray("Materials")]
    [XmlArrayItem("Material")]
    public List<MaterialXml> Materials { get; set; } = new();

    [XmlArray("Lights")]
    [XmlArrayItem("Light")]
    public List<LightXml> Lights { get; set; } = new();
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

public sealed class LightXml
{

    [XmlAttribute("Name")]
    public string Name { get; set; }

    [XmlAttribute("Type")]
    public LightType Type { get; set; }

    [XmlAttribute("sRGB")]
    public string? ColorValue { get; set; }
    [XmlIgnore]
    public Color Color
    {
        get => ColorValue != null ? ColorUtils.FromRGBHex(ColorValue) : System.Drawing.Color.Black;
        set => ColorValue = value.ToRGBHex();
    }
    [XmlAttribute("Intensity")]
    public float Intensity { get; set; }
    [XmlAttribute("Distance")]
    public float Distance { get; set; }
    [XmlAttribute("NightOnly")]
    public bool NightOnly { get; set; }
    [XmlAttribute("PointEmissionRadius")]
    public float PointEmissionRadius { get; set; }
    [XmlAttribute("PointEmissionLength")]
    public float PointEmissionLength { get; set; }
    [XmlAttribute("SpotInnerAngle")]
    public float SpotInnerAngle { get; set; } = 40;
    [XmlAttribute("SpotOuterAngle")]
    public float SpotOuterAngle { get; set; } = 60;

    [XmlAttribute("SpotEmissionSizeX")]
    public float SpotEmissionSizeX { get; set; }
    [XmlAttribute("SpotEmissionSizeY")]
    public float SpotEmissionSizeY { get; set; }

}
