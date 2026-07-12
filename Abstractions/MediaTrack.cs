using GBX.NET.Engines.Game;

namespace TM_GenericMapping.Abstractions;

public enum MediaTrackType
{
    Empty,
    Text,
    Triangles3D,
    Triangles2D,
    Image,
    CustomCamera,
    PathCamera,
    OrbitalCamera,
    PlayerCamera,
    DepthOfField,
    NotSupported
}
public class MediaTrack
{
    public CGameCtnMediaTrack Track { get; init; }
    public MediaTrackType TrackType { get; init; }
    public MediaTrack(CGameCtnMediaTrack track)
    {
        Track = track;
        if (track.Blocks.Count == 0)
        {
            TrackType = MediaTrackType.Empty;
        }
        else
        {
            var firstBlock = track.Blocks[0];
            TrackType = firstBlock switch
            {
                CGameCtnMediaBlockTriangles2D => MediaTrackType.Triangles2D,
                CGameCtnMediaBlockTriangles3D => MediaTrackType.Triangles3D,
                CGameCtnMediaBlockImage => MediaTrackType.Image,
                CGameCtnMediaBlockText => MediaTrackType.Text,
                CGameCtnMediaBlockCameraCustom => MediaTrackType.CustomCamera,
                CGameCtnMediaBlockCameraPath => MediaTrackType.PathCamera,
                CGameCtnMediaBlockCameraOrbital => MediaTrackType.OrbitalCamera,
                CGameCtnMediaBlockCameraGame => MediaTrackType.PlayerCamera,
                CGameCtnMediaBlockDOF => MediaTrackType.DepthOfField,
                _ => MediaTrackType.NotSupported,
            };
        }
    }
}
