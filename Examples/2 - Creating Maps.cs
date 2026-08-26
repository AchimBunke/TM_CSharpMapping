using GBX.NET.Engines.Game;
using TM_GenericMapping.Common;
using TM_GenericMapping.IO;

namespace TM_GenericMapping.Examples;

public class Example_CreatingMaps
{
    public void Program()
    {
        // ===================== Setup Gbx =====================
        GbxExtensions.Setup();

        // ===================== Setup Templates (see 1) =====================
        BlockTemplates blockTemplates = MediaTrackerUtils.CreateBlockTemplates();

        // ===================== Create / Open Map =====================
        string mapLoadPath = Path.Combine(WindowsUtils.TrackmaniaPath, @"Maps\My Maps\MyMap.Map.Gbx");
        string mapSavePath = Path.Combine(WindowsUtils.TrackmaniaPath, @"Maps\My Maps\Modified MyMap.Map.Gbx");
        string mapSaveName = "Modified MyMap";

        // Use a different SavePath to not overwrite the old map in case the resulting map file is corrupted.
        // Also use a different SaveName to avoid mixup ingame
        Map map = new Map(mapLoadPath, mapSavePath)
        {
            SaveName = mapSaveName,
        };
        // Load the map
        map.Open();

        // Now you can modify map as normal in GBX NET
        var challenge = map.Challenge;

        // Example: you can add clips directly to the map
        // !! Assumes map already has 1 clip
        CGameCtnMediaClip mediaClip = challenge.ClipGroupInGame!.Clips[0].Clip;

        // ===================== Create Scene =====================
        // ...
        // ===================== Render Scene into Clip =====================
        // ...

        // ===================== Save Map =====================
        map.Save();

    }
    class MySceneBuilder : SceneBuilder
    {
        protected override void Build()
        {
            Console.WriteLine("Adding Cube to Scene");
            var cube = new Cube(size: 20);
            scene.Add(cube);
        }
    }
}
