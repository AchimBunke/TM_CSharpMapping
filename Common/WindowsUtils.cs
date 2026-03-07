namespace TM_GenericMapping.Common
{
    public static class WindowsUtils
    {
        public static string MyDocumentsPath => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        public static string TrackmaniaPath { get; set; } = Path.Combine(MyDocumentsPath, @"Trackmania");
        public static string ItemDirectoryPath { get; set; } = Path.Combine(TrackmaniaPath, @"Items");
        public static string ClipsDirectoryPath { get; set; } = Path.Combine(TrackmaniaPath, @"Replays\Clips");

    }
}
