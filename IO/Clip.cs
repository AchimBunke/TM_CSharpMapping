using GBX.NET.Engines.Game;
using GBX.NET;

namespace TM_GenericMapping.IO
{
    public class Clip
    {
        public required string SavePath { get; set; }


        GbxReadSettings ReadSettings { get; set; } = new GbxReadSettings()
        {
            CloseStream = true,
            SafeSkippableChunks = true,
        };

        public bool IsOpen => MediaClip is not null;

        public CGameCtnMediaClip MediaClip { get; private set; } = null!;
        public void Create(CGameCtnMediaClip clip)
        {
            MediaClip = clip;
        }
        public void Open()
        {
            MediaClip = Gbx.Parse<CGameCtnMediaClip>(SavePath, ReadSettings).Node;
        }
        public void Save()
        {
            var directory = Path.GetDirectoryName(SavePath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            MediaClip.Save(SavePath);
        }
    }
}
