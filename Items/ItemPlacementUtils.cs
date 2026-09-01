using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.Plug;

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
}
