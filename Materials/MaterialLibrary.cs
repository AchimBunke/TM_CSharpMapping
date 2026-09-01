using System.IO.Compression;
using TM_GenericMapping.Common;
using Path = System.IO.Path;

namespace TM_GenericMapping.Materials;

public class Material : IDisposable
{
    public string Name { get; set; } = "";
    public DDSTexture? Diffuse { get; set; }
    public DDSTexture? Normal { get; set; }
    public DDSTexture? Roughness { get; set; }
    public DDSTexture? Illum { get; set; }

    public void Dispose()
    {
        Diffuse?.Dispose();
        Normal?.Dispose();
        Roughness?.Dispose();
        Illum?.Dispose();
    }

}


public class MaterialLibrary : IDisposable
{
    private class MaterialEntry
    {
        public required string EntryName;
        public long Offset;
        public long Length;
    }

    private Dictionary<string, Material> loadedMaterials = new();
    private Dictionary<string, MaterialEntry> materialEntries = new();
    private string zipPath = string.Empty;

    public bool LazyLoading { get; init; } = true;

    public void LoadFromZip(string path)
    {
        zipPath = path;
        Logger.Info($"Loading material library: {Path.GetFileName(path)}");

        using var archive = ZipFile.OpenRead(path);

        // Group textures by material name
        var textureGroups = archive.Entries
            .Where(e => e.Name.EndsWith(".dds", StringComparison.OrdinalIgnoreCase))
            .GroupBy(e => GetMaterialName(e.Name));

        foreach (var group in textureGroups)
        {
            foreach (var entry in group)
            {
                string key = GetMaterialTextureKeyFromFile(group.Key, entry.Name);
                materialEntries[key] = new MaterialEntry
                {
                    EntryName = entry.FullName,
                    Offset = 0, // ZipArchive doesn't expose offset directly
                    Length = entry.Length
                };

                Logger.Trace($"Indexed texture: {entry.Name}");
            }
        }

        if (LazyLoading)
        {
            Logger.Info($"Indexed {materialEntries.Count} material textures (lazy loading enabled)");
        }
        else
        {
            Logger.Info("Loading all materials eagerly");
            LoadAllMaterials();
        }
    }

    private void LoadAllMaterials()
    {
        using var archive = ZipFile.OpenRead(zipPath);

        var materialNames = materialEntries.Keys
            .Select(k => k.Split('|')[0])
            .Distinct();

        foreach (var materialName in materialNames)
        {
            LoadMaterial(materialName, archive);
        }

        Logger.Info($"Loaded {loadedMaterials.Count} materials");
    }

    public Material? GetMaterial(string name)
    {
        // Return cached if already loaded
        if (loadedMaterials.TryGetValue(name, out var cached))
        {
            Logger.Trace($"Material cache hit: {name}");
            return cached;
        }

        if (!LazyLoading)
        {
            Logger.Warn($"Material not found: {name}");
            return null;
        }

        // Load on demand
        Logger.Trace($"Loading material on demand: {name}");

        using var archive = ZipFile.OpenRead(zipPath);
        return LoadMaterial(name, archive);
    }

    private Material LoadMaterial(string name, ZipArchive archive)
    {
        var material = new Material { Name = name };

        // Load each texture type
        material.Diffuse = LoadTexture(name, "_D", archive);
        material.Normal = LoadTexture(name, "_N", archive);
        material.Roughness = LoadTexture(name, "_R", archive);
        material.Illum = LoadTexture(name, "_I", archive);

        loadedMaterials[name] = material;

        Logger.Debug($"Loaded material: {name} (D:{(material.Diffuse != null ? "[X]" : "[ ]")} N:{(material.Normal != null ? "[X]" : "[ ]")} R:{(material.Roughness != null ? "[X]" : "[ ]")} I:{(material.Illum != null ? "[X]" : "[ ]")})");

        return material;
    }

    private DDSTexture? LoadTexture(string materialName, string suffix, ZipArchive archive)
    {
        string key = GetMaterialTextureKey(materialName, suffix);

        if (!materialEntries.TryGetValue(key, out var entry))
        {
            Logger.Trace($"Texture not found: {materialName}{suffix}");
            return null;
        }

        var zipEntry = archive.GetEntry(entry.EntryName);
        if (zipEntry == null)
        {
            Logger.Warn($"Zip entry not found: {entry.EntryName}");
            return null;
        }

        Logger.Trace($"Loading texture: {zipEntry.Name}");

        using var stream = zipEntry.Open();
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        ms.Position = 0;

        return new DDSTexture(ms);
    }

    private string GetMaterialName(string filename)
    {
        // "stone_D.dds" -> "stone"
        var name = Path.GetFileNameWithoutExtension(filename);
        int lastUnderscore = name.LastIndexOf('_');
        return lastUnderscore > 0 ? name.Substring(0, lastUnderscore) : name;
    }

    private string GetMaterialTextureKey(string materialName, string suffix)
    {
        return $"{materialName}|{suffix}";
    }

    private string GetMaterialTextureKeyFromFile(string materialName, string filename)
    {
        // Extract suffix from filename: "stone_D.dds" -> "_D"
        var nameWithoutExt = Path.GetFileNameWithoutExtension(filename);
        int lastUnderscore = nameWithoutExt.LastIndexOf('_');
        string suffix = lastUnderscore > 0 ? nameWithoutExt.Substring(lastUnderscore) : "";
        return $"{materialName}|{suffix}";
    }

    public void UnloadMaterial(string name)
    {
        if (loadedMaterials.TryGetValue(name, out var material))
        {
            material.Dispose();
            loadedMaterials.Remove(name);
            Logger.Debug($"Unloaded material: {name}");
        }
    }

    public void UnloadAll()
    {
        foreach (var material in loadedMaterials.Values)
            material.Dispose();

        loadedMaterials.Clear();
        Logger.Info("Unloaded all materials");
    }

    public void Dispose()
    {
        UnloadAll();
    }

    public bool TryGetMaterial(string name, out Material? material)
    {
        material = GetMaterial(name);
        return material != null;
    }


    public static string DefaultTexturesPath = Path.Combine(WindowsUtils.MyDocumentsPath, @"Trackmania\Skins\Stadium\DefaultTextures.zip");
    public static MaterialLibrary CreateDefaultMaterialLibrary()
    {
        var library = new MaterialLibrary();
        library.LoadFromZip(DefaultTexturesPath);
        return library;
    }
}
