using GBX.NET.Engines.Game;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using TM_GenericMapping.Common;

namespace TM_GenericMapping.MediaTracker;

public interface IRenderingCamera
{
    Vector3 GetPosition();
    Quaternion GetRotation();
    float GetFOV();
    float GetNearClipPlane();

}
public abstract class CameraObject : RenderObject
{
    protected CameraObject([NotNull] IRenderer renderer) : base(renderer)
    {
    }

    protected CameraObject(CameraObject other) : base(other)
    {
    }

}


public abstract class PositionalCamera : CameraObject, IRenderingCamera
{
    public int Anchor { get; set; } = -1;
    public bool AnchorRotation { get; set; } = false;
    public bool AnchorVisibility { get; set; } = true;
    public float FOV { get; set; } = 80f;
    public float NearClipPlane { get; set; } = 0.05f;
    public int Target { get; set; } = -1;
    public Vector3 TargetPosition { get; set; } = Vector3.Zero;

    protected PositionalCamera([NotNull] IRenderer renderer) : base(renderer)
    {
    }
    protected PositionalCamera(PositionalCamera other) : base(other)
    {
    }

    public Vector3 GetPosition()
        => Position;

    public Quaternion GetRotation()
        => Rotation;

    public float GetFOV()
        => FOV;

    public float GetNearClipPlane()
        => NearClipPlane;
}

public class CustomCameraObject : PositionalCamera
{
    public CGameCtnMediaBlockCameraCustom.Interpolation Interpolation { get; set; } = CGameCtnMediaBlockCameraCustom.Interpolation.None;

    public CustomCameraObject(CustomCameraRenderer? renderer = null) : base(renderer ?? new CustomCameraRenderer())
    {
        Name = "CustomCamera";
    }

    public override MediaObject Clone()
    {
        throw new NotImplementedException();
    }
}
public class PathCameraObject : PositionalCamera
{
    public float Weight { get; set; } = 1f;
    public PathCameraObject(PathCameraRenderer? renderer = null) : base(renderer ?? new PathCameraRenderer())
    {
        Name = "PathCamera";
    }

    public override MediaObject Clone()
    {
        throw new NotImplementedException();
    }
}

public class OrbitalCameraObject : CameraObject, IRenderingCamera
{
    public int Target { get; set; } = -1;
    public Vector3 TargetPosition { get; set; } = Vector3.Zero;
    public float Latitude { get; set; }
    public float Longitude { get; set; }
    public float Radius { get; set; }
    public float FOV { get; set; } = 60f;

    public OrbitalCameraObject(OrbitalCameraRenderer? renderer = null) : base(renderer ?? new OrbitalCameraRenderer())
    {
        Name = "OrbitalCamera";
    }
    public override MediaObject Clone()
    {
        throw new NotImplementedException();
    }

    public Vector3 GetPosition()
    {
        float lat = Latitude * MathF.PI / 180f;
        float lon = Longitude * MathF.PI / 180f;

        return TargetPosition + new Vector3(
            Radius * MathF.Cos(lat) * MathF.Sin(lon),
            Radius * MathF.Sin(lat),
            Radius * MathF.Cos(lat) * MathF.Cos(lon)
        );
    }

    public Quaternion GetRotation()
    {
        Vector3 position = GetPosition();
        Vector3 forward = Vector3.Normalize(TargetPosition - position);
        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
        Vector3 up = Vector3.Cross(right, forward);

        return Quaternion.CreateFromRotationMatrix(new Matrix4x4(
             right.X, right.Y, right.Z, 0,
             up.X, up.Y, up.Z, 0,
            -forward.X, -forward.Y, -forward.Z, 0,
             0, 0, 0, 1
        ));
    }

    public float GetFOV() => FOV;

    public float GetNearClipPlane() => 0.05f;
}
