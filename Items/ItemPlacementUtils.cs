using GBX.NET;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Plug;
using System.Buffers.Binary;

namespace TM_GenericMapping.Items;

public static class ItemPlacementUtils
{
    /// <summary>
    /// Not exhaustive list..
    /// </summary>
    public enum PlacementPatchGroups
    {
        Border,
        BorderInGround,
        BorderOpen,
        BorderWaterSmall,
        BorderPlatform,
        BorderOpenCurve1,
        StructureSupport,
        BorderPlatformWater,
        BorderDecoWall,
        BorderTrackWall,
        Road,
        RoadDirt,
        RoadIce,
        RoadOpen,
        RoadWater,
        RoadSlopeSoft,
        RoadSlopeHard,
        RoadBump,
        RoadTilt,
        RoadWarp,
        RoadDirtTilt,
        RoadDirtWarp,
        RoadIceWarp,
        RoadIceTilt,
        RoadOpenTilt,
        RoadOpenWarp,
        RoadNarrowCenter,
        RoadNarrowLeft,
        RoadNarrowRight,
        BorderRallyRoadDirt,
        BorderMultiSize,

    }

    public enum FillAlign
    {
        Center = 0,
        Begin = 1,
        End = 2,
    }
    public enum FillDir
    {
        U,
        V,
    }

    public record struct MagnetLoc(Vec3 Position, Vec3 PitchYawRoll);
    public static void SetItemPlacementClass(CGameItemPlacementParam placementParam, NPlugItemPlacement_SClass placementClass)
    {
        placementParam.PlacementClass = placementClass;
        placementParam.TryCreateChunk<CGameItemPlacementParam.Chunk2E020005>(out _);
    }
    public static void RemoveItemPlacementClass(CGameItemPlacementParam placementParam)
    {
        placementParam.PlacementClass = null;
        placementParam.RemoveChunk<CGameItemPlacementParam.Chunk2E020005>();
    }



    public static void SetItemPlacementMagnets(CGameItemPlacementParam placementParam, List<MagnetLoc> magnets)
    {
        if(placementParam.TryCreateChunk<CGameItemPlacementParam.Chunk2E020004>(out var chunk)) { }
        chunk.Data = WriteMagnets(magnets);
    }
    public static List<MagnetLoc> GetItemPlacementMagnets(CGameItemPlacementParam placementParam)
    {
        var chunk = placementParam.GetChunk<CGameItemPlacementParam.Chunk2E020004>();
        return chunk != null ? ReadMagnets(chunk.Data) : new List<MagnetLoc>();
    }
    

    static List<MagnetLoc> ReadMagnets(byte[] data)
    {
        var span = data.AsSpan();
        var count = BinaryPrimitives.ReadInt32LittleEndian(span[4..]);
        var magnets = new List<MagnetLoc>(count);
        var o = 8;
        for (var i = 0; i < count; i++)
        {
            var x = BinaryPrimitives.ReadSingleLittleEndian(span[o..]);
            o += 4;
            var y = BinaryPrimitives.ReadSingleLittleEndian(span[o..]);
            o += 4;
            var z = BinaryPrimitives.ReadSingleLittleEndian(span[o..]);
            o += 4;
            var yaw = BinaryPrimitives.ReadSingleLittleEndian(span[o..]);
            o += 4;
            var pitch = BinaryPrimitives.ReadSingleLittleEndian(span[o..]);
            o += 4;
            var roll = BinaryPrimitives.ReadSingleLittleEndian(span[o..]);
            o += 4;
            magnets.Add(new MagnetLoc(new Vec3(x, y, z), new Vec3(pitch, yaw, roll)));
        }
        return magnets;
    }
    static byte[] WriteMagnets(IReadOnlyList<MagnetLoc> magnets)
    {
        var count = magnets.Count;

        var data = new byte[8 + count * 24];
        var span = data.AsSpan();

        BinaryPrimitives.WriteUInt32LittleEndian(span, 0);                // version/unknown
        BinaryPrimitives.WriteInt32LittleEndian(span[4..], count);

        var o = 8;
        for (var i = 0; i < count; i++)
        {
            var (p, pitchYawRoll) = magnets[i];


            WriteFloat(span, ref o, p.X);
            WriteFloat(span, ref o, p.Y);
            WriteFloat(span, ref o, p.Z);
            WriteFloat(span, ref o, pitchYawRoll.Y);     // stored order: yaw, pitch, roll
            WriteFloat(span, ref o, pitchYawRoll.X);
            WriteFloat(span, ref o, pitchYawRoll.Z);
        }

        return data;
    }
    static void WriteFloat(Span<byte> span, ref int offset, float value)
    {
        BinaryPrimitives.WriteSingleLittleEndian(span[offset..], value);
        offset += 4;
    }

}
