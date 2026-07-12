namespace TM_GenericMapping.Messaging;

public static class ErrorCodes
{
    public static class MeshExtractor
    {
        public const string UnsupportedMesh = "MeshExtractor.UnsupportedMesh";
        public const string MissingMesh = "MeshExtractor.MissingMesh";
    }
    public static class MovingItemCreator
    {

        public const string MeshExtractionFailed = "MovingItemCreator.MeshExtractionFailed";
    }
    public static class EmbeddedItemExtractor
    {
        public const string MissingEmbeddedData = "EmbeddedItemExtractor.MissingEmbeddedZipData";
        //public const string ItemParsingFailed = "EmbeddedItemExtractor.ItemParsingFailed";
    }
    public static class BlockToMediaObjectConverter
    {
        public const string MissingTriangleBlocks = "BlockToMediaObjectConverter.MissingTriangleBlocks";
        public const string MissingKeys = "BlockToMediaObjectConverter.MissingKeys";
        public const string UnsupportedBlockType = "BlockToMediaObjectConverter.UnsupportedBlockType";

    }
    public static class TriangleProjector
    {
        public const string InvalidCameraType = "TriangleProjector.InvalidCameraType";
        public const string MissingTriangleBlock = "TriangleProjector.MissingTriangleBlock";
    }
    public static class ItemEffectVariantCreator
    {
        public const string MissingTriggerSpecial = "ItemEffectVariantCreator.MissingTriggerSpecial";
    }
}
