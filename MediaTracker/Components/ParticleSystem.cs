using System.Drawing;
using System.Numerics;
using TM_GenericMapping.Common;

namespace TM_GenericMapping.MediaTracker.Components;


public class ParticleSystem : UpdatableComponentBase
{

    public struct ParticleState
    {
        public int ID;
        public int MeshIndex;
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Scale;
        public Color? Color;
        public float AgeMillis;
        public float LifeFraction; // 0..1, age / lifetime
        public bool LinearTranformation;
    }

    public enum MinMaxMode
    {
        Constant,
        Curve,
        RandomBetweenTwoConstants,
    }

    public struct MinMaxCurve
    {
        public float Min;
        public float Max;
        public Easing Easing;
        public MinMaxMode Mode;

        public static implicit operator MinMaxCurve(float v)
            => new MinMaxCurve { Min = v, Max = v, Easing = Easing.Linear, Mode = MinMaxMode.Constant };

        public static implicit operator MinMaxCurve((float min, float max) v)
            => new MinMaxCurve { Min = v.min, Max = v.max, Easing = Easing.Linear, Mode = MinMaxMode.Constant };

        public static implicit operator MinMaxCurve((float min, float max, MinMaxMode mode) v)
            => new MinMaxCurve { Min = v.min, Max = v.max, Easing = Easing.Linear, Mode = v.mode };

        public static implicit operator MinMaxCurve((float min, float max, Easing easing) v)
            => new MinMaxCurve { Min = v.min, Max = v.max, Easing = v.easing, Mode = MinMaxMode.Curve };

        public static implicit operator MinMaxCurve((float min, float max, MinMaxMode mode, Easing easing) v)
            => new MinMaxCurve { Min = v.min, Max = v.max, Easing = v.easing, Mode = v.mode };

        public float GetValue(float t, Random rng) => Mode switch
        {
            MinMaxMode.Constant => Min,
            MinMaxMode.Curve => EasingUtils.Ease(Min, Max, t, Easing),
            MinMaxMode.RandomBetweenTwoConstants => rng.NextSingle(Min, Max),
            _ => throw new ArgumentOutOfRangeException(),
        };
    }

    public struct MinMaxGradient
    {
        public Color Min;
        public Color Max;
        public Easing Easing;
        public MinMaxMode Mode;

        public static implicit operator MinMaxGradient(Color c)
            => new MinMaxGradient { Min = c, Max = c, Easing = Easing.Linear, Mode = MinMaxMode.Constant };

        public static implicit operator MinMaxGradient((Color min, Color max) v)
            => new MinMaxGradient { Min = v.min, Max = v.max, Easing = Easing.Linear, Mode = MinMaxMode.Constant };

        public static implicit operator MinMaxGradient((Color min, Color max, MinMaxMode mode) v)
            => new MinMaxGradient { Min = v.min, Max = v.max, Easing = Easing.Linear, Mode = v.mode };

        public static implicit operator MinMaxGradient((Color min, Color max, Easing easing) v)
            => new MinMaxGradient { Min = v.min, Max = v.max, Easing = v.easing, Mode = MinMaxMode.Curve };

        public Color GetValue(float t, Random rng) => Mode switch
        {
            MinMaxMode.Constant => Min,
            MinMaxMode.Curve => ColorUtils.Lerp(Min, Max, EasingUtils.Ease(0, 1, t, Easing)),
            MinMaxMode.RandomBetweenTwoConstants => Color.FromArgb(
                rng.Next(Min.A, Max.A + 1),
                rng.Next(Min.R, Max.R + 1),
                rng.Next(Min.G, Max.G + 1),
                rng.Next(Min.B, Max.B + 1)),
            _ => throw new ArgumentOutOfRangeException(),
        };
    }

    public struct ColorOverLifeTime
    {
        public bool Disabled;
        public MinMaxGradient Color;
    }

    public abstract class EmissionShape
    {
        public abstract Vector3 SpawnPosition(Random rng);
        public abstract Vector3 RandomDirection(Random rng);
    }

    public class SphereShape : EmissionShape
    {
        public float Radius = 1;
        public override Vector3 SpawnPosition(Random rng)
        {
            Vector3 dir = rng.RandomUnitVector();
            float r = MathF.Pow((float)rng.NextDouble(), 1f / 3f) * Radius;
            return dir * r;
        }
        public override Vector3 RandomDirection(Random rng) => rng.RandomUnitVector();
    }

    public class BoxShape : EmissionShape
    {
        public Vector3 Size = new Vector3(1, 1, 1);
        public override Vector3 SpawnPosition(Random rng) => new Vector3(
            rng.NextSingle(-Size.X / 2, Size.X / 2),
            rng.NextSingle(-Size.Y / 2, Size.Y / 2),
            rng.NextSingle(-Size.Z / 2, Size.Z / 2));
        public override Vector3 RandomDirection(Random rng) => rng.RandomUnitVector();
    }

    public class ConeShape : EmissionShape
    {
        public float Radius = 1;
        public float AngleDegrees = 30;
        public override Vector3 SpawnPosition(Random rng)
        {
            float a = rng.NextSingle(0, MathF.PI * 2);
            float r = MathF.Sqrt((float)rng.NextDouble()) * Radius;
            return new Vector3(MathF.Cos(a) * r, 0, MathF.Sin(a) * r);
        }
        public override Vector3 RandomDirection(Random rng)
        {
            float theta = rng.NextSingle(0, AngleDegrees * MathUtils.Deg2Rad);
            float phi = rng.NextSingle(0, MathF.PI * 2);
            Vector3 dir = new(
                MathF.Sin(theta) * MathF.Cos(phi),
                MathF.Cos(theta),
                MathF.Sin(theta) * MathF.Sin(phi));
            return Vector3.Normalize(dir);
        }
    }



    public int Seed { get; init; } = 1234;
    public TriangleObject[] ParticleMeshes { get; init; } = [];

    public float DurationMillis { get; init; } = 5000f;
    public float Duration { get => DurationMillis / 1000f; init => DurationMillis = value * 1000f; }
    public float StartDelayMillis { get; init; } = 0f;
    public float StartDelay { get => StartDelayMillis / 1000f; init => StartDelayMillis = value * 1000f; }
    public bool Loop { get; init; } = false;
    public bool Prewarm { get; init; } = false;
    public bool MergedTrack { get; init; } = true;
    public IKeysRenderer ParticleRenderer { get; init; } = Rendering.DefaultTriangleRenderer;

    public int MaxParticles { get; init; } = 10000;
    public MinMaxCurve StartLifetimeMillis { get; init; } = 1000f;
    public MinMaxCurve StartVelocity { get; init; } = 1f;
    public MinMaxCurve ScaleOverTime { get; init; } = 1f;

    public MinMaxCurve StartAngularVelocityX { get; init; } = 0f;
    public MinMaxCurve StartAngularVelocityY { get; init; } = 0f;
    public MinMaxCurve StartAngularVelocityZ { get; init; } = 0f;

    public MinMaxCurve StartRotationX { get; init; } = 0f;
    public MinMaxCurve StartRotationY { get; init; } = 0f;
    public MinMaxCurve StartRotationZ { get; init; } = 0f;

    public Vector3 Gravity { get; init; } = Vector3.Zero;

    public MinMaxCurve EmissionIntervalMillisOverTime { get; init; } = 100f;
    public EmissionShape Shape { get; init; } = new SphereShape { Radius = 1 };
    public ColorOverLifeTime EmissionColorOverTime { get; init; } = new ColorOverLifeTime { Disabled = true, Color = Color.White };


    private struct ParticleTemplate
    {
        public int ID;
        public int MeshIndex;
        public float BirthMillis;      // phase within [0, DurationMillis)
        public float LifetimeMillis;
        public Vector3 SpawnPosition;
        public Vector3 InitialVelocity;
        public Quaternion InitialRotation;
        public Vector3 AngularVelocity;
        public float Scale;
        public bool HasColor;
        public Color Color;
    }

    private List<ParticleTemplate>? templates;


    private void EnsureBuilt()
    {
        if (templates != null) return;
        Rebuild();
    }

    /// <summary>
    /// (Re)computes the deterministic emission schedule for one full duration
    /// </summary>
    void Rebuild()
    {
        var list = new List<ParticleTemplate>();
        float t = 0f;
        int index = 0;

        while (t < DurationMillis && index < MaxParticles)
        {
            float phaseProgress = DurationMillis > 0 ? t / DurationMillis : 0f;

            // Sample interval curve fresh at the *pre-birth* phase, matching
            // your original accumulator semantics (interval measured from the
            // previous emission point).
            var scratchRng = new Random(Hash(Seed, (ulong)index) ^ 0x5bd1e995);
            float interval = EmissionIntervalMillisOverTime.GetValue(phaseProgress, scratchRng);
            if (interval <= 0f) break; // no further emission possible

            t += interval;
            if (t >= DurationMillis) break; // this birth belongs to the next cycle, not this one

            list.Add(CreateTemplate(index, t));
            index++;
        }

        templates = list;
    }

    private ParticleTemplate CreateTemplate(int index, float birthMillis)
    {
        int n = Hash(Seed, (ulong)index);
        var rng = new Random(n);
        float phaseProgress = DurationMillis > 0 ? birthMillis / DurationMillis : 0f;

        var tpl = new ParticleTemplate
        {
            ID = n,
            MeshIndex = ParticleMeshes.Length > 0 ? rng.Next(ParticleMeshes.Length) : 0,
            BirthMillis = birthMillis,
            LifetimeMillis = StartLifetimeMillis.GetValue(phaseProgress, rng),
            SpawnPosition = Shape.SpawnPosition(rng),
            InitialVelocity = Shape.RandomDirection(rng) * StartVelocity.GetValue(phaseProgress, rng),
            InitialRotation = Quaternion.CreateFromYawPitchRoll(
                StartRotationY.GetValue(phaseProgress, rng),
                StartRotationX.GetValue(phaseProgress, rng),
                StartRotationZ.GetValue(phaseProgress, rng)),
            AngularVelocity = new Vector3(
                StartAngularVelocityX.GetValue(phaseProgress, rng),
                StartAngularVelocityY.GetValue(phaseProgress, rng),
                StartAngularVelocityZ.GetValue(phaseProgress, rng)),
            Scale = ScaleOverTime.GetValue(phaseProgress, rng),
        };

        if (!EmissionColorOverTime.Disabled)
        {
            tpl.HasColor = true;
            tpl.Color = EmissionColorOverTime.Color.GetValue(phaseProgress, rng);
        }

        return tpl;
    }


    /// <summary>
    /// Returns every particle alive at the given global time. Pure function of
    /// time -- call it with any timestamp in any order, including out-of-order
    /// (useful for baking keyframes ahead of time). No state is mutated.
    /// </summary>
    public IReadOnlyList<ParticleState> GetParticles(ulong timeMillis)
    {
        EnsureBuilt();
        var result = new List<ParticleState>();

        float t = timeMillis;
        if (t < 0f) return result;

        bool steadyState = Loop && (Prewarm || t >= DurationMillis);

        if (steadyState)
        {
            float phase = Mod(t, DurationMillis);
            foreach (var tpl in templates!)
            {
                // Circular age: continuous across the phase-0/phase-Duration
                // seam by construction. A particle born late in the cycle
                // (large BirthMillis) queried at a small `phase` naturally
                // gets a small `age` -- it has "wrapped in" already-aged,
                // exactly matching where it left off at phase Duration.
                float age = Mod(phase - tpl.BirthMillis, DurationMillis);
                if (age < tpl.LifetimeMillis)
                    result.Add(Evaluate(tpl, age));
            }
        }
        else
        {
            // Cold start (Loop=false, OR Loop=true+Prewarm=false during the
            // very first Duration window): no wraparound, system fills up
            // from empty. Continuous with the steadyState branch above at
            // t = DurationMillis, so there's no pop when it hands off.
            foreach (var tpl in templates!)
            {
                float age = t - tpl.BirthMillis;
                if (age >= 0f && age < tpl.LifetimeMillis)
                    result.Add(Evaluate(tpl, age));
            }
        }

        return result;
    }

    private ParticleState Evaluate(in ParticleTemplate tpl, float ageMillis)
    {
        float ageS = ageMillis / 1000f;

        bool linearTransform = true;

        // Closed-form projectile motion -- exact, no accumulated float drift
        // from stepping frame by frame.
        Vector3 position = tpl.SpawnPosition
            + tpl.InitialVelocity * ageS
            + 0.5f * Gravity * ageS * ageS;

        linearTransform &= Gravity.LengthSquared() == 0f;

        Quaternion spin = Quaternion.CreateFromYawPitchRoll(
            tpl.AngularVelocity.Y * ageS,
            tpl.AngularVelocity.X * ageS,
            tpl.AngularVelocity.Z * ageS);

      
        Quaternion rotation = Quaternion.Normalize(tpl.InitialRotation * spin);

        linearTransform &= tpl.AngularVelocity.LengthSquared() == 0f;

        return new ParticleState
        {
            ID = tpl.ID,
            MeshIndex = tpl.MeshIndex,
            Position = position,
            Rotation = rotation,
            Scale = new Vector3(tpl.Scale),
            Color = tpl.HasColor ? tpl.Color : null,
            AgeMillis = ageMillis,
            LifeFraction = tpl.LifetimeMillis > 0 ? ageMillis / tpl.LifetimeMillis : 0f,
            LinearTranformation = linearTransform
        };
    }

    private static float Mod(float a, float m)
    {
        if (m <= 0f) return 0f;
        float r = a % m;
        return r < 0f ? r + m : r;
    }

    private static int Hash(int seed, ulong index)
    {
        unchecked
        {
            ulong x = (ulong)seed + index * 0x9E3779B97F4A7C15UL;
            x ^= x >> 30;
            x *= 0xBF58476D1CE4E5B9UL;
            x ^= x >> 27;
            x *= 0x94D049BB133111EBUL;
            x ^= x >> 31;
            return (int)x;
        }
    }


    ulong startTimeMillis = 0;
    MediaObject root = null!;
    ulong activeTime = 0;
    ulong simulatedTime = 0;
    bool started = false;
    bool stopped = false;
    Dictionary<int, TriangleObject> livingParticleInstances = [];
    Dictionary<int, TriangleObject> allParticleInstances = [];
    public override void OnInit(MediaObject obj, SceneTimeline scene)
    {
        if (MergedTrack)
            root = new TriangleObject(renderer: ParticleRenderer);
        else
            root = new EmptyObject();
        OnUpdate(obj, scene, 0);
    }
    public override void OnUpdate(MediaObject obj, SceneTimeline scene, ulong deltaTimeMillis)
    {
        if (stopped)
            return;
        activeTime += deltaTimeMillis;
       
        if (activeTime < StartDelayMillis)
            return;
        if (!started)
            Start(obj, scene);
        simulatedTime += deltaTimeMillis;

        var particles = GetParticles(simulatedTime);
        foreach(var p in particles)
        {
            if(!livingParticleInstances.TryGetValue(p.ID, out var instance))
            {
                // new particle
                instance = ParticleMeshes[p.MeshIndex].Clone();
                instance.Renderer = ParticleRenderer;
                instance.LocalPosition = p.Position;
                instance.LocalRotation = p.Rotation;
                instance.LocalScale = p.Scale;
                if(p.Color.HasValue)
                    instance.SetFillColor(p.Color.Value);

                livingParticleInstances[p.ID] = instance;
                allParticleInstances[p.ID] = instance;

                if (!MergedTrack)
                    instance.BlockShareMode = BlockShareMode.FromChildren;

                scene.AddSubObjects(root, InitialStatePolicy.Hidden, instance);
                scene.SetKeepTrackActive(false, instance);
            }
            else
            {
                instance.LocalPosition = p.Position;
                instance.LocalRotation = p.Rotation;
                instance.LocalScale = p.Scale;
                if(!p.LinearTranformation)
                    scene.RequireKeyFrame(instance);
            }
        }
        foreach(var p in livingParticleInstances.Keys.Except(particles.Select(p => p.ID)).ToList())
        {
            var instance = livingParticleInstances[p];
            if (MergedTrack)
                instance.LocalScale = Vector3.Zero;
            scene.RequireKeyFrame(instance);
            livingParticleInstances.Remove(p);
        }
        foreach(var p in allParticleInstances)
        {
            if (Loop && !MergedTrack)
                scene.SetTrackCycling(new CycleData { RelativeStart = false, Start = (long)startTimeMillis, RelativeEnd = false, End = (long)scene.AnimationTimeMillis }, p.Value);
        }
        if (activeTime >= DurationMillis)
        {
            stopped = true;
            foreach(var instance in livingParticleInstances.Values)
            {
                scene.RequireKeyFrame(instance);
            }
            livingParticleInstances.Clear();
        }

    }
    void Start(MediaObject obj, SceneTimeline scene)
    {
        startTimeMillis = scene.AnimationTimeMillis;
        scene.AddSubObjects(obj, root);
        if(Loop)
            scene.SetTrackCycling(true, root);
        started = true;
    }
}