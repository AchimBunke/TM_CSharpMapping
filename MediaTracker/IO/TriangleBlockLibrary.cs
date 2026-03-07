using System.Xml.Linq;
using TM_GenericMapping.Common;
using static GBX.NET.Engines.Plug.CPlugPrefab;

namespace TM_GenericMapping.Common.IO;

/// <summary>
/// Allows easy access to sets of serialized TriangleObject data and querying
/// </summary>
public class TriangleBlockLibrary
{
    public class LoadedTriangleBlockEntry
    {
        public Dictionary<int, TriangleObjectData> LODs = [];
    }
    public class TriangleBlockEntry
    {
        public Dictionary<int, string> LODs = [];
    }

    private readonly Dictionary<string, Dictionary<string, TriangleBlockEntry>> triangleBlockEntries = [];
    Dictionary<(string theme, string block), LoadedTriangleBlockEntry> loadedBlocks = [];
    public IReadOnlyDictionary<string, Dictionary<string, TriangleBlockEntry>> AvailableBlockEntries => triangleBlockEntries;

    public const string DefaultTheme = "Default";

    private string dirPath;
    public bool LazyLoading { get; init; } = true;

    public void LoadFromDirectory(string rootPath)
    {
        foreach (var themeDir in Directory.GetDirectories(rootPath))
        {
            var themeName = Path.GetFileName(themeDir);
            triangleBlockEntries[themeName] = LoadTheme(themeDir);
        }
    }
    Dictionary<string, TriangleBlockEntry> LoadTheme(string path)
    {
        var result = new Dictionary<string, TriangleBlockEntry>();
        Logger.Info($"Loading triangleBlock library: {path}");

        var dirInfo = new DirectoryInfo(path);
        var blockNames = dirInfo.GetFiles()
            .Where(f => f.Name.EndsWith(".Item.mesh", StringComparison.OrdinalIgnoreCase))
            .GroupBy(f => GetBlockName(f.Name));
        foreach(var group in blockNames)
        {
            var blockEntry = new TriangleBlockEntry();
            string key = string.Empty;
            foreach (var file in group)
            {
                if(key == string.Empty)
                    key = GetBlockName(file.Name);
                var nameWithLOD = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(file.Name));
                int lodLevel = 0;
                if (nameWithLOD.Contains("_LOD"))
                {
                    var splits = nameWithLOD.Split("_LOD");
                    lodLevel = int.Parse(splits[1]);
                }
                blockEntry.LODs[lodLevel] = file.FullName;
            }
            result[group.Key] = blockEntry;
            Logger.Trace($"Indexed block entry: {key}");
        }

        if (LazyLoading)
        {
            Logger.Info($"Indexed {triangleBlockEntries.Count} block entries (lazy loading enabled)");
        }
        else
        {
            Logger.Info("Loading all triangle blocks eagerly");
            LoadAllBlocks();
        }
        return result;
    }

    void LoadAllBlocks()
    {
        foreach (var themePair in triangleBlockEntries)
        {
            string theme = themePair.Key;
            var themeDict = themePair.Value;

            foreach (var blockPair in themeDict)
            {
                string blockName = blockPair.Key;

                LoadBlockEntry(blockName, theme);
            }
        }

        Logger.Info($"Loaded {loadedBlocks.Count} meshes");
    }

    LoadedTriangleBlockEntry? LoadBlockEntry(string blockName, string theme)
    {
        if (!triangleBlockEntries.TryGetValue(theme, out var themeDict) ||
            !themeDict.TryGetValue(blockName, out var blockEntry))
        {
            Logger.Trace($"TriangleBlockEntry not found: {blockName} (theme: {theme})");
            return null;
        }

        var loadedBlockEntry = new LoadedTriangleBlockEntry();

        foreach (var kv in blockEntry.LODs)
        {
            string filePath = kv.Value;
            int lodLevel = kv.Key;

            var obj = TriangleObjectSerializer.Load<TriangleObjectData>(filePath);
            loadedBlockEntry.LODs[lodLevel] = obj;
        }

        loadedBlocks[(theme, blockName)] = loadedBlockEntry;

        Logger.Debug($"Loaded triangleBlock: {blockName} (theme: {theme}), LODs: {loadedBlockEntry.LODs.Count}");
        return loadedBlockEntry;
    }


    public LoadedTriangleBlockEntry? GetBlockEntry(
        string blockName, string theme = DefaultTheme)
    {
        // Return cached if already loaded
        if (loadedBlocks.TryGetValue((theme, blockName), out var cached))
        {
            Logger.Trace($"TriangleBlockEntry cache hit: {blockName} (theme: {theme})");
            return cached;
        }

        if (!LazyLoading)
        {
            Logger.Warn($"TriangleBlockEntry not found: {blockName} (theme: {theme})");
            return null;
        }

        // Load on demand
        Logger.Trace($"Loading triangleBlockEntry on demand: {blockName} (theme: {theme})");

        var entry = LoadBlockEntry(blockName, theme);

        // Fallback to default theme if needed
        if (entry == null && theme != DefaultTheme)
        {
            Logger.Trace($"Falling back to default theme for: {blockName}");
            entry = LoadBlockEntry(blockName, DefaultTheme);
        }

        return entry;
    }

    private string GetBlockName(string filename)
    {
        var name = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(filename));
        int lastLOD = name.LastIndexOf("_LOD");
        return lastLOD > 0 ? name.Substring(0, lastLOD) : name;
    }

    public bool TryGetTriangleDataForBlock(
        string blockName, 
        out TriangleObjectData triangleObjectData,
        string theme = DefaultTheme,
        int desiredLOD = 0,
        bool findNearestLOD = true)
    {
        var entry = GetBlockEntry(blockName, theme);

        if (entry == null)
        {
            triangleObjectData = null!;
            return false;
        }

        if (entry.LODs.TryGetValue(desiredLOD, out triangleObjectData))
            return true;

        if (findNearestLOD && entry.LODs.Count > 0)
        {
            var closestKey = entry.LODs.Keys
                .OrderBy(k => Math.Abs(k - desiredLOD))
                .First();

            triangleObjectData = entry.LODs[closestKey];
            return true;
        }

        triangleObjectData = null!;
        return false;
    }
    public bool TryGetTriangleForBlock(
        string blockName,
        out TriangleObject triangleObject,
        string theme = DefaultTheme,
        int desiredLOD = 0,
        bool findNearestLOD = true)
    {
        triangleObject = null!;

        bool success = TryGetTriangleDataForBlock(
            blockName,
            out var data,
            theme,
            desiredLOD,
            findNearestLOD);

        if (success)
            triangleObject = new TriangleObject(data);

        return success;
    }

    public void UnloadBlock(string blockName, string theme = DefaultTheme)
    {
        if (loadedBlocks.Remove((theme, blockName)))
        {
            Logger.Debug($"Unloaded block: {blockName} (theme: {theme})");
        }
    }
    public void UnloadAll()
    {
        var keys = loadedBlocks.Keys.ToArray();

        foreach (var key in keys)
            loadedBlocks.Remove(key);

        Logger.Info("Unloaded all blocks");
    }


    public static string DefaultObjectLibraryPath { get; set; } = Path.Combine(WindowsUtils.TrackmaniaPath, "MediaTrackerItems");
    public static TriangleBlockLibrary LoadDefaultTriangleBlockLibrary()
    {
        var library = new TriangleBlockLibrary() { LazyLoading = true };
        library.LoadFromDirectory(DefaultObjectLibraryPath);
        return library;
    }

}
