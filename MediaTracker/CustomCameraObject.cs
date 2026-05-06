using GBX.NET.Engines.Game;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using TM_GenericMapping.Common;

namespace TM_GenericMapping.MediaTracker;

public class CustomCameraObject : CameraObject
{
    public int Anchor { get; set; } = 0;
    public bool AnchorRotation { get; set; } = false;
    public bool AnchorVisibility { get; set; } = true;
    public float FOV { get; set; } = 80f;
    public CGameCtnMediaBlockCameraCustom.Interpolation Interpolation { get; set; } = CGameCtnMediaBlockCameraCustom.Interpolation.None;
    public float NearClipPlane { get; set; } = 0.05f;
    public int Target { get; set; } = -1;
    public Vector3 TargetPosition { get; set; } = Vector3.Zero;

    public CustomCameraObject([NotNull] CustomCameraRenderer renderer = null!) : base(renderer ?? new CustomCameraRenderer())
    {
    }

    public override MediaObject Clone()
    {
        throw new NotImplementedException();
    }
}
