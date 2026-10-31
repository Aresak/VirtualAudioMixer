using System.Diagnostics;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Vam.Protocol.V1;
using Vam.TestKit.Harness;
using Vam.Ui.Abstractions;
using Vam.Ui.Services;
using Xunit;

namespace Vam.Server.Tests;

/// <summary>A console pointed at an engine older than the process picker. #95.</summary>
public sealed class OlderEngineTests : IAsyncDisposable
{
    static readonly TimeSpan Patience = TimeSpan.FromSeconds(15);

    readonly OlderEngine older = new();

    WebApplication? host;

    static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public async Task AnEngineWithoutTheNameIsNotAskedForPrograms()
    {
        int port = await StartAsync();

        await using VamSessionClient session = new(
            new VamSessionOptions { Address = $"http://localhost:{port}" },
            new EngineStopTests.HeadlessPlatform(),
            NullLogger<VamSessionClient>.Instance);

        await session.ConnectAsync(Token);

        Stopwatch waited = Stopwatch.StartNew();

        while (session.Connection != ConnectionState.Connected && waited.Elapsed < Patience)
        {
            await Task.Delay(50, Token);
        }

        Assert.Equal(ConnectionState.Connected, session.Connection);

        IReadOnlyList<AudioProcess> programs = await session.GetAudioProcessesAsync(Token);

        Assert.Empty(programs);
        Assert.Equal(0, older.AudioProcessRequests);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (host is not null)
        {
            await host.StopAsync(Token);
            await host.DisposeAsync();
        }
    }

    async Task<int> StartAsync()
    {
        using System.Net.Sockets.TcpListener listener = new(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();

        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options =>
            options.ListenLocalhost(port, endpoint => endpoint.Protocols = HttpProtocols.Http2));
        builder.Services.AddGrpc();
        builder.Services.AddSingleton(older);

        host = builder.Build();
        host.MapGrpcService<OlderEngine>();

        await host.StartAsync(Token);

        return port;
    }

    /// <summary>Speaks protocol 1 and lists no feature names, like an engine from before them.</summary>
    sealed class OlderEngine : Mixer.MixerBase
    {
        int audioProcessRequests;

        public int AudioProcessRequests => Volatile.Read(ref audioProcessRequests);

        public override Task<HelloReply> Hello(HelloRequest request, ServerCallContext context) =>
            Task.FromResult(new HelloReply { Accepted = true, ProtocolVersion = 1, ServerName = "VAM" });

        public override Task<ConsoleState> GetConsole(Empty request, ServerCallContext context) =>
            Task.FromResult(new ConsoleState());

        public override Task<DeviceList> ListDevices(Empty request, ServerCallContext context) =>
            Task.FromResult(new DeviceList());

        public override Task<ModifierCatalogue> ListModifiers(Empty request, ServerCallContext context) =>
            Task.FromResult(new ModifierCatalogue());

        public override Task<ChainPresetList> ListChainPresets(Empty request, ServerCallContext context) =>
            Task.FromResult(new ChainPresetList());

        public override Task<AudioProcessList> ListAudioProcesses(Empty request, ServerCallContext context)
        {
            Interlocked.Increment(ref audioProcessRequests);

            return Task.FromResult(new AudioProcessList());
        }

        public override Task StreamMeters(Empty request, IServerStreamWriter<MeterFrame> responseStream, ServerCallContext context) =>
            WaitAsync(context);

        public override Task StreamEvents(Empty request, IServerStreamWriter<EngineEvent> responseStream, ServerCallContext context) =>
            WaitAsync(context);

        static async Task WaitAsync(ServerCallContext context)
        {
            try
            {
                await Task.Delay(Timeout.Infinite, context.CancellationToken);
            }
            catch (OperationCanceledException)
            {
                // The console went away, which is how a stream to it ends.
            }
        }
    }
}
