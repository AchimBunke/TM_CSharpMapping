using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Plug;
using static GBX.NET.Engines.Plug.CPlugSurface;

namespace TM_GenericMapping.Items;

public static class ChunkSafeItemOperations
{

    public static void NullifyIcon(CGameItemModel item)
        => SetIcon(item, null, null);
    
    public static void SetIcon(CGameItemModel item, GBX.NET.Color[,]? icon, byte[]? iconWebP)
    {
        item.Icon = icon;
        item.IconWebP = iconWebP;

        if (icon == null && iconWebP == null)
        {
            // pretty sure this is necessary to remove
            item.RemoveChunk<CGameCtnCollector.HeaderChunk2E001004>();
            // not sure if this is nessecary but it works
            item.Flags = (CGameCtnCollector.ECollectorFlags)8;
        }
        else
        {
            // this chunk saves icon data
            if (!item.TryCreateChunk<CGameCtnCollector.HeaderChunk2E001004>(out var iconChunk))
            {
                iconChunk = item.GetChunk<CGameCtnCollector.HeaderChunk2E001004>();
            }
            // not sure why but every item has this
            iconChunk!.U01 = 1;
            // idk
            iconChunk.IsHeavy = true;
            // this flag seems to be required for the game to load the icon
            item.Flags = (CGameCtnCollector.ECollectorFlags)16;
        }
    } 


    public static void SetSurfaceGameplayId(CPlugSurface surface, LegacyGameplayId gameplayId)
        => SetSurfaceGameplayId(surface, (ushort)gameplayId);
    public static void SetSurfaceGameplayId(CPlugSurface surface, ushort gameplayId)
    {
        var chunk = surface.GetChunk<Chunk0900C003>();
        chunk.U04 = [gameplayId];
    }
    public static ushort GetSurfaceGameplayId(CPlugSurface surface)
    {
        var chunk = surface.GetChunk<Chunk0900C003>();
        if (chunk?.U04?.Length > 0)
            return chunk.U04[0];
        return 0;
    }
}
