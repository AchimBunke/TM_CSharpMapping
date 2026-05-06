using TM_GenericMapping.Common;
using static GBX.NET.Engines.Game.CGameCtnMediaBlockCameraGame;

namespace TM_GenericMapping.MediaTracker;

public class PlayerCameraObject : CameraObject
{
    public EGameCam CameraType { get; set; }
    public PlayerCameraObject(
        EGameCam cameraType = EGameCam.Default) : base(new PlayerCameraRenderer())
    {
        CameraType = cameraType;
        Name = "PlayerCamera";
    }

    public override MediaObject Clone()
    {
       return new PlayerCameraObject(CameraType);
    }
}
