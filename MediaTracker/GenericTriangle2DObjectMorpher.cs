using GBX.NET;
using System.Numerics;
using System.Security.Cryptography;
using TM_GenericMapping.Common;

namespace TM_GenericMapping.MediaTracker;

public class GenericTriangle2DObjectMorpher
{
    Vector3[] sourceVertices;
    Int3[] sourceTriangles;
    Vector3[] targetVertices;
    Int3[] targetTriangles;
    IMorphable source;
    IMorphable target;

    public Easing Easing { get; init; }
    public GenericTriangle2DObjectMorpher(IMorphable source, IMorphable target, Easing easing)
    {
        ExceptionUtils.Ensure(CanMorph(source, target), () => new InvalidOperationException($"Cannot morph {source} in {target}"));
        sourceTriangles = source.Triangles.Slice(0, source.FillTrianglesCount).ToArray();
        sourceVertices = source.Vertices.Slice(0, source.FillVertexCount).ToArray();
        targetTriangles = target.Triangles.Slice(0, target.FillTrianglesCount).ToArray();
        targetVertices = target.Vertices.Slice(0, target.FillVertexCount).ToArray();
        this.source = source;
        this.target = target;
        Easing = easing;
    }
    public void Morph(float percent)
    {
        Morph(sourceVertices, sourceTriangles, source.VertexIdxOffset, targetVertices, targetTriangles, target.VertexIdxOffset, percent, Easing)
            .CopyTo(source.Vertices);
    }
    public static bool CanMorph(IMorphable source, IMorphable target) => 
        source.CanMorph() && 
        target.CanMorph() &&
        source.FillTrianglesCount >= target.FillTrianglesCount;

    public static bool CanMorph(ReadOnlySpan<Int3> sourceTriangles, ReadOnlySpan<Int3> targetTriangles)
        => sourceTriangles.Length >= targetTriangles.Length;

    public static Vector3[] Morph(
       ReadOnlySpan<Vector3> sourceVertices,
       ReadOnlySpan<Int3> sourceTriangles,
       int sourceVertexIdxOffset,
       Vector3 sourceCenter,
       ReadOnlySpan<Vector3> targetVertices,
       ReadOnlySpan<Int3> targetTriangles,
       int targetVertexIdxOffset,
       Vector3 targetCenter,
       float percent,
       Easing easing)
    {
        ExceptionUtils.Ensure(CanMorph(sourceTriangles, targetTriangles), () => new InvalidOperationException($"Triangle mismatch"));

        Vector3[] morphedVertices = new Vector3[sourceVertices.Length];

        foreach (var mappedTriangle in MapTrianglesMinimizeMovement(sourceVertices, sourceTriangles, sourceVertexIdxOffset, targetVertices, targetTriangles, targetVertexIdxOffset))
        {
            var sourceTriangleIdx = mappedTriangle.Key;
            var targetTriangleIdx = mappedTriangle.Value;
            var sourceTriangle = sourceTriangles[sourceTriangleIdx];

            if (targetTriangleIdx >= 0)
            {
                var targetTriangle = targetTriangles[targetTriangleIdx];

                morphedVertices[sourceTriangle.X - sourceVertexIdxOffset] = EasingUtils.Ease(sourceVertices[sourceTriangle.X - sourceVertexIdxOffset], targetVertices[targetTriangle.X - targetVertexIdxOffset], percent, easing);
                morphedVertices[sourceTriangle.Y - sourceVertexIdxOffset] = EasingUtils.Ease(sourceVertices[sourceTriangle.Y - sourceVertexIdxOffset], targetVertices[targetTriangle.Y - targetVertexIdxOffset], percent, easing);
                morphedVertices[sourceTriangle.Z - sourceVertexIdxOffset] = EasingUtils.Ease(sourceVertices[sourceTriangle.Z - sourceVertexIdxOffset], targetVertices[targetTriangle.Z - targetVertexIdxOffset], percent, easing);
            }
            else // what to do with excess triangles?
            {

                morphedVertices[sourceTriangle.X - sourceVertexIdxOffset] = EasingUtils.Ease(sourceVertices[sourceTriangle.X - sourceVertexIdxOffset], targetCenter, percent, easing);
                morphedVertices[sourceTriangle.Y - sourceVertexIdxOffset] = EasingUtils.Ease(sourceVertices[sourceTriangle.Y - sourceVertexIdxOffset], targetCenter, percent, easing);
                morphedVertices[sourceTriangle.Z - sourceVertexIdxOffset] = EasingUtils.Ease(sourceVertices[sourceTriangle.Z - sourceVertexIdxOffset], targetCenter, percent, easing);
            }
        }
        return morphedVertices;
    }

      public static Vector3[] Morph(
        ReadOnlySpan<Vector3> sourceVertices,
        ReadOnlySpan<Int3> sourceTriangles,
        int sourceVertexIdxOffset,
        ReadOnlySpan<Vector3> targetVertices,
        ReadOnlySpan<Int3> targetTriangles,
        int targetVertexIdxOffset,
        float percent,
        Easing easing)
    {
        // collapse to first vertex of triangle and move to center? random?
        var sourceCenter = ShapeUtils.GetCentroid(sourceVertices);
        var targetCenter = ShapeUtils.GetCentroid(targetVertices);
        return Morph(sourceVertices, sourceTriangles, sourceVertexIdxOffset, sourceCenter, targetVertices, targetTriangles, targetVertexIdxOffset, targetCenter, percent, easing);
    }

    public static void Morph(IMorphable source, IMorphable target, Easing easing, float percent = 1f)
    {
        ExceptionUtils.Ensure(CanMorph(source, target), () => new InvalidOperationException($"Cannot morph {source} in {target}"));
        Morph(source.Vertices.Slice(0, source.FillVertexCount).ToArray(),
            source.Triangles.Slice(0, source.FillTrianglesCount).ToArray(),
            source.VertexIdxOffset,
            target.Vertices.Slice(0, target.FillVertexCount).ToArray(), 
            target.Triangles.Slice(0, target.FillTrianglesCount).ToArray(),
            target.VertexIdxOffset,
            percent,
            easing)
            .CopyTo(source.Vertices);
    }

    static Dictionary<int, int> MapTriangles(ReadOnlySpan<Vector3> sourceVertices,
        ReadOnlySpan<Int3> sourceTriangles,
        ReadOnlySpan<Vector3> targetVertices,
        ReadOnlySpan<Int3> targetTriangles)
    {
        Dictionary<int, int> mappedTriangles = [];
        for (int i = 0; i < sourceTriangles.Length; ++i)
        {
            if (i >= targetTriangles.Length)
            {
                mappedTriangles[i] = -1;
            }
            else
            {
                mappedTriangles[i] = i;
            }
        }
        return mappedTriangles;
    }
    static Dictionary<int, int> MapTrianglesMinimizeMovement(
        ReadOnlySpan<Vector3> sourceVertices,
        ReadOnlySpan<Int3> sourceTriangles,
        int sourceVertexIdxOffset,
        ReadOnlySpan<Vector3> targetVertices,
        ReadOnlySpan<Int3> targetTriangles,
        int targetVertexIdxOffset)
    {
        Dictionary<int, int> mappedTriangles = [];
        Vector3[] sourceCentroids = new Vector3[sourceTriangles.Length];
        Vector3[] targetCentroids = new Vector3[targetTriangles.Length];
        for (int i = 0; i < sourceTriangles.Length; ++i)
        {
            var sourceTriangle = sourceTriangles[i];
            sourceCentroids[i] = ShapeUtils.GetCentroid(
                [sourceVertices[sourceTriangle.X - sourceVertexIdxOffset], sourceVertices[sourceTriangle.Y - sourceVertexIdxOffset], sourceVertices[sourceTriangle.Z - sourceVertexIdxOffset]]);
        }
        for (int i = 0; i < targetTriangles.Length; ++i)
        {
            var targetTriangle = targetTriangles[i];
            targetCentroids[i] = ShapeUtils.GetCentroid([targetVertices[targetTriangle.X - targetVertexIdxOffset], targetVertices[targetTriangle.Y - targetVertexIdxOffset], targetVertices[targetTriangle.Z - targetVertexIdxOffset]]);
        }
        for (int i = 0; i < targetTriangles.Length; i++)
        {
            var t1 = targetVertices[targetTriangles[i].X - targetVertexIdxOffset];
            var t2 = targetVertices[targetTriangles[i].Y - targetVertexIdxOffset];
            var t3 = targetVertices[targetTriangles[i].Z - targetVertexIdxOffset];

            int closestAvailableSourceTriangleIdx = -1;
            float closestDistance = float.MaxValue;
            for (int j = 0; j < sourceTriangles.Length; j++)
            {
                if (mappedTriangles.ContainsKey(j))
                    continue;
                //float distance = (targetCentroids[i] - sourceCentroids[j]).LengthSquared();
                var s1 = sourceVertices[sourceTriangles[j].X - sourceVertexIdxOffset];
                var s2 = sourceVertices[sourceTriangles[j].Y - sourceVertexIdxOffset];
                var s3 = sourceVertices[sourceTriangles[j].Z - sourceVertexIdxOffset];
         
                float distance = Vector3.DistanceSquared(s1,t1)
                    + Vector3.DistanceSquared(s2, t2)
                    + Vector3.DistanceSquared(s3, t3);
                if (distance < closestDistance)
                {
                    closestAvailableSourceTriangleIdx = j;
                    closestDistance = distance;
                }
            }
            mappedTriangles[closestAvailableSourceTriangleIdx] = i;
        }
        for (int i = 0; i < sourceTriangles.Length; ++i)
        {
            if (!mappedTriangles.ContainsKey(i))
                mappedTriangles[i] = -1;
        }

        return mappedTriangles;
    }

    public static void MorphCharacter(DotMatrixDisplay.DotMatrixCharacter source, DotMatrixDisplay.DotMatrixCharacter target, Easing easing = Easing.Linear, float percent = 1f)
    {
        for (int x = 0; x < source.Dots.GetLength(0); ++x)
        {
            for (int y = 0; y < source.Dots.GetLength(1); ++y)
            {
                Morph(source.Dots[x, y], target.Dots[x, y], easing, percent);
            }
        }
    }

    record HierarchyRange(IMorphable Node, int VStart, int VCount, int TStart, int TCount);
    public static void HierarchicMorph(IMorphable source, IMorphable target, Easing easing, float percent = 1f)
    {
        ExceptionUtils.Ensure(CanHierarchicMorph(source, target), () => new InvalidOperationException($"Triangle mismatch"));

        var flattenedSourceObjects = GetFlattenedHierarchyObjects(source).OfType<TriangleObject>();
        var flattenedSource = ShapeUtils.Merge(flattenedSourceObjects);
        List<HierarchyRange> ranges = [];
        int tOffset = 0;
        int vOffset = 0;
        foreach(var o in flattenedSourceObjects)
        {
            ranges.Add(new(o, vOffset, o.FillVertexCount, tOffset, o.FillTrianglesCount));
            tOffset += o.FillTrianglesCount;
            vOffset += o.FillVertexCount;
        }

        var flattenedTargetObjects = GetFlattenedHierarchyObjects(target).OfType<TriangleObject>();
        var flattenedTarget = ShapeUtils.Merge(flattenedTargetObjects);

        Morph(flattenedSource, flattenedTarget, easing, percent);

        foreach(var range in ranges)
        {
            for (int i = 0; i < range.VCount; ++i)
            {
                var flattenedIdx = range.VStart + i;
                range.Node.Vertices.Span[i] = flattenedSource.Vertices[flattenedIdx];
            }

        }
    }
    public static bool CanHierarchicMorph(IMorphable source, IMorphable target)
    {
        return GetFlattenedHierarchyObjects(source).All(s => s.CanMorph()) && GetFlattenedHierarchyObjects(target).All(s => s.CanMorph());
    }

    static IEnumerable<IMorphable> GetFlattenedHierarchyObjects(IMorphable obj)
    {
        if (obj.FillVertexCount > 0)
            return [obj, .. obj.SubObjects.SelectMany(GetFlattenedHierarchyObjects)];
        else
            return obj.SubObjects.SelectMany(GetFlattenedHierarchyObjects);
    }

}
