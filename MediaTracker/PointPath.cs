using System.Collections;
using System.Numerics;

namespace TM_GenericMapping.MediaTracker;

public interface IPointPath
{
    ReadOnlySpan<Vector3> GetPoints();
}
public class PointPath : ICollection<Vector3> ,IPointPath
{
    List<Vector3> points;
    public PointPath(params ReadOnlySpan<Vector3> points)
    {
        this.points = points.ToArray().ToList();
    }

    public int Count => ((ICollection<Vector3>)points).Count;

    public bool IsReadOnly => ((ICollection<Vector3>)points).IsReadOnly;

    public void Add(Vector3 item)
    {
        ((ICollection<Vector3>)points).Add(item);
    }

    public void Clear()
    {
        ((ICollection<Vector3>)points).Clear();
    }

    public bool Contains(Vector3 item)
    {
        return ((ICollection<Vector3>)points).Contains(item);
    }

    public void CopyTo(Vector3[] array, int arrayIndex)
    {
        ((ICollection<Vector3>)points).CopyTo(array, arrayIndex);
    }

    public IEnumerator<Vector3> GetEnumerator()
    {
        return ((IEnumerable<Vector3>)points).GetEnumerator();
    }

    public ReadOnlySpan<Vector3> GetPoints()
    {
        return points.ToArray();
    }

    public bool Remove(Vector3 item)
    {
        return ((ICollection<Vector3>)points).Remove(item);
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return ((IEnumerable)points).GetEnumerator();
    }
}
