namespace TM_GenericMapping.Common;

public static class MapFileUtils
{
    public static string MapNameToFileName(string fileName)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            fileName = fileName.Replace(c, '_');
        }
        return fileName;
    }

    public static string SanitizeMapPath(string filePath)
    {
        string directory = Path.GetDirectoryName(filePath); // Extract directory
        string fileName = Path.GetFileName(filePath);       // Extract file name

        fileName = MapNameToFileName(filePath);

        // Combine sanitized file name with the original directory
        return Path.Combine(directory, fileName);
    }
}
