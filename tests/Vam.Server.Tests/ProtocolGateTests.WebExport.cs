using System.IO.Compression;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Vam.Protocol.V1;
using Vam.TestKit.Harness;
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
        (DefaultHttpContext context, KestrelLikeBody body) = Request();

        await PastSessionDownload.HandleAsync(EngineAddress, session.Directory, context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal("application/zip", context.Response.ContentType);
        Assert.Contains("attachment", context.Response.Headers.ContentDisposition.ToString(), StringComparison.Ordinal);

        string folder = Path.GetFileName(session.Directory);
        Dictionary<string, long> onDisk = Directory
            .GetFiles(session.Directory, "*", SearchOption.AllDirectories)
            .ToDictionary(file => folder + "/" + Path.GetRelativePath(session.Directory, file).Replace('\\', '/'), file => new FileInfo(file).Length);

        using MemoryStream received = new(body.Written);
        using ZipArchive archive = new(received, ZipArchiveMode.Read);
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
        string reason = (await Assert.ThrowsAsync<RpcException>(() => ExportAsync(recording))).Status.Detail;
        (DefaultHttpContext context, KestrelLikeBody body) = Request();

        await PastSessionDownload.HandleAsync(EngineAddress, recording, context);

        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
        Assert.StartsWith("text/plain", context.Response.ContentType, StringComparison.Ordinal);
        Assert.Empty(context.Response.Headers.ContentDisposition.ToString());
        Assert.NotEmpty(reason);
        Assert.Equal(reason, System.Text.Encoding.UTF8.GetString(body.Written));

        await client.ApplyAsync(new Command { SetRecording = new SetRecording { Recording = false } }, cancellationToken: Token);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public async Task ABrowserDownloadsTheZipFromARealWebHost()
    {
        PastSession session = await RecordOneSessionAsync();
        int port = FreePort();

        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();

        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls($"http://localhost:{port}");

        // Mapped as Program.cs maps it, on Kestrel with its default refusal of synchronous IO.
        await using WebApplication web = builder.Build();

        web.MapGet("/" + PastSessionDownload.Path, PastSessionDownload.HandleAsync);
        await web.StartAsync(Token);

        using HttpClient browser = new();
        string address = $"http://localhost:{port}/"
            + new Vam.WebClient.WebPlatformServices().ExportAddress(EngineAddress, session.Directory);

        using HttpResponseMessage response = await browser.GetAsync(address, Token);

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Token));

        await using Stream downloaded = await response.Content.ReadAsStreamAsync(Token);
        using MemoryStream received = new();
        await downloaded.CopyToAsync(received, Token);
        received.Position = 0;

        using ZipArchive archive = new(received, ZipArchiveMode.Read);

        Assert.Equal(
            Directory.GetFiles(session.Directory, "*", SearchOption.AllDirectories).Length,
            archive.Entries.Count);

        await web.StopAsync(Token);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public async Task AnExportWithNoEngineAddressIsRefused()
    {
        (DefaultHttpContext context, KestrelLikeBody _) = Request();

        await PastSessionDownload.HandleAsync("not an address", "anything", context);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    }

    string EngineAddress => $"http://{channel!.Target}";

    static (DefaultHttpContext Context, KestrelLikeBody Body) Request()
    {
        KestrelLikeBody body = new();
        DefaultHttpContext context = new() { RequestAborted = Token };

        context.Response.Body = body;

        return (context, body);
    }

    /// <summary>
    /// A response body that behaves like Kestrel's: it cannot seek, and a synchronous write throws.
    /// </summary>
    /// <remarks>
    /// A <see cref="MemoryStream"/> does both, and a test written against one passed while every real
    /// download failed on the first entry it closed.
    /// </remarks>
    sealed class KestrelLikeBody : Stream
    {
        readonly MemoryStream written = new();

        public byte[] Written => written.ToArray();

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new InvalidOperationException("Synchronous operations are disallowed.");

        public override void Write(ReadOnlySpan<byte> buffer) =>
            throw new InvalidOperationException("Synchronous operations are disallowed.");

        public override void Flush() =>
            throw new InvalidOperationException("Synchronous operations are disallowed.");

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            written.WriteAsync(buffer, offset, count, cancellationToken);

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            written.WriteAsync(buffer, cancellationToken);

        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
