using GBX.NET.Engines.Game;
using GBX.NET;

namespace TM_GenericMapping.IO;

public class Map
{
    public string SaveName { get; set; }

    public string SavePath { get; set; }
    public string MapPath { get; init; }

    public Map(string mapPath)
    {
        MapPath = mapPath;
        SavePath = mapPath;
    }
    public Map(string mapPath, string savePath) : this(mapPath)
    {
        SavePath = savePath;
    }

    GbxReadSettings ReadSettings { get; set; } = new GbxReadSettings()
    {
        CloseStream = true,
        SafeSkippableChunks = true,
    };

    public bool IsOpen => Challenge is not null;

    public CGameCtnChallenge Challenge { get; private set; }
    public void Open()
    {
        Challenge = Gbx.Parse<CGameCtnChallenge>(MapPath, ReadSettings).Node;
    }
    public async Task OpenAsync()
    {
        Challenge = (await Gbx.ParseAsync<CGameCtnChallenge>(MapPath, ReadSettings)).Node;
    }
    public void Save()
    {
        if (!string.IsNullOrWhiteSpace(SaveName))
            Challenge.MapName = SaveName;
        var directory = Path.GetDirectoryName(SavePath);
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
        Challenge.Save(SavePath);
    }

}

