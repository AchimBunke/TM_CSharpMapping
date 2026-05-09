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

}
