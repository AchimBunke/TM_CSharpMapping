using GBX.NET;
using GBX.NET.Engines.Game;
using GBX.NET.Engines.GameData;
using System.IO.Compression;
using TM_GenericMapping.Messaging;

namespace TM_GenericMapping.Maps;

public class EmbeddedItemExtractor
{
    public bool HasEmbeddedZipData(CGameCtnChallenge challenge)
    {
        return challenge.EmbeddedZipData is { Length: > 0 };
    }
    public ToolResult<Stream> GetEmbeddedZipStream(CGameCtnChallenge challenge)
    {
        if (!HasEmbeddedZipData(challenge))
            return ToolResult.Fail(
                nameof(EmbeddedItemExtractor), 
                ErrorCodes.EmbeddedItemExtractor.MissingEmbeddedData);
        
        return ToolResult.Success<Stream>(
            new MemoryStream(challenge.EmbeddedZipData, writable: false),
            nameof(EmbeddedItemExtractor));
    }

    public ToolResult<None> ExtractEmbeddedItemsAsZipFile(CGameCtnChallenge challenge, string outputZipPath)
    {
        var streamResult = GetEmbeddedZipStream(challenge);
        if (streamResult.IsFailure)
            return ToolResult.Fail(streamResult);

        Directory.CreateDirectory(Path.GetDirectoryName(outputZipPath)!);

        using var input = streamResult.Value!;
        using var output = File.Create(outputZipPath);

        input.CopyTo(output);

        return ToolResult.Success(nameof(EmbeddedItemExtractor));
    }

    public ToolResult<None> ExtractEmbeddedItemsToDirectory(CGameCtnChallenge challenge, string outputDir)
    {
        var streamResult = GetEmbeddedZipStream(challenge);
        if (streamResult.IsFailure)
            return ToolResult.Fail(streamResult);

        Directory.CreateDirectory(outputDir);

        using var stream = streamResult.Value!;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        foreach (var entry in archive.Entries)
        {
            var fullPath = Path.Combine(outputDir, entry.FullName);

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(fullPath);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

            using var entryStream = entry.Open();
            using var fileStream = File.Create(fullPath);

            entryStream.CopyTo(fileStream);
        }

        return ToolResult.Success(nameof(EmbeddedItemExtractor));
    }

}
