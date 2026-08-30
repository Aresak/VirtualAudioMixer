using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using Vam.Protocol.V1;
using Vam.Ui.Abstractions;

namespace Vam.Ui.Services;

/// <summary>
/// The console's connection to an engine, over gRPC.
/// </summary>
/// <remarks>
/// <para>
/// The same class whether the engine is on this machine or across a hall. G1 makes the engine the
/// process that owns the meeting, and a console that reached into it directly when it happened to be
/// local would leave the remote path untested until the afternoon somebody needed it.
/// </para>
/// <para>
/// <b>It reconnects on its own.</b> An engine restarting, a laptop's network dropping and a cable
/// being kicked all look the same from here, and none of them should require an operator to notice
/// and press something. The status bar says what is happening; the console keeps trying.
/// </para>
/// </remarks>
public sealed class VamSessionClient(
    VamSessionOptions options,
    IPlatformServices platform,
    ILogger<VamSessionClient> logger) : IVamSession, IAsyncDisposable
{
    readonly List<EngineEvent> events = [];
    readonly CancellationTokenSource lifetime = new();

    GrpcChannel? channel;
    Mixer.MixerClient? client;
    Task? pump;
    bool isDisposed;

    /// <inheritdoc />
    public ConnectionState Connection { get; private set; } = ConnectionState.Idle;

    /// <inheritdoc />
    public string StatusMessage { get; private set; } = string.Empty;

    /// <inheritdoc />
    public ConsoleState? Console { get; private set; }

    /// <inheritdoc />
    public string ServerName { get; private set; } = string.Empty;

    /// <inheritdoc />
    public int SampleRate { get; private set; }

    /// <inheritdoc />
    public int BlockFrames { get; private set; }

    /// <inheritdoc />
    public IReadOnlyList<EngineEvent> Events => events;

    /// <inheritdoc />
    public IReadOnlyList<DeviceInfo> Devices { get; private set; } = [];

    /// <inheritdoc />
    public IReadOnlyList<ModifierDescriptorState> Modifiers { get; private set; } = [];

    /// <inheritdoc />
    public event Action? Changed;

    /// <inheritdoc />
    public MeterFrameHandler? MeterFrame { get; set; }

    /// <inheritdoc />
    public ValueTask ConnectAsync(CancellationToken cancellationToken = default)
    {
        pump ??= Task.Run(() => RunAsync(lifetime.Token), CancellationToken.None);

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public async ValueTask<CommandReply> ApplyAsync(Command command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Mixer.MixerClient? current = client;

        if (current is null)
        {
            return new CommandReply { Accepted = false, Reason = "Not connected to an engine." };
        }

        try
        {
            CommandReply reply = await current.ApplyAsync(command, cancellationToken: cancellationToken);

            // The engine is the authority on what the console now looks like. Applying the change
            // locally as well would mean two places deciding, and they would disagree the first time
            // a command was refused.
            await RefreshAsync(cancellationToken);

            return reply;
        }
        catch (RpcException failure)
        {
            logger.LogWarning(failure, "A command was lost on the way to the engine.");

            return new CommandReply { Accepted = false, Reason = failure.Status.Detail };
        }
    }

    /// <inheritdoc />
    public async ValueTask RefreshAsync(CancellationToken cancellationToken = default)
    {
        Mixer.MixerClient? current = client;

        if (current is null)
        {
            return;
        }

        try
        {
            Console = await current.GetConsoleAsync(new Empty(), cancellationToken: cancellationToken);
            Changed?.Invoke();
        }
        catch (RpcException failure)
        {
            logger.LogWarning(failure, "Could not read the console back from the engine.");
        }
    }

    /// <inheritdoc />
    public async ValueTask<DiagnosticsState?> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        Mixer.MixerClient? current = client;

        if (current is null)
        {
            return null;
        }

        try
        {
            return await current.GetDiagnosticsAsync(new Empty(), cancellationToken: cancellationToken);
        }
        catch (RpcException failure)
        {
            logger.LogWarning(failure, "The diagnostics request did not come back.");

            return null;
        }
    }

    /// <summary>Stops trying, and lets go of the channel.</summary>
    /// <remarks>
    /// Safe to call twice, and it will be. A Blazor Server host disposes the request scope and then
    /// the circuit scope, and a second call that threw would surface as an unhandled exception on a
    /// request that had already finished.
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;

        await lifetime.CancelAsync();

        if (pump is not null)
        {
            try
            {
                // VSTHRD003 warns about awaiting a task from a field, because in general you cannot
                // know what context started it. This one was started by this object, on the thread
                // pool, and it is already cancelled. Waiting for it is the difference between
                // letting go of the channel and tearing it out from under a read in flight.
#pragma warning disable VSTHRD003
                await pump;
#pragma warning restore VSTHRD003
            }
            catch (OperationCanceledException)
            {
                // Expected. This is how the pump ends.
            }
        }

        lifetime.Dispose();
        channel?.Dispose();
    }

    async Task RunAsync(CancellationToken cancellationToken)
    {
        TimeSpan delay = options.ReconnectDelay;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await SessionAsync(cancellationToken);
                delay = options.ReconnectDelay;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception failure)
            {
                logger.LogInformation(failure, "The engine connection dropped. Trying again in {Delay}.", delay);
                Set(ConnectionState.Reconnecting, failure.Message);
            }

            try
            {
                await Task.Delay(delay, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            // Backed off, so a console left open against an engine that is not running does not
            // spend an afternoon dialling once a second.
            delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, options.MaximumReconnectDelay.Ticks));
        }
    }

    async Task SessionAsync(CancellationToken cancellationToken)
    {
        Set(
            Connection == ConnectionState.Idle ? ConnectionState.Connecting : ConnectionState.Reconnecting,
            string.Empty);

        channel?.Dispose();
        channel = GrpcChannel.ForAddress(options.Address);
        client = new Mixer.MixerClient(channel);

        HelloReply hello = await client.HelloAsync(
            new HelloRequest { ProtocolVersion = options.ProtocolVersion, ClientName = platform.ClientName },
            cancellationToken: cancellationToken);

        if (!hello.Accepted)
        {
            // A refusal is a sentence, not a code. An operator reading "this console is older than
            // the engine" can do something about it; a status of 3 cannot be acted on by anybody.
            Set(ConnectionState.Refused, hello.Reason);
            throw new InvalidOperationException(hello.Reason);
        }

        ServerName = hello.ServerName;
        SampleRate = hello.SampleRate;
        BlockFrames = hello.BlockFrames;

        Console = await client.GetConsoleAsync(new Empty(), cancellationToken: cancellationToken);

        // Both are fixed for the life of a connection, so they are fetched once here rather than
        // asked for again every time an overlay opens.
        Devices = (await client.ListDevicesAsync(new Empty(), cancellationToken: cancellationToken)).Devices;
        Modifiers = (await client.ListModifiersAsync(new Empty(), cancellationToken: cancellationToken)).Modifiers;

        Set(ConnectionState.Connected, string.Empty);

        await Task.WhenAll(
            MeterPumpAsync(client, cancellationToken),
            EventPumpAsync(client, cancellationToken),
            RefreshPumpAsync(cancellationToken));
    }

    async Task MeterPumpAsync(Mixer.MixerClient current, CancellationToken cancellationToken)
    {
        using AsyncServerStreamingCall<MeterFrame> stream =
            current.StreamMeters(new Empty(), cancellationToken: cancellationToken);

        await foreach (MeterFrame frame in stream.ResponseStream.ReadAllAsync(cancellationToken))
        {
            // Straight through to whatever is drawing, and never to Changed. A meter frame that
            // reached the render tree would re-diff every strip twenty-five times a second.
            MeterFrame?.Invoke(frame.Payload.Span, frame.ChannelCount, frame.BusCount);
        }
    }

    async Task EventPumpAsync(Mixer.MixerClient current, CancellationToken cancellationToken)
    {
        using AsyncServerStreamingCall<EngineEvent> stream =
            current.StreamEvents(new Empty(), cancellationToken: cancellationToken);

        await foreach (EngineEvent engineEvent in stream.ResponseStream.ReadAllAsync(cancellationToken))
        {
            events.Insert(0, engineEvent);

            if (events.Count > options.EventHistory)
            {
                events.RemoveRange(options.EventHistory, events.Count - options.EventHistory);
            }

            // A device arriving or a modifier being switched out changes what the console shows, so
            // this one does redraw. They happen a handful of times in a session.
            await RefreshAsync(cancellationToken);
        }
    }

    async Task RefreshPumpAsync(CancellationToken cancellationToken)
    {
        using PeriodicTimer timer = new(options.RefreshInterval);

        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            await RefreshAsync(cancellationToken);
        }
    }

    void Set(ConnectionState state, string message)
    {
        Connection = state;
        StatusMessage = message;
        Changed?.Invoke();
    }
}
