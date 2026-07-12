using GBX.NET.Engines.Game;
using System.Numerics;

namespace TM_GenericMapping.Common;

public static class ItemUtils
{
    extension(CGameCtnAnchoredObject anchoredObject)
    {
        public Vector3 AbsoluteCenterPositionInMap
            => anchoredObject.AbsolutePositionInMap.ToVector3() + Vector3.Transform(anchoredObject.PivotPosition, Quaternion.CreateFromItemYawPitchRoll(anchoredObject.YawPitchRoll));
    }
}
