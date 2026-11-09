using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.Plug;
using TM_GenericMapping.Messaging;

namespace TM_GenericMapping.Items;

public class ItemPodiumCreator
{
    public ToolResult<CGameItemModel> ConvertToPodium(CGameItemModel itemModel)
    {
        CPlugPrefab? prefab = null;
        if (itemModel.EntityModel is CPlugPrefab emp) 
        {
            prefab = emp;
        }
        else if (itemModel.EntityModel is NPlugItem_SVariantList variantList)
        {
            foreach(var variant in variantList.Variants ?? [])
            {
                if (variant.EntityModel is CPlugPrefab vp)
                {
                    prefab = vp;
                    break;
                }
            }
        }
        if (prefab is null)
            return ToolResult.Fail(nameof(ItemPodiumCreator), ErrorCodes.ItemPodiumCreator.UnsupportedEntityModel);

        var podiumClipList = itemModel.PodiumClipList = new CPlugMediaClipList();
        podiumClipList.CreateChunk<CPlugMediaClipList.Chunk09189000>();
        podiumClipList.MediaClipFids = [];

        return ToolResult.Success(itemModel, nameof(ItemPodiumCreator));


    }
}
