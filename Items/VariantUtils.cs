namespace TM_GenericMapping.Items;

public static class VariantUtils
{
    public enum VariantType
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
        Ramp,
        Rig,
        RoadSign,
        RotorFrame,
        Screen,
        Show,
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

    public enum VariantSize
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

    public enum VariantPlacement
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

    public enum VariantMatModifier
    {
        Dirt,
        Grass,
        Ice
    }

    public enum VariantVariant
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
