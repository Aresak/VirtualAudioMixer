using System.IO.Compression;
using Vam.Protocol.V1;

namespace Vam.Ui.Extensions;

/// <summary>A past session's name, and its files put back together. #99.</summary>
public static class PastSessionExtensions
{
    /// <summary>The last part of an engine path.</summary>
    /// <param name="directory">The folder, as the engine gave it.</param>
    /// <returns>Its name.</returns>
    /// <remarks>
    /// Both separators, because the path is the engine's and this console may run on a machine whose
    /// <see cref="Path"/> does not know a backslash.
    /// </remarks>
    public static string FolderName(this string directory)
    {
        ArgumentNullException.ThrowIfNull(directory);

        string trimmed = directory.TrimEnd('\\', '/');

        return trimmed[(trimmed.LastIndexOfAny(['\\', '/']) + 1)..];
    }

    /// <summary>Writes an exported session into one zip archive, under a folder of its own.</summary>
    /// <param name="chunks">The files, as the engine sent them.</param>
    /// <param name="folder">The folder the files go under inside the archive.</param>
    /// <param name="destination">Where the archive is written. Left open.</param>
    /// <param name="cancellationToken">Gives up.</param>
    /// <returns>A task that completes when the archive is written.</returns>
    /// <remarks>Stored, not compressed: audio barely shrinks, and the archive is gigabytes.</remarks>
    public static async Task WriteZipAsync(
        this IAsyncEnumerable<PastSessionChunk> chunks,
        string folder,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunks);

        ZipArchive archive = await ZipArchive.CreateAsync(destination, ZipArchiveMode.Create, true, null, cancellationToken);

        await using (archive)
        {
            string? current = null;
            Stream? entry = null;

            try
            {
                await foreach (PastSessionChunk chunk in chunks.WithCancellation(cancellationToken))
                {
                    if (chunk.File != current)
                    {
                        if (entry is not null)
                        {
                            await entry.DisposeAsync();
                        }

                        entry = await archive.CreateEntry($"{folder}/{chunk.File}", CompressionLevel.NoCompression)
                            .OpenAsync(cancellationToken);
                        current = chunk.File;
                    }

                    await entry!.WriteAsync(chunk.Data.Memory, cancellationToken);
                }
            }
            finally
            {
                if (entry is not null)
                {
                    await entry.DisposeAsync();
                }
            }
        }
    }
}
