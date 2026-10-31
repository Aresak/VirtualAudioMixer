using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Vam.Engine.Devices.Abstractions;
using Vam.Server.Engine;
using Vam.Server.Mediator;
using Vam.Server.Services;
using Vam.TestKit.Devices;
using Vam.TestKit.Harness;
using Vam.Ui.Abstractions;
using Vam.Ui.Services;
using Xunit;

namespace Vam.Server.Tests;

/// <summary>
/// #93. A console connected to an engine that stops says so, and finds the engine again when it is back.
/// </summary>
public sealed class EngineStopTests : IAsyncDisposable
{
    static readonly TimeSpan Patience = TimeSpan.FromSeconds(15);

    readonly NullAudioBackend devices = new();
    readonly string workspace = Path.Combine(Path.GetTempPath(), "vam-stop-" + Guid.NewGuid().ToString("n"));

    WebApplication? host;
    VamEngine? engine;

    static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public async Task AStoppedEngineIsNoticedAndARestartedOneIsReconnectedTo()
    {
        devices.AddDevice(DeviceDirection.Render, new NullDeviceOptions("Monitor", ChannelCount: 2));

        int port = FreePort();

        await StartEngineAsync(port);

        await using VamSessionClient session = new(
            new VamSessionOptions
            {
                Address = $"http://localhost:{port}",
                ReconnectDelay = TimeSpan.FromMilliseconds(100),
                MaximumReconnectDelay = TimeSpan.FromMilliseconds(500)
            },
            new HeadlessPlatform(),
            NullLogger<VamSessionClient>.Instance);

        await session.ConnectAsync(Token);

        Assert.True(await WaitForAsync(() => session.Connection == ConnectionState.Connected), "Never connected.");

        await StopEngineAsync();

        Stopwatch noticed = Stopwatch.StartNew();

        Assert.True(
            await WaitForAsync(() => session.Connection != ConnectionState.Connected),
            "The console still says Connected to an engine that has stopped.");
        Assert.True(noticed.Elapsed < TimeSpan.FromSeconds(5), $"Took {noticed.Elapsed} to notice.");

        await StartEngineAsync(port);

        Assert.True(
            await WaitForAsync(() => session.Connection == ConnectionState.Connected),
            "The console did not reconnect to the restarted engine.");
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopEngineAsync();

        devices.Dispose();

        if (Directory.Exists(workspace))
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    async Task StartEngineAsync(int port)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();

        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options =>
            options.ListenLocalhost(port, listener => listener.Protocols = HttpProtocols.Http2));

        builder.Services.AddGrpc();
        builder.Services.AddVamMediator();
        builder.Services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);

        engine = new VamEngine(
            new EngineOptions
            {
                ConsolePath = Path.Combine(workspace, "console.json"),
                PresetPath = Path.Combine(workspace, "presets.json"),
                RecordingDirectory = Path.Combine(workspace, "recordings"),
                RecordAutomatically = false,
                ExpectedSessionDuration = TimeSpan.FromMinutes(1)
            },
            NullLoggerFactory.Instance,
            devices);

        builder.Services.AddSingleton(engine);

        host = builder.Build();
        host.MapGrpcService<MixerService>();

        engine.Start();

        await host.StartAsync(Token);
    }

    async Task StopEngineAsync()
    {
        if (host is not null)
        {
            await host.StopAsync(Token);
            await host.DisposeAsync();
            host = null;
        }

        engine?.Dispose();
        engine = null;
    }

    static async Task<bool> WaitForAsync(Func<bool> condition)
    {
        Stopwatch waited = Stopwatch.StartNew();

        while (!condition() && waited.Elapsed < Patience)
        {
            await Task.Delay(50, Token);
        }

        return condition();
    }

    static int FreePort()
    {
        using System.Net.Sockets.TcpListener listener = new(System.Net.IPAddress.Loopback, 0);

        listener.Start();

        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;

        listener.Stop();

        return port;
    }

    internal sealed class HeadlessPlatform : IPlatformServices
    {
        public string ClientName => "engine-stop-test";

        public bool CanPickFolders => false;

        public bool CanOpenFolders => false;

        public bool CanStartEngine => false;

        public string? RememberedEngine { get; set; }

        public bool StartsEngineAutomatically { get; set; }

        public ValueTask<string?> PickFolderAsync(string title, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<string?> OpenFolderAsync(string path, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public bool CanSaveFiles => false;

        public ValueTask<Stream?> CreateFileAsync(string suggestedName, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<string?> StartEngineAsync(string address, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
