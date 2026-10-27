using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using Vam.Protocol;
using Vam.Protocol.V1;
using Vam.Ui.Abstractions;
using Vam.Ui.Extensions;

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

    // Guards the swap below, and nothing else. Cancelling a disposed source throws, so the attempt
    // being replaced and the attempt being cancelled must not be able to happen at once.
    readonly Lock attemptGate = new();

    CancellationTokenSource? attempt;
    GrpcChannel? channel;

    // Set by a re-point, read when the attempt it cancelled falls over. A flag rather than an
    // exception type, because gRPC reports a cancelled call as an RpcException carrying
    // StatusCode.Cancelled - not as an OperationCanceledException - and catching that by type would
    // put "call canceled by the client" in the status bar as though something had gone wrong.
    volatile bool repointRequested;
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
    public IReadOnlySet<string> Features { get; private set; } = new HashSet<string>();

    /// <inheritdoc />
    public IReadOnlyList<EngineEvent> Events => events;

    /// <inheritdoc />
    public IReadOnlyList<DeviceInfo> Devices { get; private set; } = [];

    /// <inheritdoc />
    public IReadOnlyList<ModifierDescriptorState> Modifiers { get; private set; } = [];

    /// <inheritdoc />
    public IReadOnlyList<ChainPresetSummary> Presets { get; private set; } = [];

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
    public async ValueTask ReconnectAsync(string address, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(address);

        options.Address = address;
        repointRequested = true;

        // Taken rather than borrowed: whichever of this and the attempt's own ending gets it first
        // owns disposing it, so a cancel can never land on a source the loop has already let go of.
        CancellationTokenSource? current = Take();

        if (current is not null)
        {
            // Cancelled rather than left to time out. The loop notices, sees that this was not the
            // console shutting down, and dials again -- reading the new address on the way past,
            // because that is where SessionAsync gets it from every time.
            await current.CancelAsync().ConfigureAwait(false);

            current.Dispose();
        }

        await ConnectAsync(cancellationToken).ConfigureAwait(false);
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
    public async ValueTask RefreshPresetsAsync(CancellationToken cancellationToken = default)
    {
        Mixer.MixerClient? current = client;

        if (current is null)
        {
            return;
        }

        try
        {
            Presets = (await current.ListChainPresetsAsync(new Empty(), cancellationToken: cancellationToken)).Presets;
            Changed?.Invoke();
        }
        catch (RpcException failure)
        {
            logger.LogWarning(failure, "Could not read the preset library back.");
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
            DiagnosticsState state = await current.GetDiagnosticsAsync(new Empty(), cancellationToken: cancellationToken);

            // Absent on the wire is null here, and the view reads these without asking.
            state.Clock ??= new ClockDiagnostics();
            state.Callbacks ??= new CallbackDiagnostics();
            state.Allocations ??= new AllocationDiagnostics();

            return state;
        }
        catch (RpcException failure)
        {
            logger.LogWarning(failure, "The diagnostics request did not come back.");

            return null;
        }
    }

    /// <inheritdoc />
    public async ValueTask<PastSessionList?> GetPastSessionsAsync(CancellationToken cancellationToken = default)
    {
        Mixer.MixerClient? current = client;

        if (current is null)
        {
            return null;
        }

        try
        {
            return await current.ListPastSessionsAsync(new Empty(), cancellationToken: cancellationToken);
        }
        catch (RpcException failure)
        {
            logger.LogWarning(failure, "The past sessions did not come back.");

            return null;
        }
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<AudioProcess>> GetAudioProcessesAsync(CancellationToken cancellationToken = default)
    {
        if (client is not { } current || !Features.Contains(EngineFeatures.AudioProcesses))
        {
            return [];
        }

        try
        {
            return (await current.ListAudioProcessesAsync(new Empty(), cancellationToken: cancellationToken)).Processes;
        }
        catch (RpcException failure)
        {
            logger.LogWarning(failure, "The programs with audio did not come back.");

            return [];
        }
    }

    /// <inheritdoc />
    public ValueTask<CommandReply?> DeletePastSessionAsync(string directory, CancellationToken cancellationToken = default) =>
        AskAsync(
            current => current.DeletePastSessionAsync(new PastSessionTarget { Directory = directory }, cancellationToken: cancellationToken),
            "delete");

    /// <inheritdoc />
    public ValueTask<CommandReply?> RenamePastSessionAsync(string directory, string name, CancellationToken cancellationToken = default) =>
        AskAsync(
            current => current.RenamePastSessionAsync(new PastSessionRename { Directory = directory, Name = name }, cancellationToken: cancellationToken),
            "rename");

    /// <inheritdoc />
    public async ValueTask<string?> ExportPastSessionAsync(string directory, Stream destination, CancellationToken cancellationToken = default)
    {
        if (client is not { } current)
        {
            return "recording.exportNoEngine";
        }

        try
        {
            using AsyncServerStreamingCall<PastSessionChunk> call = current.ExportPastSession(
                new PastSessionTarget { Directory = directory },
                cancellationToken: cancellationToken);

            await call.ResponseStream.ReadAllAsync(cancellationToken)
                .WriteZipAsync(directory.FolderName(), destination, cancellationToken);

            return null;
        }
        catch (RpcException failure)
        {
            logger.LogWarning(failure, "The export of {Directory} did not finish.", directory);

            return failure.Status.Detail.Length > 0 ? failure.Status.Detail : "recording.exportFailed";
        }
        catch (IOException failure)
        {
            logger.LogWarning(failure, "The export of {Directory} could not be written.", directory);

            return failure.Message;
        }
    }

    async ValueTask<CommandReply?> AskAsync(Func<Mixer.MixerClient, AsyncUnaryCall<CommandReply>> call, string what)
    {
        if (client is not { } current)
        {
            return null;
        }

        try
        {
            return await call(current);
        }
        catch (RpcException failure)
        {
            logger.LogWarning(failure, "The {What} of a past session did not come back.", what);

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
        TimeSpan delay = TimeSpan.Zero;

        while (!cancellationToken.IsCancellationRequested)
        {
            repointRequested = false;

            CancellationToken attemptToken = BeginAttempt(cancellationToken);

            try
            {
                // The wait comes before the attempt rather than after it, so a re-point cancels the
                // waiting as well as the dialling. A console left against an engine that is not
                // running backs off to fifteen seconds, and somebody who has just typed the right
                // address should not be made to sit out the rest of a wait the wrong one earned.
                await Task.Delay(delay, attemptToken);
                await SessionAsync(attemptToken);
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception failure) when (repointRequested)
            {
                logger.LogDebug(failure, "The console was pointed at {Address} mid-call.", options.Address);

                delay = TimeSpan.Zero;
            }
            catch (Exception failure)
            {
                // Read before Set, which is about to overwrite it. A connection that worked and then
                // dropped starts the backoff from the bottom again: it is evidence the engine is
                // reachable, which a dial that never got an answer is not.
                delay = Connection == ConnectionState.Connected ? options.ReconnectDelay : Grow(delay);

                logger.LogInformation(failure, "The engine connection dropped. Trying again in {Delay}.", delay);
                Set(ConnectionState.Reconnecting, failure.Message);
            }
            finally
            {
                EndAttempt();
            }
        }
    }

    /// <summary>
    /// The next backoff, so a console left open against nothing does not dial once a second all
    /// afternoon.
    /// </summary>
    TimeSpan Grow(TimeSpan delay) =>
        delay == TimeSpan.Zero
            ? options.ReconnectDelay
            : TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, options.MaximumReconnectDelay.Ticks));

    CancellationToken BeginAttempt(CancellationToken lifetimeToken)
    {
        CancellationTokenSource current = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken);

        lock (attemptGate)
        {
            attempt = current;
        }

        return current.Token;
    }

    // Disposed outside the lock, and only by whoever took it: a linked source holds a registration
    // on the lifetime token, and one per attempt would accumulate across an afternoon of retrying.
    void EndAttempt() => Take()?.Dispose();

    CancellationTokenSource? Take()
    {
        lock (attemptGate)
        {
            CancellationTokenSource? current = attempt;

            attempt = null;

            return current;
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
        Features = hello.Features.ToHashSet(StringComparer.Ordinal);

        Console = await client.GetConsoleAsync(new Empty(), cancellationToken: cancellationToken);

        // Both are fixed for the life of a connection, so they are fetched once here rather than
        // asked for again every time an overlay opens.
        Devices = (await client.ListDevicesAsync(new Empty(), cancellationToken: cancellationToken)).Devices;
        Modifiers = (await client.ListModifiersAsync(new Empty(), cancellationToken: cancellationToken)).Modifiers;

        // Presets change while a session runs, unlike the other two, so this one is also re-read
        // whenever the console saves or deletes one.
        Presets = (await client.ListChainPresetsAsync(new Empty(), cancellationToken: cancellationToken)).Presets;

        Set(ConnectionState.Connected, string.Empty);

        await PumpAsync(client, cancellationToken);
    }

    // Any one pump ending ends the session. Waiting for all three left a console saying Connected to
    // a stopped engine, because the refresh pump survives a failed read and never ends on its own.
    async Task PumpAsync(Mixer.MixerClient current, CancellationToken cancellationToken)
    {
        using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        Task[] pumps =
        [
            MeterPumpAsync(current, stop.Token),
            EventPumpAsync(current, stop.Token),
            RefreshPumpAsync(stop.Token)
        ];

        Task ended = await Task.WhenAny(pumps);

        await stop.CancelAsync();
        await Task.WhenAll(pumps).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        await ended;
        cancellationToken.ThrowIfCancellationRequested();

        // A stream the engine closed cleanly is still the engine going away.
        throw new InvalidOperationException("The engine closed the connection.");
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
