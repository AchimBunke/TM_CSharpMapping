using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.Plug;
using TM_GenericMapping.Messaging;

namespace TM_GenericMapping.Items;

public class ItemAttachmentCreator
{
    public ToolResult<CGameItemModel> AddAttachment(CGameItemModel itemModel, ItemAttachmentLocation attachmentLocation)
        => AddAttachments(itemModel, [attachmentLocation], false);
    public ToolResult<CGameItemModel> AddAttachmentGroup(CGameItemModel itemModel, List<ItemAttachmentLocation> attachmentLocations)
        => AddAttachments(itemModel, attachmentLocations, true);

    ToolResult<CGameItemModel> AddAttachments(CGameItemModel itemModel, List<ItemAttachmentLocation> attachmentLocations, bool isGroup)
    {
        if(attachmentLocations == null || attachmentLocations.Count == 0)
            return ToolResult.Fail(nameof(ItemAttachmentCreator), ErrorCodes.ItemAttachmentCreator.InvalidAttachmentLocations);
        CPlugPrefab? prefab = null;
        if (itemModel.EntityModel is CPlugPrefab emp)
        {
            prefab = emp;
        }
        else if (itemModel.EntityModel is NPlugItem_SVariantList variantList)
        {
            foreach (var variant in variantList.Variants ?? [])
            {
                if (variant.EntityModel is CPlugPrefab vp)
                {
                    prefab = vp;
                    break;
                }
            }
        }
        if (prefab is null)
            return ToolResult.Fail(nameof(ItemAttachmentCreator), ErrorCodes.ItemAttachmentCreator.UnsupportedEntityModel);
        
        var placementRef = GbxItemUtils.CreateEntRef();
        if (!isGroup)
        {
            var sPlacement = ItemPlacementUtils.CreateSPlacement(attachmentLocations[0], out var pos, out var rot);
            placementRef.Position = pos;
            placementRef.Rotation = rot;
            placementRef.Params = sPlacement;
        }
        else
        {
            var sPlacementGroup = ItemPlacementUtils.CreateSPlacementGroup(attachmentLocations.ToArray());
            placementRef.Params = sPlacementGroup;
        }
        prefab.Ents = [.. prefab.Ents, placementRef];

        return ToolResult.Success(itemModel, nameof(ItemAttachmentCreator));
    }

}
