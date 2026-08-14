namespace TM_GenericMapping.Items;

public static class VariantUtils
{
    public enum VariantType
    {
        Cactus, 
        CherryTree, 
        CypressDirt,
        Cypress, 
        SpringTree, 
        Fir, 
        FirSnow, 
        Flag,
        Lamp,
        LightTube, 
        Pillar, 
        RotorFrame,
        PalmTree,
        Screen, 
        Ramp,
        RoadSign, 
        Rig, 
        Show,
        Fogger,
        SupportTruss, 
        SupportTrussConnector, 
        TrackBarrier, 
        TunnelSupportArch, 
        TunnelSupportArchCenter, 
        TunnelSupportHalfArch, 
        TunnelSupportPillar, 
        TunnelSupportPillarLarge
    }
    public enum VariantSize
    {
        Medium,
        VerySmall, 
        Tall, 
        Big,
        Small, 
        _16m, 
        _32m, 
        _4m, 
        _8m, 
        _2m, 
        Curve1, 
        Curve2, 
        Curve3, 
        _6x1, 
        _2x1, 
        _1x1
    }
    public enum VariantPlacement
    {
        Synthetic, 
        Wild, 
        Top_Bottom, 
        Back, 
        Front, 
        ShowFull, 
        BackStage,
        Show, 
        ShowRace
    }
    public enum VariantMatModifier
    {
        Dirt,
        Grass,
        Ice
    }
    public enum VariantVariant
    {
        Str, 
        Curve,
        DiagCube,
        DiagSquare, 
        OnTruss, 
        OnTruss8m, 
        OnTruss16m, 
        In,
        Out,
        Light,
        Speaker,
        A,
        B
    }

    public static KeyValuePair<string, string> GetTypeTag(VariantType type)
        => KeyValuePair.Create("Type", type.ToString());
    public static KeyValuePair<string, string> GetSizeTag(VariantSize size)
        => KeyValuePair.Create("Size", size.ToString().Replace("_", ""));
    public static KeyValuePair<string, string> GetPlacementTag(VariantPlacement placement)
        => KeyValuePair.Create("Placement", placement.ToString().Replace("_", "/"));
    public static KeyValuePair<string, string> GetMatModifierTag(VariantMatModifier matModifier)
        => KeyValuePair.Create("MatModifier", matModifier.ToString());
    public static KeyValuePair<string, string> GetVariantTag(VariantVariant variant)
       => KeyValuePair.Create("Variant", variant.ToString());
}
