using GBX.NET.Engines.Game;
using TM_GenericMapping.Abstractions;
using static GBX.NET.Engines.Game.CGameCtnMediaBlock;

namespace TM_GenericMapping.Common;

public static class TracksUtils
{
    public static int GetVertexCount(MediaTrack track)
    {
        return GetVertexCount(track.Track);
    }
    public static int GetTriangleCount(MediaTrack track)
    {
        return GetTriangleCount(track.Track);
    }
    public static int GetVertexCount(CGameCtnMediaTrack track)
    {
        return track.Blocks.OfType<CGameCtnMediaBlockTriangles>().Sum(b => b.Vertices.Length);
    }
    public static int GetTriangleCount(CGameCtnMediaTrack track)
    {
        return track.Blocks.OfType<CGameCtnMediaBlockTriangles>().Sum(b => b.Triangles.Length);
    }

    public static int GetKeyFrameCount(CGameCtnMediaTrack track)
        => track.Blocks.OfType<IHasKeys>().Sum(hk => hk.Keys.Count());

    public static int GetKeyFrameCount(MediaTrack track)
       => GetKeyFrameCount(track.Track);

    public static void MakeTrianglesRelative(CGameCtnMediaTrack track, bool ensureVisibility = true, float ensuranceVerticesDistance = 10000)
    {
        foreach (var block in track.Blocks.OfType<CGameCtnMediaBlockTriangles3D>())
            BlocksUtils.MakeTrianglesRelative(block, ensureVisibility, ensuranceVerticesDistance);
    }

}
