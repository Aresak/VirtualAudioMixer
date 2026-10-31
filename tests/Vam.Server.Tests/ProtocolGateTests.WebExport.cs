using System.IO.Compression;
using Microsoft.AspNetCore.Http;
using Vam.Protocol.V1;
using Vam.TestKit.Harness;
using Vam.Ui.Services;
using Vam.WebClient;
using Xunit;

namespace Vam.Server.Tests;

/// <summary>The web console's download of a past session. #118.</summary>
public partial class ProtocolGateTests
{
    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public async Task TheWebHostStreamsASessionAsAZipOfItsFiles()
    {
        PastSession session = await RecordOneSessionAsync();
        DefaultHttpContext context = Request();
        using MemoryStream body = new();
        context.Response.Body = body;

        await PastSessionDownload.HandleAsync(session.Directory, context, WebHostOptions());

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal("application/zip", context.Response.ContentType);
        Assert.Contains("attachment", context.Response.Headers.ContentDisposition.ToString(), StringComparison.Ordinal);

        string folder = Path.GetFileName(session.Directory);
        Dictionary<string, long> onDisk = Directory
            .GetFiles(session.Directory, "*", SearchOption.AllDirectories)
            .ToDictionary(file => folder + "/" + Path.GetRelativePath(session.Directory, file).Replace('\\', '/'), file => new FileInfo(file).Length);

        body.Position = 0;

        using ZipArchive archive = new(body, ZipArchiveMode.Read);
        Dictionary<string, long> zipped = archive.Entries.ToDictionary(entry => entry.FullName, entry => entry.Length);

        Assert.NotEmpty(onDisk);
        Assert.Equal(onDisk.OrderBy(pair => pair.Key), zipped.OrderBy(pair => pair.Key));
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public async Task AnExportTheEngineRefusesShowsItsReasonAndNoArchive()
    {
        CommandReply started = await client!.ApplyAsync(
            new Command { SetRecording = new SetRecording { Recording = true } },
            cancellationToken: Token);

        Assert.True(started.Accepted, started.Reason);

        string recording = (await client.GetConsoleAsync(new Empty(), cancellationToken: Token)).Recording.Directory;
        DefaultHttpContext context = Request();
        using MemoryStream body = new();
        context.Response.Body = body;

        await PastSessionDownload.HandleAsync(recording, context, WebHostOptions());

        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
        Assert.StartsWith("text/plain", context.Response.ContentType, StringComparison.Ordinal);
        Assert.Empty(context.Response.Headers.ContentDisposition.ToString());
        Assert.NotEmpty(System.Text.Encoding.UTF8.GetString(body.ToArray()));

        await client.ApplyAsync(new Command { SetRecording = new SetRecording { Recording = false } }, cancellationToken: Token);
    }

    static DefaultHttpContext Request() => new() { RequestAborted = Token };

    VamSessionOptions WebHostOptions() => new() { Address = $"http://{channel!.Target}" };
}
