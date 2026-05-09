using GBX.NET.Engines.GameData;

namespace TM_GenericMapping.Items;

public static class ChunkSafeItemOperations
{
    public static void SetIcon(CGameItemModel item, GBX.NET.Color[,]? icon, byte[] iconWebP)
    {
        item.Icon = icon;
        item.IconWebP = iconWebP;

        if (icon == null && iconWebP == null)
        {
            item.RemoveChunk<CGameCtnCollector.HeaderChunk2E001004>();
            item.Flags = (CGameCtnCollector.ECollectorFlags)8;
        }
        else
        {
            if(!item.TryCreateChunk<CGameCtnCollector.HeaderChunk2E001004>(out var iconChunk))
            {
                iconChunk = item.GetChunk<CGameCtnCollector.HeaderChunk2E001004>();
            }
            iconChunk.U01 = 1;
            iconChunk.IsHeavy = true;
            item.Flags = (CGameCtnCollector.ECollectorFlags)16;
        }
    } 
}
