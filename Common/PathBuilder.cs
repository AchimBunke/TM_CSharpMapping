using System.Numerics;

namespace TM_GenericMapping.Common;

public class PathBuilder
{
    private readonly List<Vector3> _points = new();
    private Vector3 _cursor;
    private bool _recording = true;

    public PathBuilder() : this(Vector3.Zero) { }
    public PathBuilder(Vector3 start)
    {
        _cursor = start;
        _points.Add(_cursor);
    }

    // ── Pen control ──────────────────────────────────────────────────────

    /// Stop emitting points on movement — just move the cursor.
    public PathBuilder PenUp() { _recording = false; return this; }

    /// Resume emitting points on movement.
    public PathBuilder PenDown(bool stamp = false)
    {
        _recording = true;
        if (stamp) _points.Add(_cursor);
        return this;
    }

    /// Emit the current cursor position as a point, regardless of pen state.
    public PathBuilder Stamp() { _points.Add(_cursor); return this; }

    // ── Relative movement ────────────────────────────────────────────────

    public PathBuilder Right(float d) => Move(d, 0, 0);
    public PathBuilder Left(float d) => Move(-d, 0, 0);
    public PathBuilder Up(float d) => Move(0, d, 0);
    public PathBuilder Down(float d) => Move(0, -d, 0);
    public PathBuilder Forward(float d) => Move(0, 0, d);
    public PathBuilder Back(float d) => Move(0, 0, -d);

    public PathBuilder Move(float dx, float dy, float dz = 0)
        => Move(new Vector3(dx, dy, dz));
    public PathBuilder Move(Vector3 d)
    {
        _cursor += d;
        if (_recording) _points.Add(_cursor);
        return this;
    }

    // ── Absolute positioning ─────────────────────────────────────────────

    public PathBuilder To(float x, float y, float z = 0)
        => To(new Vector3(x, y, z));
    public PathBuilder To(Vector3 point)
    {
        _cursor = point;
        if (_recording) _points.Add(_cursor);
        return this;
    }

    public PathBuilder ToX(float x) => To(x, _cursor.Y, _cursor.Z);
    public PathBuilder ToY(float y) => To(_cursor.X, y, _cursor.Z);
    public PathBuilder ToZ(float z) => To(_cursor.X, _cursor.Y, z);

    // ── Close path ───────────────────────────────────────────────────────

    /// Adds the starting point again to close the outline.
    public PathBuilder Close()
    {
        _points.Add(_points[0]);
        return this;
    }

    // ── Output ───────────────────────────────────────────────────────────

    public List<Vector3> Build() => new(_points);

    public Vector3[] BuildArray() => _points.ToArray();
}
