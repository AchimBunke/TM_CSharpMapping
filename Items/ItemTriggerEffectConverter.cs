using GBX.NET;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Plug;
using TM_GenericMapping.Common;
using TM_GenericMapping.Templating;

namespace TM_GenericMapping.Items;

public class ItemTriggerEffectConverter
{

    public static Dictionary<LegacyGameplayId, ushort> GameplayIdToShort = new Dictionary<LegacyGameplayId, ushort>()
    {
        [LegacyGameplayId.None] = 0x0000,
        [LegacyGameplayId.Turbo] = 0x0100,
        [LegacyGameplayId.Turbo2] = 0x0200,
        [LegacyGameplayId.TurboRoulette] = 0x0300,
        [LegacyGameplayId.FreeWheeling] = 0x0400,
        [LegacyGameplayId.NoGrip] = 0x0500,
        [LegacyGameplayId.NoSteering] = 0x0600,
        [LegacyGameplayId.ForceAcceleration] = 0x0700,
        [LegacyGameplayId.Reset] = 0x0800,
        [LegacyGameplayId.SlowMotion] = 0x0900,
        [LegacyGameplayId.Bumper] = 0x0A00,
        [LegacyGameplayId.Bumper2] = 0x0B00,
        [LegacyGameplayId.Boost1LegacyUp] = 0x0C00,
        [LegacyGameplayId.Fragile] = 0x0D00,
        [LegacyGameplayId.Boost2LegacyUp] = 0x0E00,
        //[LegacyGameplayId.Bouncy] = 0x0F,
        [LegacyGameplayId.NoBrakes] = 0x1000,
        [LegacyGameplayId.Cruise] = 0x1100,
        [LegacyGameplayId.ReactorBoost_Oriented] = 0x1200,
        [LegacyGameplayId.ReactorBoost2_Oriented] = 0x1300,
        [LegacyGameplayId.VehicleTransform_Reset] = 0x1400,
        [LegacyGameplayId.VehicleTransform_CarSnow] = 0x1500,
        [LegacyGameplayId.VehicleTransform_CarRally] = 0x1600,
        [LegacyGameplayId.VehicleTransform_CarDesert] = 0x1700,
    };
    public static LegacyGameplayId ShortToGameplayId(ushort gameplay) => GameplayIdToShort.FirstOrDefault(x => x.Value == gameplay).Key;

    public static bool TryConvertEffect(LegacyGameplayId gameplayId, CGameItemModel item)
        => TryConvertEffect(GameplayIdToShort[gameplayId], item);
    public static void ConvertEffect(LegacyGameplayId gameplayId, NPlugTrigger_SSpecial triggerSpecial)
        => ConvertEffect(GameplayIdToShort[gameplayId], triggerSpecial);

    static bool TryConvertEffect(ushort gameplay, CGameItemModel item)
    {
        if (item.EntityModel is CGameCommonItemEntityModel commonEntityModel && commonEntityModel.TriggerShape != null)
            ConvertCommonItemEntityModelToCPlugPrefab(item);
        if(item.EntityModel is not CPlugPrefab prefab)
            return false;
        foreach (var entRef in prefab.Ents)
        {
            if (entRef.Model is not NPlugTrigger_SSpecial triggerSpecial)
                continue;
            ConvertEffect(gameplay, triggerSpecial);
        }
        return true;
    }

    static void ConvertEffect(ushort gameplay, NPlugTrigger_SSpecial triggerSpecial)
    {
        var surface = triggerSpecial.TriggerShape!;
        var chunk = surface.Chunks.Get<CPlugSurface.Chunk0900C003>()!;
        chunk.U02 = [gameplay];
    }

    static void ConvertCommonItemEntityModelToCPlugPrefab(CGameItemModel item)
    {   
        var entityModel = item.EntityModel as CGameCommonItemEntityModel;
        var prefab = (GbxTemplateLibrary.CreateTriggerItemTemplate().Value.EntityModel as CPlugPrefab)!;
        prefab.Ents[0].Model = entityModel!.StaticObject!;
        var triggerSpecial = (prefab.Ents[1].Model as NPlugTrigger_SSpecial)!;
        triggerSpecial.TriggerShape = entityModel.TriggerShape as CPlugSurface;
        item.EntityModel = prefab;
        item.Chunks.Get<CGameItemModel.Chunk2E00201F>()!.U08 = 0; // necessary otherwise error
    }
}
