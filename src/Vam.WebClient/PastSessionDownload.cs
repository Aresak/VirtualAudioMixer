using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Net.Http.Headers;
using Vam.Protocol.V1;
using Vam.Ui.Extensions;

namespace Vam.WebClient;

/// <summary>
/// Hands a past session to the browser as one zip archive. #118.
/// </summary>
/// <remarks>
/// <para>
/// Streamed from the engine straight into the response, a chunk at a time. A session is gigabytes,
/// and neither the Blazor circuit nor a script's blob is a place to hold one.
/// </para>
/// <para>
/// It exports only what the engine would: the engine refuses a folder it did not record and the
/// session still being recorded, and that refusal is what the browser is shown.
/// </para>
/// <para>
/// The engine is named in the link, because it is the one the console that made the link is
/// connected to. The host's options are shared by every console it serves, and hold whichever
/// address any of them was pointed at last. Dialling a named engine is nothing a console cannot
/// already do from Settings.
/// </para>
/// </remarks>
public static class PastSessionDownload
{
    /// <summary>The address it answers on, relative to the page.</summary>
    public const string Path = "export";

    /// <summary>Answers one download.</summary>
    /// <param name="engine">The engine's address, as the console dialled it.</param>
    /// <param name="directory">The session's folder, as the engine listed it.</param>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the archive is sent or the refusal is.</returns>
    public static async Task HandleAsync(string engine, string directory, HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!Uri.TryCreate(engine, UriKind.Absolute, out Uri? address) || address.Scheme is not ("http" or "https"))
        {
            await RefuseAsync(context, StatusCodes.Status400BadRequest, "No engine address was given.");
            return;
        }

        using GrpcChannel channel = GrpcChannel.ForAddress(address);
        Mixer.MixerClient client = new(channel);

        DeferredWriteStream? body = null;
        string? refusal;

        try
        {
            refusal = await client.ExportZipAsync(directory, () => body = Begin(context, directory), context.RequestAborted);

            if (body is not null)
            {
                await body.FlushAsync(context.RequestAborted);
            }
        }
        catch (Exception failure) when (failure is RpcException or IOException && context.Response.HasStarted)
        {
            // Half an archive must not look like a whole one when it lands. Aborting the connection
            // is what makes the browser mark the download as failed instead of finished.
            context.Abort();
            return;
        }
        catch (RpcException failure)
        {
            // The engine was not there at all.
            refusal = failure.Status.Detail.Length > 0 ? failure.Status.Detail : failure.Status.StatusCode.ToString();
        }

        if (refusal is not null)
        {
            await RefuseAsync(context, StatusCodes.Status409Conflict, refusal);
        }
    }

    static DeferredWriteStream Begin(HttpContext context, string directory)
    {
        ContentDispositionHeaderValue disposition = new("attachment");

        disposition.SetHttpFileName(directory.FolderName() + ".zip");

        context.Response.ContentType = "application/zip";
        context.Response.Headers.ContentDisposition = disposition.ToString();

        return new DeferredWriteStream(context.Response.Body);
    }

    static async Task RefuseAsync(HttpContext context, int status, string reason)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "text/plain; charset=utf-8";

        await context.Response.WriteAsync(reason, context.RequestAborted);
    }

    /// <summary>
    /// A response body that holds synchronous writes until the next asynchronous one.
    /// </summary>
    /// <remarks>
    /// Writing to a body that cannot seek, a zip archive closes each entry with a small trailing
    /// record written synchronously, and Kestrel refuses synchronous writes outright. Those records
    /// are a few dozen bytes; they wait here and go out ahead of the next asynchronous write, so the
    /// order of the bytes is unchanged and nothing blocks a thread.
    /// </remarks>
    sealed class DeferredWriteStream(Stream inner) : Stream
    {
        readonly MemoryStream pending = new();

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count) => pending.Write(buffer, offset, count);

        public override void Write(ReadOnlySpan<byte> buffer) => pending.Write(buffer);

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await DrainAsync(cancellationToken);
            await inner.WriteAsync(buffer, cancellationToken);
        }

        // Nothing goes out synchronously; what is held leaves with the next asynchronous write or flush.
        public override void Flush()
        {
        }

        public override async Task FlushAsync(CancellationToken cancellationToken)
        {
            await DrainAsync(cancellationToken);
            await inner.FlushAsync(cancellationToken);
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        async Task DrainAsync(CancellationToken cancellationToken)
        {
            if (pending.Length == 0)
            {
                return;
            }

            await inner.WriteAsync(pending.GetBuffer().AsMemory(0, (int)pending.Length), cancellationToken);
            pending.SetLength(0);
        }
    }
}
