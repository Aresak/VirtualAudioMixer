using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Net.Http.Headers;
using Vam.Protocol.V1;
using Vam.Ui.Extensions;
using Vam.Ui.Services;

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
/// </remarks>
public static class PastSessionDownload
{
    /// <summary>The address it answers on, relative to the page.</summary>
    public const string Path = "export";

    /// <summary>Answers one download.</summary>
    /// <param name="directory">The session's folder, as the engine listed it.</param>
    /// <param name="context">The request.</param>
    /// <param name="options">Where the engine is.</param>
    /// <returns>A task that completes when the archive is sent or the refusal is.</returns>
    public static async Task HandleAsync(string directory, HttpContext context, VamSessionOptions options)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);

        using GrpcChannel channel = GrpcChannel.ForAddress(options.Address);
        Mixer.MixerClient client = new(channel);

        string? refusal;

        try
        {
            refusal = await client.ExportZipAsync(directory, () => Begin(context, directory), context.RequestAborted);
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
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            context.Response.ContentType = "text/plain; charset=utf-8";

            await context.Response.WriteAsync(refusal, context.RequestAborted);
        }
    }

    static Stream Begin(HttpContext context, string directory)
    {
        ContentDispositionHeaderValue disposition = new("attachment");

        disposition.SetHttpFileName(directory.FolderName() + ".zip");

        context.Response.ContentType = "application/zip";
        context.Response.Headers.ContentDisposition = disposition.ToString();

        return context.Response.Body;
    }
}
