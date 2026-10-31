using System.IO.Compression;
using Grpc.Core;
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

    /// <summary>
    /// Exports a session into one zip archive, opening the destination only once the engine has
    /// started sending. #118.
    /// </summary>
    /// <param name="client">The engine.</param>
    /// <param name="directory">The session's folder, as the engine listed it.</param>
    /// <param name="open">Opens the destination. Not called when the engine refuses.</param>
    /// <param name="cancellationToken">Gives up.</param>
    /// <returns>Null when the archive was written, otherwise the engine's reason for refusing.</returns>
    /// <remarks>
    /// A refusal arrives before the first file, and a download that has already sent its headers
    /// cannot turn into a sentence any more. Waiting for the first chunk is what lets a browser be
    /// told why. A failure after that throws, because half an archive has to be abandoned, not saved.
    /// </remarks>
    public static async Task<string?> ExportZipAsync(
        this Mixer.MixerClient client,
        string directory,
        Func<Stream> open,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(open);

        using AsyncServerStreamingCall<PastSessionChunk> call = client.ExportPastSession(
            new PastSessionTarget { Directory = directory },
            cancellationToken: cancellationToken);

        IAsyncEnumerator<PastSessionChunk> chunks = call.ResponseStream.ReadAllAsync(cancellationToken)
            .GetAsyncEnumerator(cancellationToken);

        await using (chunks)
        {
            bool any;

            try
            {
                any = await chunks.MoveNextAsync();
            }
            catch (RpcException refused)
            {
                return refused.Status.Detail.Length > 0 ? refused.Status.Detail : refused.Status.StatusCode.ToString();
            }

            await RemainingAsync(chunks, any).WriteZipAsync(directory.FolderName(), open(), cancellationToken);
        }

        return null;
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

    // The chunk already read, then the rest.
    static async IAsyncEnumerable<PastSessionChunk> RemainingAsync(IAsyncEnumerator<PastSessionChunk> chunks, bool any)
    {
        if (!any)
        {
            yield break;
        }

        do
        {
            yield return chunks.Current;
        }
        while (await chunks.MoveNextAsync());
    }
}
