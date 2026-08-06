using GBX.NET;
using System.Xml.Serialization;

namespace TM_GenericMapping.Items.FbxConverter;

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

    //[XmlElement("Pivots")]
    //public PivotsXml? Pivots { get; set; } = null;

    //public GridSnap GridSnap { get; set; }

    //public Levitation Levitation { get; set; }

    //public PivotSnap PivotSnap { get; set; }

    //public Options Options { get; set; }
}
public sealed class MeshParamsLinkXml
{
    [XmlAttribute("File")]
    public string? File { get; set; } = null;
}
public sealed class WaypointXml
{
    [XmlAttribute("File")]
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

public sealed class PivotsXml
{
    [XmlElement("Pivot")]
    public List<PivotXml> Items { get; set; } = new();
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