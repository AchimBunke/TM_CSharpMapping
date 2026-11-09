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



    //public static void SetItemPlacementMagnets(CGameItemPlacementParam placementParam, List<MagnetLoc> magnets)
    //{
    //    if(placementParam.TryCreateChunk<CGameItemPlacementParam.Chunk2E020004>(out var chunk)) { }
    //    chunk.Data = WriteMagnets(magnets);
    //}
    //public static List<MagnetLoc> GetItemPlacementMagnets(CGameItemPlacementParam placementParam)
    //{
    //    var chunk = placementParam.GetChunk<CGameItemPlacementParam.Chunk2E020004>();
    //    return chunk != null ? ReadMagnets(chunk.Data) : new List<MagnetLoc>();
    //}
    

    //static List<MagnetLoc> ReadMagnets(byte[] data)
    //{
    //    var span = data.AsSpan();
    //    var count = BinaryPrimitives.ReadInt32LittleEndian(span[4..]);
    //    var magnets = new List<MagnetLoc>(count);
    //    var o = 8;
    //    for (var i = 0; i < count; i++)
    //    {
    //        var x = BinaryPrimitives.ReadSingleLittleEndian(span[o..]);
    //        o += 4;
    //        var y = BinaryPrimitives.ReadSingleLittleEndian(span[o..]);
    //        o += 4;
    //        var z = BinaryPrimitives.ReadSingleLittleEndian(span[o..]);
    //        o += 4;
    //        var yaw = BinaryPrimitives.ReadSingleLittleEndian(span[o..]);
    //        o += 4;
    //        var pitch = BinaryPrimitives.ReadSingleLittleEndian(span[o..]);
    //        o += 4;
    //        var roll = BinaryPrimitives.ReadSingleLittleEndian(span[o..]);
    //        o += 4;
    //        magnets.Add(new MagnetLoc(new Vec3(x, y, z), new Vec3(pitch, yaw, roll)));
    //    }
    //    return magnets;
    //}
    //static byte[] WriteMagnets(IReadOnlyList<MagnetLoc> magnets)
    //{
    //    var count = magnets.Count;

    //    var data = new byte[8 + count * 24];
    //    var span = data.AsSpan();

    //    BinaryPrimitives.WriteUInt32LittleEndian(span, 0);                // version/unknown
    //    BinaryPrimitives.WriteInt32LittleEndian(span[4..], count);

    //    var o = 8;
    //    for (var i = 0; i < count; i++)
    //    {
    //        var (p, pitchYawRoll) = magnets[i];


    //        WriteFloat(span, ref o, p.X);
    //        WriteFloat(span, ref o, p.Y);
    //        WriteFloat(span, ref o, p.Z);
    //        WriteFloat(span, ref o, pitchYawRoll.Y);     // stored order: yaw, pitch, roll
    //        WriteFloat(span, ref o, pitchYawRoll.X);
    //        WriteFloat(span, ref o, pitchYawRoll.Z);
    //    }

    //    return data;
    //}
    //static void WriteFloat(Span<byte> span, ref int offset, float value)
    //{
    //    BinaryPrimitives.WriteSingleLittleEndian(span[offset..], value);
    //    offset += 4;
    //}

    private const int GhostModeBit = 5;
    private const int HasPathBit = 6;
    private const int IsFreelyAnchorableBit = 0;

    extension(CGameItemPlacementParam placementParam)
    {

        public bool GhostMode
        {
            get
            {
                return (placementParam.Flags & (1 << GhostModeBit)) != 0;
            }
            set
            {
                if (value) placementParam.Flags |= 1 << GhostModeBit;
                else placementParam.Flags &= ~(1 << GhostModeBit);
            }
        }
        public bool HasPath
        {
            get
            {
                return (placementParam.Flags & (1 << HasPathBit)) != 0;
            }
            set
            {
                if (value) placementParam.Flags |= 1 << HasPathBit;
                else placementParam.Flags &= ~(1 << HasPathBit);
            }
        }
        public bool IsFreelyAnchorable
        {
            get
            {
                return (placementParam.Flags & (1 << IsFreelyAnchorableBit)) != 0;
            }
            set
            {
                if (value) placementParam.Flags |= 1 << IsFreelyAnchorableBit;
                else placementParam.Flags &= ~(1 << IsFreelyAnchorableBit);
            }
        }
    }


    // known types
    public enum PlacementType
    {
        Bush,
        Cactus,
        CherryTree,
        Cypress,
        CypressDirt,
        Fir,
        FirSnow,
        Flag,
        Flower,
        Fogger,
        Lamp,
        LightTube,
        PalmTree,
        PalmTreeSugar,
        Pillar,
        Plant,
        Podium,
        Ramp,
        Rig,
        RoadSign,
        RotorFrame,
        Screen,
        Show,
        Spectator,
        SpringTree,
        SupportTruss,
        SupportTrussConnector,
        TrackBarrier,
        Tree,
        TreeBushy,
        TreeFir,
        TreePineDead,
        TreeThin,
        TreeThinBushy,
        TunnelSupportArch,
        TunnelSupportArchCenter,
        TunnelSupportHalfArch,
        TunnelSupportPillar,
        TunnelSupportPillarLarge
    }

    public enum PlacementSize
    {
        _16m,
        _1x1,
        _2m,
        _2x1,
        _32m,
        _4m,
        _6x1,
        _8m,
        Big,
        Curve1,
        Curve2,
        Curve3,
        Medium,
        Small,
        Tall,
        VerySmall
    }

    public enum PlacementLayout
    {
        Back,
        BackStage,
        Ecotone,
        Forest,
        Front,
        Grove,
        PalmEcotone,
        PalmForest,
        PalmGrove,
        Show,
        ShowFull,
        ShowRace,
        Synthetic,
        Top_Bottom,
        Wild
    }

    public enum PlacementMatModifier
    {
        Dirt,
        Grass,
        Ice
    }

    public enum PlacementVariant
    {
        A,
        B,
        C,
        D,
        DiagCube,
        DiagSquare,
        E,
        F,
        In,
        Light,
        OnTruss,
        OnTruss16m,
        OnTruss8m,
        Out,
        Speaker,
        Str,
        Curve
    }

    public static KeyValuePair<string, string> GetPlacementTypeTag(PlacementType type)
    => KeyValuePair.Create("Type", type.ToString());
    public static KeyValuePair<string, string> GetPlacementSizeTag(PlacementSize size)
        => KeyValuePair.Create("Size", size.ToString().Replace("_", ""));
    public static KeyValuePair<string, string> GetPlacementLayoutTag(PlacementLayout placement)
        => KeyValuePair.Create("Placement", placement.ToString().Replace("_", "/"));
    public static KeyValuePair<string, string> GetPlacementMatModifierTag(PlacementMatModifier matModifier)
        => KeyValuePair.Create("MatModifier", matModifier.ToString());
    public static KeyValuePair<string, string> GetPlacementVariantTag(PlacementVariant variant)
       => KeyValuePair.Create("Variant", variant.ToString());

    public static KeyValuePair<string, string> GetPlacementSlot(int slot)
       => KeyValuePair.Create("Slot", slot.ToString());
    public static KeyValuePair<string, string> GetPlacementPilotSlot(int slot)
       => KeyValuePair.Create("PilotSlot", slot.ToString());
}
