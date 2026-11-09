using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.Plug;
using System.Numerics;
using TM_GenericMapping.Messaging;

namespace TM_GenericMapping.Items;

public class ItemPodiumCreator
{
    public struct PodiumSlot
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public int PlayerPosition;
        public bool Car;
    }

    ItemAttachmentCreator _attachmentCreator = new ItemAttachmentCreator();

    public ToolResult<CGameItemModel> ConvertToPodium(CGameItemModel itemModel,IEnumerable<PodiumSlot> podiumSlots)
        => ConvertToPodium(itemModel, podiumSlots.Select(s => new ItemAttachmentLocation(s.Position, s.Rotation, new ItemAttachmentDefinition(
            [
                new()
                {
                    ["Type"]="Podium",
                    [$"{(s.Car ? "Slot" : "PilotSlot")}"] = $"0{s.PlayerPosition}"
                }
            ],
            ILayout: 0)
            )).ToList());
    public ToolResult<CGameItemModel> ConvertToPodium(CGameItemModel itemModel, List<ItemAttachmentLocation> attachmentLocations)
    {
        var res = _attachmentCreator.AddAttachmentGroup(itemModel, attachmentLocations);
        if (res.IsFailure)
            return res;

        var podiumClipList = itemModel.PodiumClipList = new CPlugMediaClipList();
        podiumClipList.CreateChunk<CPlugMediaClipList.Chunk09189000>();
        podiumClipList.MediaClipFids = [];

        return ToolResult.Success(itemModel, nameof(ItemPodiumCreator));
    }
}
