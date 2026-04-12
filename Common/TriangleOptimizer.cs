using GBX.NET;
using GBX.NET.Engines.Game;
using System.Numerics;
using TM_GenericMapping.IO;

namespace TM_GenericMapping.Common;


public class TriangleOptimizer
{
    public struct TriangleOptimizerSettings()
    {
        // ===== AUTO MODE =====
        public bool AutoComputeParameters = true;

        public bool AutoQuantizationStep;
        public bool AutoZeroThreshold = true;
        public bool AutoNormalization = true;
        public bool AutoTemporalThreshold = true;
        public bool AutoColorQuantization = true;

        public float RelativeError = 1e-5f; // used for auto-computing quantization step (max error relative to bounding box diagonal)

        // ===== OPTIONS =====
        public bool EnableQuantization = true;
        public float QuantizationStep = 0.0125f;

        public bool EnableZeroClamp = true;
        public float ZeroThreshold = 0.0125f / 2;

        public bool EnableNormalizeRange = false;
        public float TargetNormalizationRange = 10f;

        public bool EnableColorOptimization = false;

        public bool EnableTemporalStabilization = true;
        public float TemporalThresholdFactor = 1.0f;
        // multiplier of QuantizationStep (1.0 = same as step)
    }

    public record class OptimizationResult
    {
        public int BlocksProcessed;
        public int KeysProcessed;
        public int VerticesProcessed;
        public int VerticesChanged;
        public int VerticesZeroed;
        public int VerticesTemporallySnapped;
        public float ComputedStep;
        public bool IsStepComputed = false;

        public int ZeroBytesBeforeOptimization;
        public int ZeroBytesAfterOptimization;


        public float VertexChangePercent => VerticesProcessed == 0 ? 0f : (VerticesChanged / (float)VerticesProcessed) * 100f;
        public int MaximumPossibleZeroBytes => VerticesProcessed * 3 * 4;
        public float ZeroBytePercentagBefore => ZeroBytesBeforeOptimization / (float)MaximumPossibleZeroBytes * 100f;
        public float ZeroBytePercentagAfter => ZeroBytesAfterOptimization / (float)MaximumPossibleZeroBytes * 100f;
    }

    float computedQuantizationStep;
    float computedZeroThreshold;
    float computedTemporalThreshold;

    public TriangleOptimizerSettings Settings { get; set; } = new TriangleOptimizerSettings();

    public OptimizationResult OptimizeClip(CGameCtnMediaClip clip)
    {
        return OptimizeTracks(clip?.Tracks?.ToArray() ?? []);
    }

    public OptimizationResult OptimizeTracks(params ReadOnlySpan<CGameCtnMediaTrack> tracks)
    {
        var result = new OptimizationResult();

        if (tracks.IsEmpty) return result;

        float computedStep = 0f;
        foreach (var track in tracks)
        {
            var trackResult = OptimizeTrack(track);
            result.BlocksProcessed += trackResult.BlocksProcessed;
            result.KeysProcessed += trackResult.KeysProcessed;
            result.VerticesProcessed += trackResult.VerticesProcessed;
            result.VerticesChanged += trackResult.VerticesChanged;
            result.VerticesZeroed += trackResult.VerticesZeroed;
            result.VerticesTemporallySnapped += trackResult.VerticesTemporallySnapped;
            computedStep += trackResult.ComputedStep;
            result.IsStepComputed = trackResult.IsStepComputed;
            result.ZeroBytesBeforeOptimization += trackResult.ZeroBytesBeforeOptimization;
            result.ZeroBytesAfterOptimization += trackResult.ZeroBytesAfterOptimization;
        }
        result.ComputedStep = result.IsStepComputed ? computedStep / tracks.Length : 0f;
        return result;
    }

    public OptimizationResult OptimizeTrack(CGameCtnMediaTrack track)
    {
        var result = new OptimizationResult();
        if (track?.Blocks == null) return result;

        foreach (var block in track.Blocks.OfType<CGameCtnMediaBlockTriangles>())
            OptimizeBlock(block, result);

        return result;
    }

    void OptimizeBlock(CGameCtnMediaBlockTriangles block, OptimizationResult result)
    {
        if (block == null || block.Keys == null || block.Keys.Count == 0)
            return;

        if (Settings.AutoComputeParameters)
            ComputeAutoParameters(block, result);

        if (Settings.EnableNormalizeRange)
            NormalizeAllKeys(block);

        // FIRST: quantize all frames
        foreach (var key in block.Keys)
        {
            OptimizeVertices(key.Positions, result);
            result.KeysProcessed++;
        }

        // THEN: enforce temporal coherence
        if(Settings.EnableTemporalStabilization)
            ApplyTemporalCoherence(block, result);


        // colors unchanged per frame
        if (Settings.EnableColorOptimization)
            OptimizeColors(block.Vertices);

        result.BlocksProcessed++;
    }

    // ===== AUTO PARAMS (ACROSS ALL KEYS) =====
    void ComputeAutoParameters(CGameCtnMediaBlockTriangles block, OptimizationResult result)
    {
        bool hasAny = false;
        Vector3 min = default, max = default;

        float maxAbs = 0f;

        foreach (var key in block.Keys)
        {
            var verts = key.Positions;
            if (verts == null || verts.Length == 0) continue;

            if (!hasAny)
            {
                min = max = verts[0];
                hasAny = true;
            }

            foreach (var v in verts)
            {
                min = Vector3.Min(min, v);
                max = Vector3.Max(max, v);

                maxAbs = MathF.Max(maxAbs, MathF.Abs(v.X));
                maxAbs = MathF.Max(maxAbs, MathF.Abs(v.Y));
                maxAbs = MathF.Max(maxAbs, MathF.Abs(v.Z));
            }
        }

        if (!hasAny) return;

        float diag = (max - min).Length();
        if (diag == 0f) diag = 1f;

        float maxError = diag * Settings.RelativeError;

        computedQuantizationStep = maxError * 2f;

        result.ComputedStep = computedQuantizationStep;
        result.IsStepComputed = true;
        computedZeroThreshold = computedQuantizationStep * 0.5f;
        computedTemporalThreshold = computedQuantizationStep * Settings.TemporalThresholdFactor;
        //ComputedEnableNormalization = (maxAbs > 1000f || maxAbs < 0.001f);

    }

    // ===== VERTICES =====
    void OptimizeVertices(Vec3[] vertices, OptimizationResult result)
    {
        if (vertices == null) return;

        for (int i = 0; i < vertices.Length; i++)
        {
            var v = vertices[i];

            result.ZeroBytesBeforeOptimization += CountZeroBytes(v.X) + CountZeroBytes(v.Y) + CountZeroBytes(v.Z);
           

            var newV = new Vec3(
                ProcessFloat(v.X, result),
                ProcessFloat(v.Y, result),
                ProcessFloat(v.Z, result));

            result.ZeroBytesAfterOptimization += CountZeroBytes(newV.X) + CountZeroBytes(newV.Y) + CountZeroBytes(newV.Z);
            result.VerticesProcessed++;
            if (newV.X != v.X || newV.Y != v.Y || newV.Z != v.Z)
                result.VerticesChanged++;

            vertices[i] = newV;
        }
    }

    int CountZeroBytes(float v)
    {
        int bits = BitConverter.SingleToInt32Bits(v);
        int count = 0;
        if ((bits & 0xFF) == 0) count++;
        if ((bits & 0xFF00) == 0) count++;
        if ((bits & 0xFF0000) == 0) count++;
        if ((bits & 0xFF000000) == 0) count++;
        return count;
    }

    // ===== COLORS =====
    void OptimizeColors(Vec4[] colors)
    {
        if (colors == null) return;

        for (int i = 0; i < colors.Length; i++)
        {
            var c = colors[i];
            Vec4 newC = new();
            newC = new Vec4(
                   QuantizeColor(c.X),
                   QuantizeColor(c.Y),
                   QuantizeColor(c.Z),
                   QuantizeColor(c.W));

            colors[i] = newC;
        }
    }

    // ===== FLOAT OPS =====
    float ProcessFloat(float v, OptimizationResult result)
    {
        float step = (Settings.AutoQuantizationStep && Settings.AutoComputeParameters) ? computedQuantizationStep : Settings.QuantizationStep;
        float threshold = (Settings.AutoZeroThreshold && Settings.AutoComputeParameters) ? computedZeroThreshold : Settings.ZeroThreshold;

        v = MathF.Round(v / step) * step;

        if (Settings.EnableZeroClamp && MathF.Abs(v) < threshold)
        {
            v = 0f;
            result.VerticesZeroed++;
        }

        return v;
    }



    float QuantizeColor(float v)
    {
        return MathF.Round(v * 255f) / 255f;
    }

    // ===== NORMALIZE (ALL KEYS CONSISTENTLY) =====
    void NormalizeAllKeys(CGameCtnMediaBlockTriangles block)
    {
        float maxAbs = 0f;

        foreach (var key in block.Keys)
        {
            var verts = key.Positions;
            if (verts == null) continue;

            foreach (var v in verts)
            {
                maxAbs = MathF.Max(maxAbs, MathF.Abs(v.X));
                maxAbs = MathF.Max(maxAbs, MathF.Abs(v.Y));
                maxAbs = MathF.Max(maxAbs, MathF.Abs(v.Z));
            }
        }

        if (maxAbs == 0f) return;

        float scale = Settings.TargetNormalizationRange / maxAbs;

        foreach (var key in block.Keys)
        {
            var verts = key.Positions;
            if (verts == null) continue;

            for (int i = 0; i < verts.Length; i++)
                verts[i] *= scale;
        }
    }

    void ApplyTemporalCoherence(CGameCtnMediaBlockTriangles block, OptimizationResult result)
    {
        if (block.Keys == null || block.Keys.Count < 2) return;

        float step = (Settings.AutoQuantizationStep && Settings.AutoComputeParameters) ? computedQuantizationStep : Settings.QuantizationStep;
        float temporalFactor = (Settings.AutoTemporalThreshold && Settings.AutoComputeParameters) ? computedTemporalThreshold : Settings.TemporalThresholdFactor;

        float threshold = step * temporalFactor;

        var keys = block.Keys;

        for (int k = 1; k < keys.Count; k++)
        {
            var prev = keys[k - 1].Positions;
            var curr = keys[k].Positions;

            if (prev == null || curr == null) continue;
            if (prev.Length != curr.Length) continue;

            for (int i = 0; i < curr.Length; i++)
            {
                var p = prev[i];
                var c = curr[i];

                c = new Vec3()
                {
                    X = Stabilize(c.X, p.X, threshold),
                    Y = Stabilize(c.Y, p.Y, threshold),
                    Z = Stabilize(c.Z, p.Z, threshold),
                };
                if (c.X != c.X || c.Y != c.Y || c.Z != c.Z)
                    result.VerticesTemporallySnapped++;
                curr[i] = c;
            }
        }
    }

    float Stabilize(float curr, float prev, float threshold)
    {
        if (MathF.Abs(curr - prev) < threshold)
            return prev; // snap to previous frame (key part)

        return curr;
    }
}
