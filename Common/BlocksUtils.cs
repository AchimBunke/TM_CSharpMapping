using GBX.NET;
using GBX.NET.Engines.Game;

namespace TM_GenericMapping.Common;

public static class BlocksUtils
{
    extension(CGameCtnBlock block)
    {
        public Vec3 AbsolutePositionInMapSave
        {
            get => block.IsFree ? (Vec3)block.AbsolutePositionInMap : BlockPositionToAbsolutePosition(block.Coord, block.Direction);
        }
        public Vec3 PitchYawRollSave
        {
            get => block.IsFree ? new Vec3(block.PitchYawRoll!.Value.Y, block.PitchYawRoll!.Value.X, block.PitchYawRoll!.Value.Z) : BlockDirectionToPitchYawRoll(block.Direction);
        }
    }
    extension(CGameCtnChallenge challenge)
    {
        public CGameCtnAnchoredObject PlaceAnchoredObjectCorrect(Ident itemModel, Vec3 absolutePosition, Vec3 pitchYawRoll, Vec3 offsetPivot = default)
        {
            return challenge.PlaceAnchoredObject(itemModel, absolutePosition, (pitchYawRoll.Y, pitchYawRoll.X, pitchYawRoll.Z), offsetPivot);
        }
    }

    public static Vec3 BlockPositionToAbsolutePosition(Int3 blockPosition, Direction blockDirection)
    {
        return new Vec3(
            (blockPosition.X + blockDirection switch
            {
                Direction.North => 0f,
                Direction.East => 1f,
                Direction.South => 1f,
                Direction.West => 0f,
                _ => throw new NotImplementedException(),
            }) * 32f, 
            (blockPosition.Y - 8f) * 8f,
            (blockPosition.Z + blockDirection switch
            {
                Direction.North => 0f,
                Direction.East => 0f,
                Direction.South => 1f,
                Direction.West => 1f,
                _ => throw new NotImplementedException(),
            }) * 32f);
    }
    public static Vec3 BlockDirectionToPitchYawRoll(Direction blockDirection)
        => blockDirection switch
        {
            Direction.North => new Vec3(0f, 0f, 0f) * MathUtils.Deg2Rad,
            Direction.East => new Vec3(0f, -90f, 0f) * MathUtils.Deg2Rad,
            Direction.South => new Vec3(0f, -180f, 0f) * MathUtils.Deg2Rad,
            Direction.West => new Vec3(0f, 90f, 0f) * MathUtils.Deg2Rad,
            _ => throw new NotImplementedException(),
        } * MathUtils.Deg2Rad;
}
