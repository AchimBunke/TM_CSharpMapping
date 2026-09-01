using GBX.NET;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Plug;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using TM_GenericMapping.Messaging;
using TM_GenericMapping.Templating;

namespace TM_GenericMapping.Items;
public class ItemEffectVariantCreator
{
    const int effectIconSize = 24;

    Image<Rgba32> effectIconImage;
    public ItemEffectVariantCreator()
    {
        var stream = TemplateLoader.GetTemplate("icons-24.png");
        effectIconImage = Image.Load<Rgba32>(stream);
    }

    public ToolResult<(Stream itemStream, LegacyGameplayId gameplayId)[]> CreateAllGameplayVariants(Stream itemTemplateStream)
    {
        List<(Stream itemStream, LegacyGameplayId gameplayId)> streams = [];
        foreach (var gameplayId in Enum.GetValues<LegacyGameplayId>())
        {
            var result = CreateVariant(itemTemplateStream, gameplayId);
            if(result.IsFailure)
                return ToolResult.Fail(result);
            streams.Add((result.Value, gameplayId));
        }
        return ToolResult.Success(streams.ToArray(), nameof(ItemEffectVariantCreator));
    }
    public ToolResult<Stream> CreateVariant(Stream itemTemplateStream, LegacyGameplayId gameplayId)
    {
        var itemTemplate = Gbx.Parse<CGameItemModel>(itemTemplateStream).Node;
        itemTemplateStream.Seek(0, SeekOrigin.Begin);
        var result = CreateVariant(itemTemplate, gameplayId);
        if (result.IsFailure)
            return ToolResult.Fail(result);
        var memoryStream = new MemoryStream();
        itemTemplate.Save(memoryStream, new GbxWriteSettings { CloseStream = false });
        memoryStream.Seek(0, SeekOrigin.Begin);
        return ToolResult.Success<Stream>(memoryStream, nameof(ItemEffectVariantCreator));
    }
    ToolResult<None> CreateVariant(CGameItemModel item, LegacyGameplayId gameplayId)
    {
        if (!ItemTriggerEffectConverter.TryConvertEffect(gameplayId, item))
            return ToolResult.Fail(nameof(ItemEffectVariantCreator), ErrorCodes.ItemEffectVariantCreator.MissingTriggerSpecial);
        ChangeTextures(item, gameplayId);
        ChangeIcon(item, gameplayId);
        ChangeName(item, gameplayId);
        return ToolResult.Success(nameof(ItemEffectVariantCreator));
    }
    bool ChangeTextures(CGameItemModel item, LegacyGameplayId gameplayId)
    {
        if (item.EntityModel is not CPlugPrefab prefab)
            return false;
        bool changedTexture = false;
        foreach (var entRef in prefab.Ents)
        {
            if (entRef.Model is not CPlugStaticObjectModel staticModel)
                continue;
            foreach (var matContainer in staticModel.Mesh!.CustomMaterials ?? [])
            {
                var mat = matContainer.MaterialUserInst!;
                var newLink = ConvertLink(mat.Link!, gameplayId);
                if (string.IsNullOrEmpty(newLink))
                    continue;

                mat.Link = newLink;
                mat.IsUsingGameMaterial = true;
                mat.MaterialName = ConvertMaterialName(mat.Link, gameplayId);
                changedTexture = true;
            }

        }
        return changedTexture;
    }
    void ChangeIcon(CGameItemModel item, LegacyGameplayId gameplayId)
    {
        var icon = item.Icon;
        if (icon == null)
            return;
        var iconSize = icon.GetLength(0);
        var effectIconOffset = GameplayIdToIconOffset(gameplayId);
        if (effectIconOffset < 0)
            return;
        for (int y = 0; y < effectIconSize; ++y)
        {
            for (int x = 0; x < effectIconSize; ++x)
            {
                var pixelColorRgba = effectIconImage[effectIconOffset + effectIconSize - x - 1, effectIconSize - y - 1];
                var pixelColor = new GBX.NET.Color(pixelColorRgba.R, pixelColorRgba.G, pixelColorRgba.B, pixelColorRgba.A);
                icon[iconSize - x - 1, iconSize - y - 1] = pixelColor;
            }
        }

    }
    void ChangeName(CGameItemModel item, LegacyGameplayId gameplayId)
    {
        if (item.Name == null)
            return;
        if (!TryReplaceEffectInName(item.Name, gameplayId, out var newName))
            newName = $"{item.Name}_{GameplayIdToDisplayString(gameplayId)}";
        item.Name = newName;
    }
    public bool TryReplaceEffectInName(string name, LegacyGameplayId gameplayId, out string newName)
    {
        foreach (var id in Enum.GetValues<LegacyGameplayId>())
        {
            string idString = GameplayIdToDisplayString(id);
            if (name.Contains($"_{id}"))
            {
                newName = name.Replace($"_{id}", $"_{GameplayIdToDisplayString(gameplayId)}");
                return true;
            }
            else if (name.Contains($"_{idString}"))
            {
                newName = name.Replace($"_{idString}", $"_{GameplayIdToDisplayString(gameplayId)}");
                return true;
            }
            
        }
        newName = name;
        return false;
    }
    int GameplayIdToIconOffset(LegacyGameplayId gameplayId) => ((int)gameplayId) * effectIconSize;

    public string GameplayIdToDisplayString(LegacyGameplayId gameplayId) => gameplayId switch
    {
        LegacyGameplayId.ReactorBoost_Oriented => "Boost",
        LegacyGameplayId.ReactorBoost2_Oriented => "Boost2",
        LegacyGameplayId.VehicleTransform_CarDesert => "Desert",
        LegacyGameplayId.VehicleTransform_CarSnow => "Snow",
        LegacyGameplayId.VehicleTransform_CarRally => "Rally",
        LegacyGameplayId.VehicleTransform_Reset => "Stadium",
        LegacyGameplayId.FreeWheeling => "NoEngine",
        LegacyGameplayId.SlowMotion => "SlowMo",
        LegacyGameplayId.TurboRoulette => "TurboR",
        LegacyGameplayId.Boost1LegacyUp => "LegacyBoost_Up",
        LegacyGameplayId.Boost2LegacyUp => "LegacyBoost2_Up",
        _ => gameplayId.ToString(),
    };
    string GameplayIdToMaterialString(LegacyGameplayId gameplayId) => gameplayId switch
    {
        LegacyGameplayId.ReactorBoost_Oriented => "Boost",
        LegacyGameplayId.ReactorBoost2_Oriented => "Boost2",
        LegacyGameplayId.FreeWheeling => "NoEngine",
        LegacyGameplayId.NoGrip => "Cruise",
        LegacyGameplayId.ForceAcceleration => "NoBrake",
        LegacyGameplayId.NoBrakes => "NoBrake",
        LegacyGameplayId.Boost1LegacyUp => "Boost",
        LegacyGameplayId.Boost2LegacyUp => "Boost2",

        _ => gameplayId.ToString(),
    };

    string GameplayIdToSign(LegacyGameplayId gameplayId)
    {
        switch (gameplayId)
        {

            case LegacyGameplayId.VehicleTransform_CarRally:
                return @$"Stadium\Media\Modifier\GateGameplayRally\Screen";
            case LegacyGameplayId.VehicleTransform_CarSnow:
                return @$"Stadium\Media\Modifier\GateGameplaySnow\Sign";
            case LegacyGameplayId.VehicleTransform_CarDesert:
                return @$"Stadium\Media\Modifier\GateGameplayDesert\Screen";
            case LegacyGameplayId.VehicleTransform_Reset:
                return @$"Stadium\Media\Material\GateGameplayScreen";
            case LegacyGameplayId.Bumper:
            case LegacyGameplayId.Bumper2:
            case LegacyGameplayId.NoGrip:
            case LegacyGameplayId.ForceAcceleration:
            case LegacyGameplayId.None:
                return @$"Stadium\Media\Material\SpecialSignOff";
            default:
                return @$"Stadium\Media\Modifier\{GameplayIdToMaterialString(gameplayId)}\Sign";

        }
    }
    string GameplayIdToSignOffLink(LegacyGameplayId gameplayId)
    {
        switch (gameplayId)
        {
            case LegacyGameplayId.Turbo:
            case LegacyGameplayId.Turbo2:
            case LegacyGameplayId.TurboRoulette:
            case LegacyGameplayId.ReactorBoost_Oriented:
            case LegacyGameplayId.ReactorBoost2_Oriented:
                return @$"Stadium\Media\Modifier\{GameplayIdToMaterialString(gameplayId)}\SignOff";
            default:
                return GameplayIdToSign(gameplayId);

        }
    }
    string GameplayIdToSpecialFX(LegacyGameplayId gameplayId)
    {
        switch (gameplayId)
        {
            case LegacyGameplayId.Turbo:
                return "Stadium\\Media\\Material\\SpecialFXTurbo";
            case LegacyGameplayId.VehicleTransform_Reset:
            case LegacyGameplayId.VehicleTransform_CarSnow:
            case LegacyGameplayId.VehicleTransform_CarRally:
            case LegacyGameplayId.VehicleTransform_CarDesert:
            case LegacyGameplayId.Bumper:
            case LegacyGameplayId.Bumper2:
            case LegacyGameplayId.None:
                return "Stadium\\Media\\Material\\SpecialFXGateGameplay";
            default:
                return @$"Stadium\Media\Modifier\{GameplayIdToMaterialString(gameplayId)}\SpecialFX";

        }
    }
    string GameplayIdToDecal(LegacyGameplayId gameplayId)
    {
        switch (gameplayId)
        {
            case LegacyGameplayId.Turbo:
                return "Stadium\\Media\\Material\\DecalSpecialTurbo";
            case LegacyGameplayId.VehicleTransform_CarRally:
                return @$"Stadium\Media\Modifier\GateGameplayRally\Decal";
            case LegacyGameplayId.VehicleTransform_CarSnow:
                return @$"Stadium\Media\Modifier\GateGameplaySnow\Decal";
            case LegacyGameplayId.VehicleTransform_CarDesert:
                return @$"Stadium\Media\Modifier\GateGameplayDesert\Decal";
            case LegacyGameplayId.VehicleTransform_Reset:
                return @$"Stadium\Media\Material\DecalGateGameplay";
            case LegacyGameplayId.Bumper:
            case LegacyGameplayId.Bumper2:
            case LegacyGameplayId.None:
                return @$"Stadium\Media\Material\DecalGateGameplay";
            default:
                return @$"Stadium\Media\Modifier\{GameplayIdToMaterialString(gameplayId)}\Decal";

        }
    }
    string GameplayIdToTriggerFX(LegacyGameplayId gameplayId)
    {
        switch (gameplayId)
        {
            case LegacyGameplayId.Turbo:
                return "Stadium\\Media\\Material\\TriggerFXTurbo";
            case LegacyGameplayId.VehicleTransform_Reset:
            case LegacyGameplayId.VehicleTransform_CarSnow:
            case LegacyGameplayId.VehicleTransform_CarRally:
            case LegacyGameplayId.VehicleTransform_CarDesert:
            case LegacyGameplayId.Bumper:
            case LegacyGameplayId.Bumper2:
            case LegacyGameplayId.None:
                return "Stadium\\Media\\Material\\TriggerFXGateGameplay";
            case LegacyGameplayId.NoGrip:
                return @"Stadium\Media\Material\RaceTriggerFXCheckpoint";
            case LegacyGameplayId.ForceAcceleration:
                return @"Stadium\Media\Material\RaceTriggerFXMultilap";
            default:
                return @$"Stadium\Media\Modifier\{GameplayIdToMaterialString(gameplayId)}\TriggerFX";

        }
    }

    string ConvertLink(string currentLink, LegacyGameplayId gameplayId)
    {
        switch (currentLink)
        {
            case "SpecialSignOff":
            case var s when s.Contains("SignOff"):
                return GameplayIdToSignOffLink(gameplayId);

            case var s when s.Contains("DecalSpecial"):
            case var s2 when s2.Contains("Decal"):
                return GameplayIdToDecal(gameplayId);

            case var s when s.Contains("TriggerFX"):
                return GameplayIdToTriggerFX(gameplayId);

            case var s when s.Contains("SpecialFX"):
                return GameplayIdToSpecialFX(gameplayId);

            case var s when s.Contains("SpecialSign"):
            case var s2 when s2.Contains(@"\Sign"):
                return GameplayIdToSign(gameplayId);

            default:
                return string.Empty;
        }
    }
    string ConvertMaterialName(string currentLink, LegacyGameplayId gameplayId)
    {
        switch (currentLink)
        {
            case var s when s.Contains("Decal"):
                return @$"{GameplayIdToMaterialString(gameplayId)}_Decal";

            case var s when s.Contains("TriggerFX"):
                return @$"{GameplayIdToMaterialString(gameplayId)}_TriggerFX";

            case var s when s.Contains("SpecialFX"):
                return @$"{GameplayIdToMaterialString(gameplayId)}_SpecialFX";
            case var s when s.Contains("Sign"):
                return @$"{GameplayIdToMaterialString(gameplayId)}_Sign";
            case var s when s.Contains("SignOff"):
                return @$"{GameplayIdToMaterialString(gameplayId)}_SignOff";
            default:
                return string.Empty;
        }
    }
}
