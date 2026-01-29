using System.Xml.Linq;
using TM_GenericMapping.Common;

namespace TM_GenericMapping.MediaTracker.IO;

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

    Dictionary<string, LoadedTriangleBlockEntry> loadedBlocks = [];
    Dictionary<string, TriangleBlockEntry> triangleBlockEntries = [];
    public IReadOnlyDictionary<string, TriangleBlockEntry> AvailableBlockEntries => triangleBlockEntries;
    private string dirPath;
    public bool LazyLoading { get; init; } = true;

    public void LoadFromDirectory(string path)
    {
        dirPath = path;
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
            triangleBlockEntries[key] = blockEntry;
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
    }

    void LoadAllBlocks()
    {
        foreach(var blockName in triangleBlockEntries.Keys)
        {
            LoadBlockEntry(blockName);
        }
        Logger.Info($"Loaded {loadedBlocks.Count} meshes");
    }

    LoadedTriangleBlockEntry LoadBlockEntry(string blockName)
    {
        if(!triangleBlockEntries.TryGetValue(blockName, out var blockEntry))
        {
            Logger.Trace($"TriangleBlockEntry not found: {blockName}");
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
        loadedBlocks[blockName] = loadedBlockEntry;
        Logger.Debug($"Loaded triangleBlock: {blockName}, LODs: {loadedBlockEntry.LODs.Count}");

        return loadedBlockEntry;
    }

    public LoadedTriangleBlockEntry? GetBlockEntry(string blockName)
    {
        // Return cached if already loaded
        if (loadedBlocks.TryGetValue(blockName, out var cached))
        {
            Logger.Trace($"TriangleBlockEntry cache hit: {blockName}");
            return cached;
        }

        if (!LazyLoading)
        {
            Logger.Warn($"TriangleBlockEntry not found: {blockName}");
            return null;
        }
        // Load on demand
        Logger.Trace($"Loading triangleBlockEntry on demand: {blockName}");
        return LoadBlockEntry(blockName);
    }

    private string GetBlockName(string filename)
    {
        var name = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(filename));
        int lastLOD = name.LastIndexOf("_LOD");
        return lastLOD > 0 ? name.Substring(0, lastLOD) : name;
    }

    public bool TryGetTriangleDataForBlock(string blockName, out TriangleObjectData triangleObjectData, int desiredLOD = 0, bool findNearestLOD = true)
    {
        var entry = GetBlockEntry(blockName);
        if (entry == null)
        {
            triangleObjectData = null!;
            return false;
        }
        if (entry.LODs.TryGetValue(desiredLOD, out triangleObjectData))
        {
            return true;
        }
        if (findNearestLOD)
        {
            var closestKey = entry.LODs.Keys
                .OrderBy(k => Math.Abs(k - desiredLOD))
                .ThenBy(Math.Abs)
                .First();
            triangleObjectData = entry.LODs[closestKey];
            return true;
        }
        triangleObjectData = null!;
        return false;
    }
    public bool TryGetTriangleForBlock(string blockName, out TriangleObject triangleObject, int desiredLOD = 0, bool findNearestLOD = true)
    {
        triangleObject = null!;
        bool success = TryGetTriangleDataForBlock(blockName, out var data, desiredLOD, findNearestLOD);
        if (success)
            triangleObject = new TriangleObject(data);
        return success;
    }

    public void UnloadBlock(string blockName)
    {
        if (loadedBlocks.TryGetValue(blockName, out var loadedBlockEntry))
        {
            loadedBlocks.Remove(blockName);
            Logger.Debug($"Unloaded material: {loadedBlocks}");
        }
    }
    public void UnloadAll()
    {
        var keys = loadedBlocks.Keys.ToArray();
        foreach (var key in keys)
            UnloadBlock(key);
        Logger.Info("Unloaded all blocks");
    }


    public const string DefaultObjectLibraryPath = @"H:\Dev\Assets\Trackmania\MediaTrackerItems";
    public static TriangleBlockLibrary LoadDefaultTriangleBlockLibrary()
    {
        var library = new TriangleBlockLibrary() { LazyLoading = true };
        library.LoadFromDirectory(DefaultObjectLibraryPath);
        return library;
    }

}
