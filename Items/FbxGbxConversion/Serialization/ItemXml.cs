using GBX.NET;
using System.Xml.Serialization;
using static GBX.NET.Engines.GameData.CGameItemModel;

namespace TM_GenericMapping.Items.FbxGbxConversion.Serialization;


[XmlRoot("Item")]
public sealed class ItemXml
{
    [XmlAttribute("Type")]
    public string Type { get; set; } = "StaticObject";

    [XmlAttribute("Collection")]
    public string Collection { get; set; } = "Stadium";

    [XmlAttribute("AuthorName")]
    public string AuthorName { get; set; } = string.Empty;

    [XmlElement("MeshParamsLink")]
    public MeshParamsLinkXml MeshParamsLink { get; set; } = new MeshParamsLinkXml();

    [XmlElement("Waypoint")]
    public WaypointXml? Waypoint { get; set; } = null;

    [XmlArray("Pivots")]
    [XmlArrayItem("Pivot")]
    public List<PivotXml>? Pivots { get; set; } = new List<PivotXml>();

    [XmlElement("GridSnap")]
    public GridSnapXml? GridSnap { get; set; }

    [XmlElement("Levitation")]
    public LevitationXml? Levitation { get; set; }

    [XmlElement("PivotSnap")]
    public PivotSnapXml? PivotSnap { get; set; }

    [XmlElement("Options")]
    public OptionsXml? Options { get; set; }

}
public sealed class MeshParamsLinkXml
{
    [XmlAttribute("File")]
    public string? File { get; set; } = null;
}
public sealed class WaypointXml
{
    [XmlAttribute("Type")]
    public WaypointTypeXml Type { get; set; } = WaypointTypeXml.Checkpoint;
}
public enum WaypointTypeXml
{
    [XmlEnum("Start")]
    Start,

    [XmlEnum("Checkpoint")]
    Checkpoint,

    [XmlEnum("Finish")]
    Finish,

    [XmlEnum("StartFinish")]
    StartFinish
}
public sealed class PivotSnapXml
{
    [XmlAttribute("Distance")]
    public float Distance { get; set; }
}
public sealed class PivotXml
{
    [XmlAttribute("Pos")]
    public string Position { get; set; } = "";
    public void SetPosition(Vec3 pos)=> Position = $"{pos.X} {pos.Y} {pos.Z}";
    public Vec3 GetPosition()
    {
        var parts = Position.Split(' ');
        return new Vec3(
            float.TryParse(parts[0], out float x) ? x : 0f,
            float.TryParse(parts[1], out float y) ? y : 0f,
            float.TryParse(parts[2], out float z) ? z : 0f
        );
    }
}
public sealed class GridSnapXml
{
    [XmlAttribute("HStep")]
    public float HStep { get; set; }

    [XmlAttribute("HOffset")]
    public float HOffset { get; set; }

    [XmlAttribute("VStep")]
    public float VStep { get; set; }
    [XmlAttribute("VOffset")]
    public float VOffset { get; set; }
}
public sealed class LevitationXml
{

    [XmlAttribute("VStep")]
    public float VStep { get; set; }
    [XmlAttribute("VOffset")]
    public float VOffset { get; set; }

    [XmlAttribute("GhostMode")]
    public string GhostModeValue { get; set; } = string.Empty;

    [XmlIgnore]
    public bool GhostMode
    {
        get => bool.Parse(GhostModeValue);
        set => value.ToString();
    }
}
public sealed class OptionsXml
{
    [XmlAttribute("NotOnItem")]
    public string NotOnItemValue { get; set; } = string.Empty;
    [XmlIgnore]
    public bool NotOnItem
    {
        get => bool.Parse(NotOnItemValue);
        set => value.ToString();
    }

    [XmlAttribute("OneAxisRotation")]
    public string OneAxisRotationValue { get; set; } = string.Empty;
    [XmlIgnore]
    public bool OneAxisRotation
    {
        get => bool.Parse(OneAxisRotationValue);
        set => value.ToString();
    }
    [XmlAttribute("ManualPivotSwitch")]
    public string ManualPivotSwitchValue { get; set; } = string.Empty;
    [XmlIgnore]
    public bool ManualPivotSwitch
    {
        get => bool.Parse(ManualPivotSwitchValue);
        set => value.ToString();
    }
    [XmlAttribute("AutoRotation")]
    public string AutoRotationValue { get; set; } = string.Empty;
    [XmlIgnore]
    public bool AutoRotation
    {
        get => bool.Parse(AutoRotationValue);
        set => value.ToString();
    }
}