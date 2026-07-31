using GBX.NET.Engines.Game;
using GBX.NET.Engines.MwFoundations;
using System.Net.Http.Headers;
using System.Reflection;
using TM_GenericMapping.Common;
using TM_GenericMapping.IO;
using TM_GenericMapping.Messaging;

namespace TM_GenericMapping.MediaTracker.Tools;

public class MediaTrackerDeduplicator
{
    public struct DeduplicationSettings
    {
        public bool InGame;
        public bool Intro;
        public bool EndRace;
        public bool Podium;
        public bool Ambience;

        public bool DeduplicateTracks;
        public bool DeduplicateBlocks;
        public bool IgnoreTrackName;
        public static DeduplicationSettings Default => new()
        {
            InGame = true,
            Intro = true,
            EndRace = true,
            Podium = true,
            Ambience = true,
            DeduplicateTracks = true,
            DeduplicateBlocks = true,
            IgnoreTrackName = true,
        };
    }
    public record class DeduplicationInfo
    {
        public List<string> TrackDeduplicationDetails { get; } = new();
        public List<string> BlockDeduplicationDetails { get; } = new();

        public int TracksDeduplicated => TrackDeduplicationDetails.Count;
        public int BlocksDeduplicated => BlockDeduplicationDetails.Count;
        public int BlocksRemovedViaTrackDeduplication { get; set; } = 0;

    }

    public DeduplicationSettings Settings { get; set; } = DeduplicationSettings.Default;


    Dictionary<int, List<CMwNod>> _nodes = [];
    HashSet<CMwNod> _nodeRefs = [];
    GbxObjectComparerOptions _comparerOptions = GbxObjectComparerOptions.Default;

    public ToolResult<DeduplicationInfo> Deduplicate(CGameCtnChallenge challenge)
    {
        _nodes.Clear();
        _nodeRefs.Clear();
        CreateOptions();
        var info = new DeduplicationInfo();

        if (Settings.Intro && challenge.ClipIntro != null)
            Deduplicate(challenge.ClipIntro, challenge.ClipIntro.Tracks.ToArray(), info);

        if (Settings.InGame && challenge.ClipGroupInGame != null)
            Deduplicate(challenge.ClipGroupInGame, info);

        if (Settings.EndRace && challenge.ClipGroupEndRace != null)
            Deduplicate(challenge.ClipGroupEndRace, info);

        if (Settings.Podium && challenge.ClipPodium != null)
            Deduplicate(challenge.ClipPodium, challenge.ClipPodium.Tracks.ToArray(), info);

        if (Settings.Ambience && challenge.ClipAmbiance != null)
            Deduplicate(challenge.ClipAmbiance, challenge.ClipAmbiance.Tracks.ToArray(), info);

        return ToolResult.Success(info, nameof(MediaTrackerDeduplicator));
    }

    public ToolResult<DeduplicationInfo> Deduplicate(CGameCtnMediaClip clip)
        => Deduplicate(clip, clip.Tracks.ToArray());
    public ToolResult<DeduplicationInfo> Deduplicate(CGameCtnMediaClip clip, ReadOnlySpan<CGameCtnMediaTrack> consideredTracks)
    {
        _nodes.Clear();
        _nodeRefs.Clear();
        CreateOptions();
        var info = new DeduplicationInfo();

        Deduplicate(clip, clip.Tracks.ToArray(), info);

        return ToolResult.Success(info, nameof(MediaTrackerDeduplicator));
    }

    void CreateOptions()
    {
        var dic = new Dictionary<Type, IGbxStructureComparer>();
        dic[typeof(CGameCtnMediaBlockTriangles3D.Chunk03029001)] = new Chunk03029001Comparer();
        if(Settings.IgnoreTrackName)
        {
            dic[typeof(CGameCtnMediaTrack)] = new TrackComparer();
        }

        _comparerOptions = new GbxObjectComparerOptions
        {
            Flags = GbxObjectComparerFlags.PrivateFields,
            CustomComparers = dic
        };
    }

    void Deduplicate(CGameCtnMediaClip clip, ReadOnlySpan<CGameCtnMediaTrack> consideredTracks, DeduplicationInfo info)
    {
        for(int i = 0; i < clip.Tracks.Count; i++)
        {
            var track = clip.Tracks[i];
            if (!consideredTracks.Contains(track))
                continue;
            if(_nodeRefs.Contains(track))
                continue;
            _nodeRefs.Add(track);
            if (Settings.DeduplicateTracks)
            {
                var hash = ComputeHash(track);
                if(_nodes.TryGetValue(hash, out var nodeList))
                {
                    bool deduplicated = false;
                    foreach (CGameCtnMediaTrack otherTrack in nodeList)
                    {
                        if (DeepEqual(track, otherTrack))
                        {
                            clip.Tracks[i] = otherTrack;
                            info.TrackDeduplicationDetails.Add($"Track '{track.Name}' deduplicated from '{otherTrack.Name}', reused {otherTrack.Blocks.Count} blocks");
                            info.BlocksRemovedViaTrackDeduplication += otherTrack.Blocks.Count;
                            deduplicated = true;
                            break;
                        }
                    }
                    if (deduplicated)
                        continue;

                    nodeList.Add(track);
                }
                else
                {
                    _nodes[hash] = [track];
                }
            }
            // deduplicate blocks
            Deduplicate(track, info);
        }

    }

    void Deduplicate(CGameCtnMediaTrack track, DeduplicationInfo info)
    {
        for (int i = 0; i < track.Blocks.Count; ++i)
        {
            var block = track.Blocks[i];
            if (_nodeRefs.Contains(block))
                continue;
            _nodeRefs.Add(block);
            if (Settings.DeduplicateBlocks)
            {
                var hash = ComputeHash(block);
                if (_nodes.TryGetValue(hash, out var nodeList))
                {
                    bool deduplicated = false;
                    foreach (CGameCtnMediaBlock otherBlock in nodeList)
                    {
                        if (DeepEqual(block, otherBlock))
                        {
                            track.Blocks[i] = otherBlock;
                            info.BlockDeduplicationDetails.Add($"Block {block.GetType().Name} deduplicated in Track '{track.Name}'");
                            deduplicated = true;
                            break;
                        }
                    }
                    if (deduplicated)
                        continue;

                    nodeList.Add(block);
                }
                else
                {
                    _nodes[hash] = [block];
                }
            }
        }
    }

    void Deduplicate(CGameCtnMediaClipGroup clipGroup, DeduplicationInfo info)
    {
        foreach(var clip in clipGroup.Clips)
        {
            Deduplicate(clip.Clip, clip.Clip.Tracks.ToArray(), info);
        }
    }

    class TrackComparer : GbxStructureComparerBase<CGameCtnMediaTrack>
    {
        public override bool EqualsField(FieldInfo fInfo, object obj1, object obj2, HashSet<(object, object)> visited, GbxObjectComparerOptions options)
        {
            if(GbxObjectComparer.GetFieldName(fInfo) == "name")
            {
                return true;
            }
            return GbxObjectComparer.EqualsField(fInfo, obj1, obj2, visited, options);
        }
        public override void AddHashField(ref HashCode hash, FieldInfo fInfo, object value, HashSet<object> visited, GbxObjectComparerOptions options)
        {
            if (GbxObjectComparer.GetFieldName(fInfo) == "name")
            {
                return;
            }
            GbxObjectComparer.AddHashField(ref hash, fInfo, value, visited, options);
        }
    }

    class Chunk03029001Comparer : GbxStructureComparerBase<CGameCtnMediaBlockTriangles3D.Chunk03029001>
    {
        public override bool FullReplacement => true;
        protected override void AddHash(ref HashCode hash, CGameCtnMediaBlockTriangles.Chunk03029001 value, HashSet<object> visited, GbxObjectComparerOptions options)
        {
            hash.Add(value.Id);
            hash.Add(value.U01);
            // ignore other U because they differe when copying inside editor and i dont know what they do
        }
        protected override bool Equals(CGameCtnMediaBlockTriangles.Chunk03029001 obj1, CGameCtnMediaBlockTriangles.Chunk03029001 obj2, HashSet<(object, object)> visited, GbxObjectComparerOptions options)
        {
            return obj1.Id == obj2.Id && obj1.U01 == obj2.U01;
        }
    }

    int ComputeHash(CMwNod node)
    {
        return GbxObjectComparer.GetHashCode(node, _comparerOptions);
    }
    bool DeepEqual(CMwNod a, CMwNod b)
    {
        return GbxObjectComparer.Equals(a, b, _comparerOptions);
    }
}
