using GBX.NET;
using GBX.NET.Engines.Control;
using GBX.NET.Engines.Game;
using GBX.NET.Engines.MwFoundations;
using GBX.NET.Serialization.Chunking;
using System.Reflection;
using TM_GenericMapping.Common;
using TmEssentials;
using static GBX.NET.Engines.Game.CGameCtnMediaBlock;

namespace TM_GenericMapping.Common;

public static class MediaTrackerUtils
{
    public static CGameCtnMediaClip DeepCopyClip(CGameCtnMediaClip original)
    {
        var cpy = new CGameCtnMediaClip()
        {
            StopWhenLeave = original.StopWhenLeave,
            StopWhenRespawn = original.StopWhenRespawn,
            LocalPlayerClipEntIndex = original.LocalPlayerClipEntIndex,
            Name = original.Name,
            Tracks = original.Tracks.Select(DeepCopyTrack).ToList()
        };
        foreach (var chunk in original.Chunks)
        {
            cpy.Chunks.Add(chunk);
        }
        return cpy;
    }
    public static CGameCtnMediaClipGroup.ClipTrigger DeepCopyClipTrigger(CGameCtnMediaClipGroup.ClipTrigger original)
    {
        var cpy = new CGameCtnMediaClipGroup.ClipTrigger()
        {
            Clip = DeepCopyClip(original.Clip),
            Trigger = DeepCopyTrigger(original.Trigger),
        };
        return cpy;
    }
    public static CGameCtnMediaClipGroup.Trigger DeepCopyTrigger(CGameCtnMediaClipGroup.Trigger original)
    {
        var cpy = new CGameCtnMediaClipGroup.Trigger()
        {
            Condition = original.Condition,
            Coords = original.Coords.ToList(),
            ConditionValue = original.ConditionValue,
            U01 = original.U01,
            U02 = original.U02,
            U03 = original.U03,
            U04 = original.U04,

        };
        return cpy;
    }
    public static CGameCtnMediaTrack DeepCopyTrack(CGameCtnMediaTrack original)
    {
        var cpy = new CGameCtnMediaTrack()
        {
            IsCycling = original.IsCycling,
            IsKeepPlaying = original.IsKeepPlaying,
            IsReadOnly = original.IsReadOnly,
            Name = original.Name,
        };
        foreach (var chunk in original.Chunks)
        {
            //cpy.CreateChunk(chunk.Id);
            cpy.Chunks.Add(chunk);
        }
        foreach (var block in original.Blocks)
        {
            if (block is CGameCtnMediaBlockImage mbi)
                cpy.Blocks.Add(DeepCopyBlockImage(mbi));
            else if (block is CGameCtnMediaBlockText mbt)
                cpy.Blocks.Add(DeepCopyBlockText(mbt));
            else if (block is CGameCtnMediaBlockTriangles3D mt3)
                cpy.Blocks.Add(DeepCopyBlockTriangles3D(mt3));
            else if (block is CGameCtnMediaBlockTriangles2D mt2)
                cpy.Blocks.Add(DeepCopyBlockTriangles2D(mt2));
            else
            {
                throw new NotImplementedException();
            }
        }
        return cpy;
    }
    public static CGameCtnMediaBlockImage DeepCopyBlockImage(CGameCtnMediaBlockImage original)
    {
        var cpy = new CGameCtnMediaBlockImage()
        {
            Image = original.Image,
            Effect = DeepCopyEffect(original.Effect),
        };
        DeepCopyChunks(original, cpy);
        return cpy;
    }
    public static CControlEffectSimi DeepCopyEffect(CControlEffectSimi original)
    {
        var cpy = new CControlEffectSimi()
        {
            IsContinousEffect = original.IsContinousEffect,
            IsInterpolated = original.IsInterpolated,
            Centered = original.Centered,
            ColorBlendMode = original.ColorBlendMode,
            Keys = original.Keys.Select(DeepCopyKey).ToList(),
        };
        DeepCopyChunks(original, cpy);
        return cpy;
    }
    public static GBX.NET.Engines.Control.CControlEffectSimi.Key DeepCopyKey(GBX.NET.Engines.Control.CControlEffectSimi.Key original)
    {
        var cpy = new GBX.NET.Engines.Control.CControlEffectSimi.Key()
        {
            Depth = original.Depth,
            Opacity = original.Opacity,
            Position = original.Position,
            Rotation = original.Rotation,
            Scale = original.Scale,
            Time = original.Time,
            U01 = original.U01,
            U02 = original.U02,
            U03 = original.U03,
            U04 = original.U04,
        };
        return cpy;
    }
    public static CGameCtnMediaBlockText DeepCopyBlockText(CGameCtnMediaBlockText original)
    {
        var cpy = new CGameCtnMediaBlockText()
        {
            Text = original.Text,
            Color = original.Color,
            Effect = DeepCopyEffect(original.Effect),
        };
        DeepCopyChunks(original, cpy);
        return cpy;
    }
    public static CGameCtnMediaBlockTriangles3D DeepCopyBlockTriangles3D(CGameCtnMediaBlockTriangles3D original)
    {
        var cpy = new CGameCtnMediaBlockTriangles3D()
        {
            Keys = original.Keys.Select(k => DeepCopyTriangle3DKey(k, original)).ToList(),
            Triangles = original.Triangles.ToArray(),
            Vertices = original.Vertices.ToArray(),
        };
        foreach (var chunk in original.Chunks)
        {
            cpy.Chunks.Add(chunk);
        }
        return cpy;
    }


    public static CGameCtnMediaBlockTriangles3D.Key DeepCopyTriangle3DKey(CGameCtnMediaBlockTriangles3D.Key original, CGameCtnMediaBlockTriangles3D triangles)
    {
        var cpy = new CGameCtnMediaBlockTriangles3D.Key(triangles)
        {
            Time = original.Time,
            Positions = original.Positions.ToArray(),
        };
        return cpy;
    }

    public static CGameCtnMediaBlockTriangles2D DeepCopyBlockTriangles2D(CGameCtnMediaBlockTriangles2D original)
    {
        var cpy = new CGameCtnMediaBlockTriangles2D()
        {
            Keys = original.Keys.Select(k => DeepCopyTriangle2DKey(k, original)).ToList(),
            Triangles = original.Triangles.ToArray(),
            Vertices = original.Vertices.ToArray(),
        };
        foreach (var chunk in original.Chunks)
        {
            cpy.Chunks.Add(chunk);
        }
        return cpy;
    }
    public static CGameCtnMediaBlockTriangles2D.Key DeepCopyTriangle2DKey(CGameCtnMediaBlockTriangles2D.Key original, CGameCtnMediaBlockTriangles2D triangles)
    {
        var cpy = new CGameCtnMediaBlockTriangles2D.Key(triangles)
        {
            Time = original.Time,
            Positions = original.Positions.ToArray(),
        };
        return cpy;
    }

    public static IKey GetLastKeyInBlock(IHasKeys block) => block.Keys.Last();

    public static void LoadIntoClipAtTime(CGameCtnMediaClip target, CGameCtnMediaClip from, ulong timeMillis)
    {
        foreach(var fromTrack in from.Tracks)
        {
            foreach (var block in fromTrack.Blocks)
            {
                if (block is CGameCtnMediaBlock.IHasKeys hasKeys)
                {
                    foreach (var key in hasKeys.Keys)
                    {
                        key.Time += TimeSingle.FromMilliseconds(timeMillis);
                    }
                }
                else if (block is IHasTwoKeys hasTwoKeys)
                {
                    hasTwoKeys.End += TimeSingle.FromMilliseconds(timeMillis);
                    hasTwoKeys.Start += TimeSingle.FromMilliseconds(timeMillis);
                }
            }
            target.Tracks.Add(fromTrack);
        }
    }


    public static string Triangles2DTemplatePath = Path.Combine(WindowsUtils.MyDocumentsPath, @"Trackmania\Replays\Clips\Templates\Triangle2DTemplate.Clip.Gbx");
    public static string Triangles3DTemplatePath = Path.Combine(WindowsUtils.MyDocumentsPath, @"Trackmania\Replays\Clips\Templates\Triangle3DTemplate.Clip.Gbx");
    public static string TrackTemplatePath = Path.Combine(WindowsUtils.MyDocumentsPath, @"Trackmania\Replays\Clips\Templates\Triangle2DTemplate.Clip.Gbx");
    public static string ImageTemplatePath = Path.Combine(WindowsUtils.MyDocumentsPath, @"Trackmania\Replays\Clips\Templates\ImageTemplate.Clip.Gbx");
    public static string TextTemplatePath = Path.Combine(WindowsUtils.MyDocumentsPath, @"Trackmania\Replays\Clips\Templates\TextTemplate.Clip.Gbx");

    public static BlockTemplates CreateBlockTemplates()
    {
        var emptyTrackClip = Gbx.Parse<CGameCtnMediaClip>(TrackTemplatePath).Node;
        var emptyTrack = emptyTrackClip.Tracks[0];
        var emptyClip = DeepCopyClip(emptyTrackClip);
        emptyClip.Tracks.Clear();
        emptyTrack.Blocks.Clear();
        return new BlockTemplates(
            emptyClip,
            emptyTrack,
            Gbx.Parse<CGameCtnMediaClip>(Triangles2DTemplatePath).Node.Tracks[0].Blocks[0] as CGameCtnMediaBlockTriangles2D,
            Gbx.Parse<CGameCtnMediaClip>(Triangles3DTemplatePath).Node.Tracks[0].Blocks[0] as CGameCtnMediaBlockTriangles3D,
            Gbx.Parse<CGameCtnMediaClip>(TextTemplatePath).Node.Tracks[0].Blocks[0] as CGameCtnMediaBlockText,
            Gbx.Parse<CGameCtnMediaClip>(ImageTemplatePath).Node.Tracks[0].Blocks[0] as CGameCtnMediaBlockImage
            );
    }

    public static void DeepCopyChunks(CMwNod original, CMwNod target)
    {
        foreach (var chunk in original.Chunks)
        {
            var chunkType = chunk.GetType();
            var method = target.GetType()
                .GetMethod(nameof(target.CreateChunk))!
                .MakeGenericMethod(chunkType);

            var newChunk = method.Invoke(target, null);

            foreach (var field in chunkType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                field.SetValue(newChunk, field.GetValue(chunk));
            }
        }
    }



}
