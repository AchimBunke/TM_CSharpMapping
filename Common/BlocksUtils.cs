using GBX.NET;
using GBX.NET.Engines.Game;
using TM_GenericMapping.Abstractions;
using static TM_GenericMapping.Common.Triangle3DRenderer;

namespace TM_GenericMapping.Common;

public static class BlocksUtils
{
    extension(CGameCtnBlock block)
    {
        /// <summary>
        /// Gets AbsolutePositionInMap even if block is not in free mode.
        /// </summary>
        public Vec3 AbsolutePositionInMapSave
        {
            get => block.IsFree ? (Vec3)block.AbsolutePositionInMap! : BlockPositionToAbsolutePosition(block.Coord, block.Direction);
        }
        /// <summary>
        /// Gets YawPitchRoll even if block is not in free mode.
        /// </summary>
        public Vec3 YawPitchRollSave
        {
            get => block.IsFree ? new Vec3(block.YawPitchRoll!.Value.X, block.YawPitchRoll!.Value.Y, block.YawPitchRoll!.Value.Z) : BlockDirectionToYawPitchRoll(block.Direction);
        }
    }
    extension(CGameCtnChallenge challenge)
    {
        [Obsolete("Did fix PitchYawRoll before it was changed")]
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
    public static Vec3 BlockDirectionToYawPitchRoll(Direction blockDirection)
        => blockDirection switch
        {
            Direction.North => new Vec3(0f, 0f, 0f),
            Direction.East => new Vec3(-90f, 0f, 0f),
            Direction.South => new Vec3(180f, 0f, 0f),
            Direction.West => new Vec3(90f, 0f, 0f),
            _ => throw new NotImplementedException(),
        } * MathUtils.Deg2Rad;

    public static void MakeTrianglesRelative(CGameCtnMediaBlockTriangles3D triangleBlock, bool ensureVisibility = true, float ensuranceVerticesDistance = 10000)
    {

        triangleBlock.Chunks.Remove<CGameCtnMediaBlockTriangles3D.Chunk03029002>();
        var chunk = triangleBlock.CreateChunk<CGameCtnMediaBlockTriangles3D.Chunk03029002>();
        chunk.U01 = 0;

        if (ensureVisibility)
        {
            triangleBlock.Vertices = [
                ..triangleBlock.Vertices,
                ..Enumerable.Repeat(new Vec4(), 4)
                ];
            foreach(var key in triangleBlock.Keys)
            {
                key.Positions = [
                    ..key.Positions,
                    new Vec3(-ensuranceVerticesDistance, 0, -ensuranceVerticesDistance),
                    new Vec3(-ensuranceVerticesDistance, 0, ensuranceVerticesDistance),
                    new Vec3(ensuranceVerticesDistance, 0, -ensuranceVerticesDistance),
                    new Vec3(ensuranceVerticesDistance, 0, ensuranceVerticesDistance)
                ];
            }
        }

    }

}
