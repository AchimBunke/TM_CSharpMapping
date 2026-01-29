namespace TM_GenericMapping.Common
{
    public static class WindowsUtils
    {
        public static string MyDocumentsPath => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }
}
