using GBX.NET;
using GBX.NET.Engines.Control;
using GBX.NET.Engines.Game;
using GBX.NET.Engines.MwFoundations;
using System.Reflection;
using TM_GenericMapping.Templating;
using TmEssentials;
using static GBX.NET.Engines.Game.CGameCtnMediaBlock;
using static GBX.NET.Engines.Game.CGameCtnMediaClipGroup;

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
            Coords = original.Coords?.ToList() ?? [],
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
            else if (block is CGameCtnMediaBlockCameraGame mcg)
                cpy.Blocks.Add(DeepCopyBlockPlayerCamera(mcg));
            else if (block is CGameCtnMediaBlockDOF dof)
                cpy.Blocks.Add(DeepCopyBlockDepthOfField(dof));
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
            Effect = original.Effect is not null ? DeepCopyEffect(original.Effect) : null,
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
            Keys = original.Keys?.Select(DeepCopyKey).ToList() ?? [],
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
            Effect = original.Effect is not null ? DeepCopyEffect(original.Effect) : null,
        };
        DeepCopyChunks(original, cpy);
        return cpy;
    }
    public static CGameCtnMediaBlockTriangles3D DeepCopyBlockTriangles3D(CGameCtnMediaBlockTriangles3D original)
    {
        var cpy = new CGameCtnMediaBlockTriangles3D()
        {
            Keys = original.Keys?.Select(k => DeepCopyTriangle3DKey(k, original)).ToList() ?? [],
            Vertices = original.Vertices.ToArray(),
            Triangles = original.Triangles.ToArray(),
           
        };
        foreach (var chunk in original.Chunks)
        {
            cpy.Chunks.Add(chunk);
        }
        return cpy;
    }

    public static CGameCtnMediaBlockCameraGame DeepCopyBlockPlayerCamera(CGameCtnMediaBlockCameraGame original)
    {
        var cpy = new CGameCtnMediaBlockCameraGame()
        {
            Start = original.Start,
            End = original.End,
            CamFarClipPlane = original.CamFarClipPlane,
            CamFov = original.CamFov,
            CamNearClipPlane = original.CamNearClipPlane,
            CamPitchYawRoll = original.CamPitchYawRoll,
            CamPosition = original.CamPosition,
            ClipEntId = original.ClipEntId,
            GameCam = original.GameCam,
            GameCamId = original.GameCamId,
            GameCamOld = original.GameCamOld,
        };
        DeepCopyChunks(original, cpy);
        return cpy;
    }
    public static CGameCtnMediaBlockDOF DeepCopyBlockDepthOfField(CGameCtnMediaBlockDOF original)
    {
        var cpy = new CGameCtnMediaBlockDOF()
        {
            Keys = original.Keys?.Select(k => DeepCopyDOFKey(k)).ToList() ?? [],
        };

        DeepCopyChunks(original, cpy);
        return cpy;
    }

    public static CGameCtnMediaBlockCameraCustom DeepCopyBlockCustomCamera(CGameCtnMediaBlockCameraCustom original)
    {
        var cpy = new CGameCtnMediaBlockCameraCustom()
        {
            Keys = original.Keys?.Select(k => DeepCopyCustomCameraKey(k)).ToList() ?? [],
        };

        DeepCopyChunks(original, cpy);
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
    public static CGameCtnMediaBlockFxBlurDepth.Key DeepCopyFxBlurDepthKey(CGameCtnMediaBlockFxBlurDepth.Key original)
    {
        var cpy = new CGameCtnMediaBlockFxBlurDepth.Key()
        {
            Time = original.Time,
            LensSize = original.LensSize,
            FocusZ = original.FocusZ,
            ForceFocus = original.ForceFocus,
        };
        return cpy;
    }
    public static CGameCtnMediaBlockDOF.Key DeepCopyDOFKey(CGameCtnMediaBlockDOF.Key original)
    {
        var cpy = new CGameCtnMediaBlockDOF.Key()
        {
            Time = original.Time,
            LensSize = original.LensSize,
            Target = original.Target,
            TargetPosition = original.TargetPosition,
            ZFocus = original.ZFocus,
        };
        return cpy;
    }
    public static CGameCtnMediaBlockCameraCustom.Key DeepCopyCustomCameraKey(CGameCtnMediaBlockCameraCustom.Key original)
    {
        var cpy = new CGameCtnMediaBlockCameraCustom.Key()
        {
            Time = original.Time,
            Anchor = original.Anchor,
            AnchorRot = original.AnchorRot,
            AnchorVis = original.AnchorVis,
            Fov = original.Fov,
            Interpolation = original.Interpolation,
            NearZ = original.NearZ,
            PitchYawRoll = original.PitchYawRoll,
            Position = original.Position,
            Target = original.Target,
            TargetPosition = original.TargetPosition,
            U01 = original.U01,
            U02 = original.U02,
            U03 = original.U03,
            U04 = original.U04,
            U05 = original.U05,
            U06 = original.U06,
            U07 = original.U07,
            U08 = original.U08,
            U09 = original.U09,
            LeftTangent = original.LeftTangent is not null ? DeepCopyInterpVal(original.LeftTangent) : null,
            RightTangent = original.RightTangent is not null ? DeepCopyInterpVal(original.RightTangent) : null,
        };
        return cpy;
    }
    public static CGameCtnMediaBlockCameraCustom.InterpVal DeepCopyInterpVal(CGameCtnMediaBlockCameraCustom.InterpVal original)
    {
        var cpy = new CGameCtnMediaBlockCameraCustom.InterpVal()
        {
            Fov = original.Fov,
            NearZ = original.NearZ,
            PitchYawRoll = original.PitchYawRoll,
            Position = original.Position,
            TargetPosition = original.TargetPosition,
            U01 = original.U01,
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
    public static string PlayerCameraTemplatePath = Path.Combine(WindowsUtils.MyDocumentsPath, @"Trackmania\Replays\Clips\Templates\PlayerCameraTemplate.Clip.Gbx");
    public static string DepthOfFieldTemplatePath = Path.Combine(WindowsUtils.MyDocumentsPath, @"Trackmania\Replays\Clips\Templates\DepthOfFieldTemplate.Clip.Gbx");
    public static string CustomCameraTemplatePath = Path.Combine(WindowsUtils.MyDocumentsPath, @"Trackmania\Replays\Clips\Templates\CustomCameraTemplate.Clip.Gbx");
    public static string PathCameraTemplatePath = Path.Combine(WindowsUtils.MyDocumentsPath, @"Trackmania\Replays\Clips\Templates\PathCameraTemplate.Clip.Gbx");
    public static string OrbitalCameraTemplatePath = Path.Combine(WindowsUtils.MyDocumentsPath, @"Trackmania\Replays\Clips\Templates\OrbitalCameraTemplate.Clip.Gbx");

    public static bool UseCustomTemplates = false;

    public static BlockTemplates CreateBlockTemplates()
    {
        CGameCtnMediaClip emptyTrackClip = UseCustomTemplates ? 
            Gbx.Parse<CGameCtnMediaClip>(TrackTemplatePath).Node : 
            Gbx.Parse<CGameCtnMediaClip>(TemplateLoader.GetTemplate("Triangle2DTemplate.Clip.Gbx")).Node;

        var emptyTrack = emptyTrackClip.Tracks[0];
        var emptyClip = DeepCopyClip(emptyTrackClip);
        emptyClip.Tracks.Clear();
        emptyTrack.Blocks.Clear();
        return new BlockTemplates(
            emptyClip,
            emptyTrack,
            UseCustomTemplates ? 
                (Gbx.Parse<CGameCtnMediaClip>(Triangles2DTemplatePath).Node.Tracks[0].Blocks[0] as CGameCtnMediaBlockTriangles2D)! :
                (Gbx.Parse<CGameCtnMediaClip>(TemplateLoader.GetTemplate("Triangle2DTemplate.Clip.Gbx")).Node.Tracks[0].Blocks[0] as CGameCtnMediaBlockTriangles2D)!,
            UseCustomTemplates ?
                (Gbx.Parse<CGameCtnMediaClip>(Triangles3DTemplatePath).Node.Tracks[0].Blocks[0] as CGameCtnMediaBlockTriangles3D)! :
                (Gbx.Parse<CGameCtnMediaClip>(TemplateLoader.GetTemplate("Triangle3DTemplate.Clip.Gbx")).Node.Tracks[0].Blocks[0] as CGameCtnMediaBlockTriangles3D)!,
            UseCustomTemplates ?
                (Gbx.Parse<CGameCtnMediaClip>(TextTemplatePath).Node.Tracks[0].Blocks[0] as CGameCtnMediaBlockText)! :
                (Gbx.Parse<CGameCtnMediaClip>(TemplateLoader.GetTemplate("TextTemplate.Clip.Gbx")).Node.Tracks[0].Blocks[0] as CGameCtnMediaBlockText)!,
            UseCustomTemplates ?
                (Gbx.Parse<CGameCtnMediaClip>(ImageTemplatePath).Node.Tracks[0].Blocks[0] as CGameCtnMediaBlockImage)! :
                (Gbx.Parse<CGameCtnMediaClip>(TemplateLoader.GetTemplate("ImageTemplate.Clip.Gbx")).Node.Tracks[0].Blocks[0] as CGameCtnMediaBlockImage)!,
            UseCustomTemplates ?
                (Gbx.Parse<CGameCtnMediaClip>(PlayerCameraTemplatePath).Node.Tracks[0].Blocks[0] as CGameCtnMediaBlockCameraGame)! :
                (Gbx.Parse<CGameCtnMediaClip>(TemplateLoader.GetTemplate("PlayerCameraTemplate.Clip.Gbx")).Node.Tracks[0].Blocks[0] as CGameCtnMediaBlockCameraGame)!,
            UseCustomTemplates ?
                (Gbx.Parse<CGameCtnMediaClip>(DepthOfFieldTemplatePath).Node.Tracks[0].Blocks[0] as CGameCtnMediaBlockDOF)! :
                (Gbx.Parse<CGameCtnMediaClip>(TemplateLoader.GetTemplate("DepthOfFieldTemplate.Clip.Gbx")).Node.Tracks[0].Blocks[0] as CGameCtnMediaBlockDOF)!,
            UseCustomTemplates ?
                (Gbx.Parse<CGameCtnMediaClip>(CustomCameraTemplatePath).Node.Tracks[0].Blocks[0] as CGameCtnMediaBlockCameraCustom)! :
                (Gbx.Parse<CGameCtnMediaClip>(TemplateLoader.GetTemplate("CustomCameraTemplate.Clip.Gbx")).Node.Tracks[0].Blocks[0] as CGameCtnMediaBlockCameraCustom)!,
            UseCustomTemplates ?
                (Gbx.Parse<CGameCtnMediaClip>(PathCameraTemplatePath).Node.Tracks[0].Blocks[0] as CGameCtnMediaBlockCameraPath)! :
                (Gbx.Parse<CGameCtnMediaClip>(TemplateLoader.GetTemplate("PathCameraTemplate.Clip.Gbx")).Node.Tracks[0].Blocks[0] as CGameCtnMediaBlockCameraPath)!,
            UseCustomTemplates ?
                (Gbx.Parse<CGameCtnMediaClip>(OrbitalCameraTemplatePath).Node.Tracks[0].Blocks[0] as CGameCtnMediaBlockCameraOrbital)! :
                (Gbx.Parse<CGameCtnMediaClip>(TemplateLoader.GetTemplate("OrbitalCameraTemplate.Clip.Gbx")).Node.Tracks[0].Blocks[0] as CGameCtnMediaBlockCameraOrbital)!
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

    public static Trigger CreateEmptyTrigger()
    {
        var trigger = new Trigger()
        {
            U01 = -1,
            U02 = -1,
            U03 = -1,
            U04 = 0,
            Coords = []
        };
        return trigger;
    }

}
