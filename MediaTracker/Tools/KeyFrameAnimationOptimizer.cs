using GBX.NET.Engines.Game;
using System.Numerics;

namespace TM_GenericMapping.MediaTracker.Tools;

public class KeyFrameAnimationOptimizer
{
    public enum ErrorMetric
    {
        Max,
        RootMeanSquare
    }
    public struct OptimizerSettings
    {
        public ErrorMetric ErrorMetric;
        public float ErrorThreshold;
        public static OptimizerSettings Default => new()
        {
            ErrorMetric = ErrorMetric.Max,
            ErrorThreshold = 0.001f,
        };
    }

    public record class BlockOptimizationInfo
    {
        public int RemovedKeyFrameCount;
        public int RemovedPositionCount;
        public List<int> RemovedKeyFrameTimes = [];
    }

    public OptimizerSettings Settings { get; set; } = OptimizerSettings.Default;
    public required CGameCtnMediaBlockTriangles TrianglesBlock { get; set; }

    public BlockOptimizationInfo OptimizeKeyframes()
    {
        var optimizationInfo = new BlockOptimizationInfo();

        var keys = TrianglesBlock.Keys.ToArray();
        int n = keys.Length;
        bool[] keep = new bool[n];
        keep[0] = keep[n - 1] = true;
        SimplifyRange(keys, 0, n - 1, Settings.ErrorThreshold, keep);
        // Erzeuge neue Keyframe-Liste nur mit keep == true
        for (int i = 0; i < keys.Length; ++i)
        {
            if (keep[i])
                continue;
            optimizationInfo.RemovedKeyFrameCount++;
            optimizationInfo.RemovedPositionCount += keys[i].Positions.Length;
            optimizationInfo.RemovedKeyFrameTimes.Add((int)keys[i].Time.TotalMilliseconds);
        }
        TrianglesBlock.Keys = keys.Where((k, i) => keep[i]).ToList();
        return optimizationInfo;
    }
    void SimplifyRange(ReadOnlySpan<CGameCtnMediaBlockTriangles.Key> keys, int i, int k, float eps, Span<bool> keep)
    {
        float maxError = 0;
        int idxMax = -1;
        // Suche Keyframe mit maximaler Positionsabweichung
        for (int j = i + 1; j < k; j++)
        {
            float error = Error(keys[i], keys[k], keys[j]);
            if (error > maxError) 
            { 
                maxError = error; 
                idxMax = j; 
            }
        }
        if (idxMax >= 0 && maxError > eps)
        {
            keep[idxMax] = true;
            SimplifyRange(keys, i, idxMax, eps, keep);
            SimplifyRange(keys, idxMax, k, eps, keep);
        }
        // sonst: keine Zwischen-Keys behalten
    }

    float Error(CGameCtnMediaBlockTriangles.Key a, CGameCtnMediaBlockTriangles.Key b, CGameCtnMediaBlockTriangles.Key mid)
        => Settings.ErrorMetric switch
        {
            ErrorMetric.Max => MaxError(a, b, mid),
            ErrorMetric.RootMeanSquare => RMSError(a, b, mid),
            _ => throw new NotImplementedException(),
        };

    float MaxError(CGameCtnMediaBlockTriangles.Key a, CGameCtnMediaBlockTriangles.Key b, CGameCtnMediaBlockTriangles.Key mid)
    {
        float maxErr = 0;
        float t = (mid.Time - a.Time) / (b.Time - a.Time);
        for (int v = 0; v < a.Positions.Length; v++)
        {
            Vector3 pred = a.Positions[v] + (b.Positions[v] - a.Positions[v]) * t;
            float err = Vector3.Distance(pred, mid.Positions[v]);
            if (err > maxErr) 
                maxErr = err;
        }
        return maxErr;
    }
    float RMSError(CGameCtnMediaBlockTriangles.Key a, CGameCtnMediaBlockTriangles.Key b, CGameCtnMediaBlockTriangles.Key mid)
    {
        float sumSq = 0f;
        int count = a.Positions.Length;

        float t = (mid.Time - a.Time) / (b.Time - a.Time);

        for (int v = 0; v < count; v++)
        {
            Vector3 pred = a.Positions[v] + (b.Positions[v] - a.Positions[v]) * t;
            float err = Vector3.Distance(pred, mid.Positions[v]);

            sumSq += err * err;
        }

        return MathF.Sqrt(sumSq / count);
    }



    public record class TrackOptimizationInfo
    {
        public int TotalRemovedKeyFrameCount => BlockOptimizationInfos.Sum(b=>b.RemovedKeyFrameCount);
        public int TotalRemovedPositionCount => BlockOptimizationInfos.Sum(b => b.RemovedPositionCount);
        public string TrackName { get; set; } = string.Empty;

        public List<BlockOptimizationInfo> BlockOptimizationInfos = [];
    }
    public static TrackOptimizationInfo OptimizeTrack(CGameCtnMediaTrack track, OptimizerSettings? optimizerSettings = null)
    {
        var triangleBlocks = track.Blocks.OfType<CGameCtnMediaBlockTriangles>();
        if (triangleBlocks.Count() == 0)
            return new();
        var trackOptimizationInfo = new TrackOptimizationInfo();
        trackOptimizationInfo.TrackName = track.Name ?? string.Empty;
        var optimizer = new KeyFrameAnimationOptimizer
        {
            TrianglesBlock = triangleBlocks.First(),
            Settings = optimizerSettings ?? OptimizerSettings.Default
        };
        foreach(var block in triangleBlocks)
        {
            optimizer.TrianglesBlock = block;
            var blockOptimizationInfo = optimizer.OptimizeKeyframes();
            trackOptimizationInfo.BlockOptimizationInfos.Add(blockOptimizationInfo);
        }
        return trackOptimizationInfo;
    }
    public record class ClipOptimizationInfo
    {
        public int TotalRemovedKeyFrameCount => TrackOptimizationInfos.Sum(b => b.TotalRemovedKeyFrameCount);
        public int TotalRemovedPositionCount => TrackOptimizationInfos.Sum(b => b.TotalRemovedPositionCount);

        public List<TrackOptimizationInfo> TrackOptimizationInfos = [];
    }
    public static ClipOptimizationInfo OptimizeClip(CGameCtnMediaClip clip, OptimizerSettings? optimizerSettings = null)
    {
        var clipOptimizationInfo = new ClipOptimizationInfo();
        foreach(var track in clip.Tracks)
        {
            var trackOptimizationInfo = OptimizeTrack(track, optimizerSettings);
            clipOptimizationInfo.TrackOptimizationInfos.Add(trackOptimizationInfo);
        }
        return clipOptimizationInfo;
    }
}
