namespace TM_GenericMapping.Messaging;

public static class ErrorCodes
{
    public static class MeshExtractor
    {
        public const string UnsupportedMesh = "MeshExtractor.UnsupportedMesh";
        public const string MissingMesh = "MeshExtractor.MissingMesh";
        public const string MissingTriggerShape = "MeshExtractor.MissingTriggerShape";
    }
    public static class MeshBuilder
    {
        public const string MissingTrigger = "MeshBuilder.MissingTrigger";
        public const string MissingDynaShape = "MeshBuilder.MissingDynaShape";
        public const string MissingStaticShape = "MeshBuilder.MissingStaticShape";
    }
    public static class MovingItemCreator
    {

        public const string MeshExtractionFailed = "MovingItemCreator.MeshExtractionFailed";
        public const string MeshBuildingFailed = "MovingItemCreator.MeshBuildingFailed";
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
