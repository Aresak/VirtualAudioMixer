using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Vam.Engine.Devices;
using Vam.Engine.Devices.Abstractions;
using Vam.Protocol;
using Vam.Protocol.V1;
using Vam.Server.Engine;
using Vam.Server.Mediator;
using Vam.Server.Services;
using Vam.TestKit.Devices;
using Vam.TestKit.Harness;
using Xunit;

namespace Vam.Server.Tests;

/// <summary>
/// EPIC-08's gate: a client with no user interface at all drives the whole mixer over the protocol.
/// </summary>
/// <remarks>
/// <para>
/// The point of the gate is that the protocol is complete before any Razor component exists. If a
/// console can be built, a channel routed, a fader moved and meters read from here, then the UI is a
/// view over a working engine rather than the place the engine's behaviour actually lives.
/// </para>
/// <para>
/// Over a real Kestrel on a real socket, not against the service class directly, because half the
/// things that go wrong with a protocol are transport-shaped.
/// </para>
/// </remarks>
public class ProtocolGateTests : IAsyncLifetime
{
    const string Speakerphone = "{11111111-2222-3333-4444-555555555555}";

    readonly NullAudioBackend devices = new();
    readonly string workspace = Path.Combine(Path.GetTempPath(), "vam-server-" + Guid.NewGuid().ToString("n"));

    WebApplication? host;
    GrpcChannel? channel;
    Mixer.MixerClient? client;
    VamEngine? engine;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        // Two channels, like every real microphone on the machine this was found on. A speakerphone,
        // a headset and most USB microphones present a stereo endpoint whatever is behind it.
        devices.AddDevice(DeviceDirection.Capture, new NullDeviceOptions("Mayor 180 degrees", ChannelCount: 2, Signal: NullSignal.Tone));

        // One piece of hardware with a microphone and a speaker, the way a speakerphone is. Sending
        // its own microphone to its own speaker is the loop mix-minus exists to refuse.
        devices.AddDevice(DeviceDirection.Capture, new NullDeviceOptions("Lectern", Signal: NullSignal.Tone, ContainerId: Speakerphone));
        devices.AddDevice(DeviceDirection.Render, new NullDeviceOptions("Monitor", ChannelCount: 2));
        devices.AddDevice(DeviceDirection.Render, new NullDeviceOptions("Lectern", ChannelCount: 2, ContainerId: Speakerphone));

        int port = FreePort();

        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();

        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options =>
            options.ListenLocalhost(port, listener => listener.Protocols = HttpProtocols.Http2));

        builder.Services.AddGrpc();

        // The same registration the real host uses. A test host that wired the handlers up its own
        // way would be testing an arrangement nobody ships.
        builder.Services.AddVamMediator();
        builder.Services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);

        engine = new VamEngine(
            new EngineOptions
            {
                ConsolePath = Path.Combine(workspace, "console.json"),
                RecordingDirectory = Path.Combine(workspace, "recordings"),
                RecordAutomatically = false
            },
            NullLoggerFactory.Instance,
            devices);

        builder.Services.AddSingleton(engine);

        host = builder.Build();
        host.MapGrpcService<MixerService>();

        engine.Start();

        await host.StartAsync(TestContext.Current.CancellationToken);

        channel = GrpcChannel.ForAddress($"http://localhost:{port}");
        client = new Mixer.MixerClient(channel);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public async Task MetersKeepMovingAfterTheGraphIsRebuilt()
    {
        Mixer.MixerClient live = client!;

        Assert.True(await SawALevelAsync(live), "The meters were not moving to begin with.");

        await live.ApplyAsync(
            new Command { AddBus = new AddBus { Name = "Councillor headphones", Role = "Monitor", ChannelCount = 2 } },
            cancellationToken: TestContext.Current.CancellationToken);

        // Adding a bus recompiles, and a recompile builds a new meter node with new cells. The
        // publisher held the old ones, so every meter in every console froze at the first structural
        // change and stayed frozen until the engine was restarted - while the audio itself carried on
        // perfectly, which is the worst way for this to fail.
        Assert.True(await SawALevelAsync(live), "The meters stopped when the graph was rebuilt.");
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AStereoMicrophoneIsReadAsStereo()
    {
        // The ring's width has to be the stream's width. Narrower and Write takes the first half of
        // every buffer and reads interleaved channels as consecutive frames: half the audio thrown
        // away, the rest at half rate an octave down with left and right alternating. It is audible
        // immediately and it is not obvious what it is - it sounds like a bad sample rate.
        //
        // Folding stereo down to a mono strip is the graph's job. The fold needs both channels first.
        IReadOnlyList<AudioDeviceInfo> present = devices.Enumerate(DeviceDirection.Capture);

        Assert.Contains(present, device => device.ChannelCount == 2);

        foreach (DeviceInputChannel channel in engine!.Channels.Channels)
        {
            AudioDeviceInfo device = present.Single(candidate => candidate.Id == channel.DeviceId);

            Assert.Equal(device.ChannelCount, channel.ChannelCount);
        }
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public async Task ASpeakerphoneIsNotSentItsOwnMicrophone()
    {
        Mixer.MixerClient live = client!;

        ConsoleState before = await live.GetConsoleAsync(new Empty(), cancellationToken: TestContext.Current.CancellationToken);
        DeviceList inventory = await live.ListDevicesAsync(new Empty(), cancellationToken: TestContext.Current.CancellationToken);

        int lectern = IndexOfChannel(before, "Lectern");
        string speaker = inventory.Devices.Single(device => device.Name == "Lectern" && device.Direction == "Render").Id;

        await live.ApplyAsync(
            new Command
            {
                AddBus = new AddBus { Name = "Lectern monitor", Role = "Monitor", ChannelCount = 2, OutputDeviceId = speaker }
            },
            cancellationToken: TestContext.Current.CancellationToken);

        ConsoleState after = await live.GetConsoleAsync(new Empty(), cancellationToken: TestContext.Current.CancellationToken);
        BusState monitor = after.Buses[^1];

        // The engine has to work out for itself that these two endpoints are one object. Nothing
        // else can: they have different identities and their names agree only by luck. Without it
        // the pair list stays empty and mix-minus, which the whole monitoring design leans on, never
        // once fires.
        Assert.Contains(lectern, monitor.ExcludedChannels);

        // And the other microphone still gets through, or this would be proving that monitors are
        // simply broken rather than that one send is refused.
        Assert.DoesNotContain(IndexOfChannel(after, "Mayor 180 degrees"), monitor.ExcludedChannels);
    }

    static int IndexOfChannel(ConsoleState console, string name)
    {
        for (int index = 0; index < console.Channels.Count; index++)
        {
            if (console.Channels[index].Name == name)
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>Reads meter frames until one shows a channel above silence, or gives up.</summary>
    static async Task<bool> SawALevelAsync(Mixer.MixerClient live)
    {
        using CancellationTokenSource giveUp = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        giveUp.CancelAfter(TimeSpan.FromSeconds(5));

        try
        {
            using AsyncServerStreamingCall<MeterFrame> stream = live.StreamMeters(new Empty(), cancellationToken: giveUp.Token);

            while (await stream.ResponseStream.MoveNext(giveUp.Token))
            {
                MeterFrame frame = stream.ResponseStream.Current;

                for (int channel = 0; channel < frame.ChannelCount; channel++)
                {
                    if (MeterFrameCodec.ReadChannel(frame.Payload.Span, channel).PeakDb > MeterFrameCodec.SilenceDb)
                    {
                        return true;
                    }
                }
            }
        }
        catch (RpcException)
        {
            // The deadline. Answered below by returning false.
        }
        catch (OperationCanceledException)
        {
        }

        return false;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        channel?.Dispose();

        if (host is not null)
        {
            await host.StopAsync(TestContext.Current.CancellationToken);
            await host.DisposeAsync();
        }

        engine?.Dispose();
        devices.Dispose();

        if (Directory.Exists(workspace))
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public async Task AnOldClientIsRefusedWithASentenceRatherThanMisbehaving()
    {
        HelloReply refused = await client!.HelloAsync(new HelloRequest { ProtocolVersion = 0, ClientName = "old tablet" }, cancellationToken: Token);

        Assert.False(refused.Accepted);

        // The failure a version negotiation exists to prevent is the subtle one three commands
        // later, so the refusal has to be legible.
        Assert.Contains("protocol", refused.Reason, StringComparison.OrdinalIgnoreCase);

        HelloReply accepted = await client.HelloAsync(new HelloRequest
        {
            ProtocolVersion = MixerService.ProtocolVersion,
            ClientName = "test"
        }, cancellationToken: Token);

        Assert.True(accepted.Accepted);
        Assert.Equal(48000, accepted.SampleRate);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public async Task TheEngineBuildsAConsoleFromWhateverIsPluggedIn()
    {
        ConsoleState console = await client!.GetConsoleAsync(new Empty(), cancellationToken: Token);

        // Nothing was saved, so the engine made something that works rather than waiting to be told.
        Assert.Equal(2, console.Channels.Count);
        Assert.Single(console.Buses);
        Assert.Contains(console.Channels, channel => channel.Name == "Mayor 180 degrees");
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public async Task AFaderMovesAndTheConsoleSaysSo()
    {
        CommandReply reply = await client!.ApplyAsync(new Command
        {
            SetFader = new SetFader { ChannelIndex = 0, Decibels = -7.5 }
        }, cancellationToken: Token);

        Assert.True(reply.Accepted);

        ConsoleState console = await client.GetConsoleAsync(new Empty(), cancellationToken: Token);

        Assert.Equal(-7.5, console.Channels[0].FaderDb, 3);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public async Task ABusIsCreatedAndAChannelRoutedIntoIt()
    {
        CommandReply added = await client!.ApplyAsync(new Command
        {
            AddBus = new AddBus { Name = "Councillor headphones", Role = "Monitor", ChannelCount = 2 }
        }, cancellationToken: Token);

        Assert.True(added.Accepted);

        ConsoleState console = await client.GetConsoleAsync(new Empty(), cancellationToken: Token);

        Assert.Equal(2, console.Buses.Count);
        Assert.Equal("Monitor", console.Buses[1].Role);

        CommandReply routed = await client.ApplyAsync(new Command
        {
            SetSend = new SetSend { ChannelIndex = 0, BusIndex = 1, On = true, Decibels = -3 }
        }, cancellationToken: Token);

        Assert.True(routed.Accepted);

        console = await client.GetConsoleAsync(new Empty(), cancellationToken: Token);

        SendState send = console.Sends.Single(s => s.ChannelIndex == 0 && s.BusIndex == 1);

        Assert.Equal("On", send.State);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public async Task MetersArriveOnTheirOwnStreamAndDecodeToLevels()
    {
        using AsyncServerStreamingCall<MeterFrame> meters = client!.StreamMeters(new Empty(), cancellationToken: Token);

        Assert.True(await meters.ResponseStream.MoveNext(Token), "No meter frame arrived.");

        MeterFrame frame = meters.ResponseStream.Current;

        Assert.Equal(2, frame.ChannelCount);
        Assert.Equal(MeterFrameCodec.SizeOf(frame.ChannelCount, frame.BusCount), frame.Payload.Length);

        // Decoded with the codec a third-party console would use, from the Apache-licensed protocol
        // assembly rather than from anything in the engine.
        ChannelMeter meter = MeterFrameCodec.ReadChannel(frame.Payload.Span, 0);

        Assert.InRange(meter.PeakDb, -140.0, 12.0);
        Assert.InRange(meter.RmsDb, -140.0, 12.0);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public async Task ARefusedCommandSaysWhy()
    {
        CommandReply reply = await client!.ApplyAsync(new Command
        {
            SetFlag = new SetFlag { ChannelIndex = 0, Flag = "NotAFlag", Enabled = true }
        }, cancellationToken: Token);

        Assert.False(reply.Accepted);
        Assert.Contains("NotAFlag", reply.Reason, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public async Task TheConsoleSurvivesBeingSavedAndLoaded()
    {
        await client!.ApplyAsync(new Command { SetFader = new SetFader { ChannelIndex = 1, Decibels = -12 } }, cancellationToken: Token);
        await client.ApplyAsync(new Command { SetTrim = new SetTrim { ChannelIndex = 1, Decibels = 4 } }, cancellationToken: Token);

        engine!.SaveConsole();

        using VamEngine reopened = new(
            new EngineOptions
            {
                ConsolePath = Path.Combine(workspace, "console.json"),
                RecordingDirectory = Path.Combine(workspace, "recordings"),
                RecordAutomatically = false
            },
            NullLoggerFactory.Instance,
            devices);

        reopened.Start();

        // H1 and H3. An operator who spent a meeting setting a console up gets it back, and gets it
        // back keyed by device identity rather than by position.
        Assert.Equal(-12, reopened.Graph!.Config.Channels[1].FaderDb, 3);
        Assert.Equal(4, reopened.Graph.Config.Channels[1].TrimDb, 3);
        Assert.Equal("Lectern", reopened.Graph.Config.Channels[1].Name);
    }

    /// <summary>The token the runner cancels, so a hung call fails the test rather than the run.</summary>
    static CancellationToken Token => TestContext.Current.CancellationToken;

    static int FreePort()
    {
        using System.Net.Sockets.TcpListener listener = new(System.Net.IPAddress.Loopback, 0);

        listener.Start();

        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;

        listener.Stop();

        return port;
    }
}
