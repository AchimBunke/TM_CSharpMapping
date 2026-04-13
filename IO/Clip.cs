using GBX.NET.Engines.Game;
using GBX.NET;
using TM_GenericMapping.Common;

namespace TM_GenericMapping.IO
{
    public class Clip
    {
        public required string SavePath { get; set; }


        public GbxReadSettings ReadSettings { get; set; } = new GbxReadSettings()
        {
            CloseStream = true,
            SafeSkippableChunks = true,
        };
        public GbxWriteSettings WriteSettings { get; set; } = new GbxWriteSettings()
        {
            CloseStream = true,
        };

        public bool IsOpen => MediaClip is not null;

        public CGameCtnMediaClip MediaClip { get; private set; } = null!;
        public void Create(CGameCtnMediaClip clip)
        {
            MediaClip = clip;
        }
        public void Create(BlockTemplates templates)
            => Create(MediaTrackerUtils.DeepCopyClip(templates.Clip));
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

        public void Save(Stream outputStream)
        {
            MediaClip.Save(outputStream, WriteSettings);
        }
        public void Open(Stream clipStream)
        {
            MediaClip = Gbx.Parse<CGameCtnMediaClip>(clipStream, ReadSettings).Node;
        }

        public async Task OpenAsync(Stream clipStream)
        {
            MediaClip = (await Gbx.ParseAsync<CGameCtnMediaClip>(clipStream, ReadSettings)).Node;
        }
    }
}
