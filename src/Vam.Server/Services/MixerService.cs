using Microsoft.Extensions.Hosting;
using Grpc.Core;
using Shiny.Mediator;
using Vam.Server.Mediator;
using Vam.Engine.Devices;
using Vam.Engine.Devices.Abstractions;
using Vam.Engine.Recording;
using Vam.Modifiers.Abstractions;
using Vam.Engine.Graph;
using Vam.Engine.Graph.Nodes;
using Vam.Engine.Modifiers;
using Vam.Engine.Metering;
using Vam.Protocol;
using Vam.Protocol.V1;
using Vam.Server.Engine;
using EngineSendState = Vam.Engine.Graph.SendState;
using WireAutomixState = Vam.Protocol.V1.AutomixState;
using WireSendState = Vam.Protocol.V1.SendState;

namespace Vam.Server.Services;

/// <summary>
/// The control surface, over gRPC. G2 and G4.
/// </summary>
/// <remarks>
/// <para>
/// <b>The local console uses this too.</b> There is no private path for the client running on the
/// same machine, and that is deliberate: a shortcut for the local case would leave the remote case
/// as the one nobody exercised until somebody needed it during a meeting.
/// </para>
/// <para>
/// Meters have their own stream. A tablet on a slow link throttles its meters and keeps its faders,
/// which would be impossible if both shared one.
/// </para>
/// </remarks>
public sealed class MixerService(
    VamEngine engine,
    IMediator mediator,
    IHostApplicationLifetime lifetime,
    ILogger<MixerService> logger) : Mixer.MixerBase
{
    /// <summary>What this build speaks.</summary>
    public const int ProtocolVersion = 1;

    /// <inheritdoc />
    public override Task<HelloReply> Hello(HelloRequest request, ServerCallContext context)
    {
        bool accepted = request.ProtocolVersion == ProtocolVersion;

        if (!accepted)
        {
            // Refused with a sentence rather than left to misbehave subtly three commands later.
            logger.LogWarning(
                "{Client} speaks protocol {Theirs}; this server speaks {Ours}.",
                request.ClientName,
                request.ProtocolVersion,
                ProtocolVersion);
        }

        return Task.FromResult(new HelloReply
        {
            Accepted = accepted,
            ProtocolVersion = ProtocolVersion,
            ServerName = "VAM",
            Reason = accepted
                ? string.Empty
                : $"This server speaks protocol {ProtocolVersion} and the client speaks {request.ProtocolVersion}.",
            SampleRate = 48000,
            BlockFrames = 120
        });
    }

    /// <inheritdoc />
    public override Task<ConsoleState> GetConsole(Empty request, ServerCallContext context) =>
        Task.FromResult(BuildConsole());

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// A translation and a send, and nothing else. This method used to be a switch over thirty
    /// commands with the work inline; the work is in handlers now and the transport's whole job is
    /// to turn one wire message into the contract it stands for.
    /// </para>
    /// <para>
    /// That is what makes the second transport nearly free — a WebSocket adapter writes a
    /// translator and stops, because everything below this line has never heard of gRPC.
    /// </para>
    /// </remarks>
    public override async Task<CommandReply> Apply(Command request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (CommandTranslator.Translate(request) is not IRequest<CommandReply> contract)
        {
            return Refuse("The command carried nothing this engine knows.");
        }

        try
        {
            (IMediatorContext _, CommandReply reply) = await mediator
                .Request(contract, context?.CancellationToken ?? CancellationToken.None)
                .ConfigureAwait(false);

            return reply;
        }
        catch (Exception error)
        {
            // A command that threw must not take the transport with it. The session continues, the
            // console is told in words, and the stack trace goes where a stack trace belongs.
            logger.LogError(error, "{Operation} failed.", contract.GetType().Name);

            return Refuse(error.Message);
        }
    }

    /// <inheritdoc />
    public override Task<DeviceList> ListDevices(Empty request, ServerCallContext context)
    {
        DeviceList list = new();

        if (engine.Backend is not { } backend)
        {
            return Task.FromResult(list);
        }

        HashSet<string> inUse = engine.Graph is { } graph
            ? [.. graph.Config.Channels.Select(channel => channel.DeviceId.Value)]
            : [];

        foreach (DeviceDirection direction in (ReadOnlySpan<DeviceDirection>)[DeviceDirection.Capture, DeviceDirection.Render])
        {
            foreach (AudioDeviceInfo device in backend.Enumerate(direction))
            {
                list.Devices.Add(new DeviceInfo
                {
                    Id = device.Id.Value,
                    Name = device.FriendlyName,
                    Direction = direction.ToString(),
                    ChannelCount = device.ChannelCount,
                    SampleRate = device.NominalSampleRate,
                    IsPresent = true,

                    // Informs rather than forbids. Two strips on one endpoint is legal and
                    // occasionally exactly what somebody wants.
                    IsInUse = inUse.Contains(device.Id.Value)
                });
            }
        }

        return Task.FromResult(list);
    }

    /// <inheritdoc />
    public override Task<ModifierCatalogue> ListModifiers(Empty request, ServerCallContext context)
    {
        ModifierCatalogue catalogue = new();

        foreach (string id in engine.Modifiers.Ids)
        {
            if (engine.Modifiers.Create(id) is not { } modifier)
            {
                continue;
            }

            ModifierDescriptorState state = new()
            {
                Id = modifier.Descriptor.Id,
                Name = modifier.Descriptor.Name,
                Description = string.Empty
            };

            foreach (ParameterDescriptor parameter in modifier.Parameters)
            {
                state.Parameters.Add(new ParameterState
                {
                    Id = parameter.Id,
                    Name = parameter.Name,
                    Unit = parameter.Unit,
                    Value = parameter.Default,
                    Minimum = parameter.Minimum,
                    Maximum = parameter.Maximum
                });
            }

            catalogue.Modifiers.Add(state);
        }

        return Task.FromResult(catalogue);
    }

    /// <inheritdoc />
    public override Task<ChainPresetList> ListChainPresets(Empty request, ServerCallContext context) =>
        Task.FromResult(PresetCommands.List(engine.Presets));

    /// <inheritdoc />
    public override Task<DiagnosticsState> GetDiagnostics(Empty request, ServerCallContext context) =>
        Task.FromResult(MixerDiagnostics.Build(engine));

    /// <inheritdoc />
    public override async Task StreamMeters(Empty request, IServerStreamWriter<MeterFrame> responses, ServerCallContext context)
    {
        TimeSpan interval = TimeSpan.FromSeconds(1.0 / MeterPublisher.FramesPerSecond);

        using CancellationTokenSource stopping = Stopping(context);

        try
        {
            while (!stopping.Token.IsCancellationRequested)
            {
                if (engine.Meters is { } meters)
                {
                    await responses.WriteAsync(BuildFrame(meters), stopping.Token).ConfigureAwait(false);
                }

                await Task.Delay(interval, stopping.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Either the console went away or the engine is stopping. Both end this stream and
            // neither is an error.
        }
    }

    /// <summary>
    /// A token that ends a stream when the console goes away <b>or</b> the engine is stopping.
    /// </summary>
    /// <remarks>
    /// A meter stream lasts as long as a meeting, so it never ends on its own. Left waiting only on
    /// the call's own token, graceful shutdown sits on these until its timeout expires -- half a
    /// minute between asking the engine to stop and it being gone, which makes restarting it from
    /// the console look broken and asking it to stop look ignored.
    /// </remarks>
    CancellationTokenSource Stopping(ServerCallContext context) =>
        CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, lifetime.ApplicationStopping);

    /// <inheritdoc />
    public override async Task StreamEvents(Empty request, IServerStreamWriter<EngineEvent> responses, ServerCallContext context)
    {
        // Devices coming and going, faults, modifiers switched out. Queued rather than pushed from
        // wherever they happen, so a slow client cannot hold up the control loop.
        System.Threading.Channels.Channel<EngineEvent> queue =
            System.Threading.Channels.Channel.CreateBounded<EngineEvent>(256);

        void OnDeviceChange(object? sender, DeviceChange change) =>
            queue.Writer.TryWrite(new EngineEvent
            {
                TimestampTicks = change.Timestamp.UtcTicks,
                Kind = change.Kind.ToString(),
                Subject = change.FriendlyName,
                Message = $"{change.FriendlyName} {change.Kind}."
            });

        if (engine.Supervisor is { } supervisor)
        {
            supervisor.Changed += OnDeviceChange;
        }

        using CancellationTokenSource stopping = Stopping(context);

        try
        {
            await foreach (EngineEvent message in queue.Reader.ReadAllAsync(stopping.Token).ConfigureAwait(false))
            {
                await responses.WriteAsync(message, stopping.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // The client went away, or the engine is stopping. Not an error; consoles come and go
            // and the session continues without them, right up until it does not.
        }
        finally
        {
            if (engine.Supervisor is { } detach)
            {
                detach.Changed -= OnDeviceChange;
            }
        }
    }

    static CommandReply Refuse(string reason) => new() { Accepted = false, Reason = reason };

    static CommandReply Accept() => new() { Accepted = true, Reason = string.Empty };

    static MeterFrame BuildFrame(MeterPublisher meters)
    {
        int channels = meters.Channels.Length;
        int buses = meters.Buses.Length;
        byte[] payload = new byte[MeterFrameCodec.SizeOf(channels, buses)];

        for (int index = 0; index < channels; index++)
        {
            MeterReading reading = meters.Channels[index];

            MeterFrameCodec.WriteChannel(payload, index, new ChannelMeter(
                reading.PeakDb,
                reading.RmsDb,
                reading.GainReductionDb,
                reading.AutomixShare,
                (byte)((reading.IsDucked ? MeterFlags.Ducked : MeterFlags.None)
                    | (reading.HasClipped ? MeterFlags.Clipped : MeterFlags.None)
                    | (reading.IsSpeaking ? MeterFlags.Speaking : MeterFlags.None))));
        }

        for (int index = 0; index < buses; index++)
        {
            MeterFrameCodec.WriteBus(payload, channels, index, meters.Buses[index].PeakDb, meters.Buses[index].RmsDb);
        }

        return new MeterFrame
        {
            TimestampTicks = DateTimeOffset.UtcNow.UtcTicks,
            Payload = Google.Protobuf.ByteString.CopyFrom(payload),
            ChannelCount = channels,
            BusCount = buses
        };
    }

    ConsoleState BuildConsole()
    {
        ConsoleState state = new();

        if (engine.Graph is not { } graph)
        {
            return state;
        }

        GraphConfig config = graph.Config;
        GraphSnapshot snapshot = graph.Publisher.Current;

        for (int index = 0; index < config.Channels.Count; index++)
        {
            state.Channels.Add(BuildChannel(config, index));
        }

        AddBuses(state, config, snapshot);
        AddSends(state, snapshot);

        state.Automix = BuildAutomix(config);
        state.Recording = BuildRecording();
        state.Health = BuildHealth();
        state.Startup = new StartupOptions
        {
            LoadLastConsole = engine.Startup.LoadLastConsole,
            RecordAutomatically = engine.Startup.RecordAutomatically
        };

        return state;
    }

    void AddBuses(ConsoleState state, GraphConfig config, GraphSnapshot snapshot)
    {
        for (int index = 0; index < config.Buses.Count; index++)
        {
            BusConfig bus = config.Buses[index];

            BusState wire = new()
            {
                Index = index,
                Name = bus.Name,
                Role = bus.Role.ToString(),
                ChannelCount = bus.ChannelCount,
                GainDb = bus.GainDb,
                IsMuted = bus.IsMuted,
                OutputDeviceId = bus.OutputDeviceId.Value,
                OutputDeviceName = DeviceName(bus.OutputDeviceId, DeviceDirection.Render),
                Colour = bus.Colour,

                // D8. Monitor sends are pre-fader, so moving a fader does not change what the person
                // in the chair hears. Derived from the role rather than configured, like everything
                // else the role decides.
                IsPreFader = index < snapshot.BusCount && snapshot.Buses[index].IsPreFader
            };

            AddBusChain(wire, bus, snapshot, index);
            state.Buses.Add(wire);

            AddExclusions(wire, snapshot, index);
        }
    }

    /// <summary>
    /// D4's exclusions, read off the compiled matrix rather than off the configuration.
    /// </summary>
    /// <remarks>
    /// The engine works them out from the declared pairing; the console shows the answer, and there
    /// is deliberately no way for it to send a different one.
    /// </remarks>
    static void AddExclusions(BusState wire, GraphSnapshot snapshot, int index)
    {
        for (int channel = 0; channel < snapshot.Sends.ChannelCount; channel++)
        {
            if (snapshot.Sends.StateOf(channel, index) == EngineSendState.ExcludedMixMinus)
            {
                wire.ExcludedChannels.Add(channel);
            }
        }
    }

    static void AddSends(ConsoleState state, GraphSnapshot snapshot)
    {
        for (int channel = 0; channel < snapshot.Sends.ChannelCount; channel++)
        {
            for (int bus = 0; bus < snapshot.Sends.BusCount; bus++)
            {
                state.Sends.Add(new WireSendState
                {
                    ChannelIndex = channel,
                    BusIndex = bus,
                    State = snapshot.Sends.StateOf(channel, bus).ToString(),
                    LevelDb = 0
                });
            }
        }
    }

    /// <summary>Fills in a bus's chain, its limiter activity, and whether the engine added the limiter.</summary>
    static void AddBusChain(BusState wire, BusConfig bus, GraphSnapshot snapshot, int index)
    {
        IReadOnlyList<Vam.Engine.Modifiers.ModifierSetting> effective = GraphCompiler.EffectiveBusChain(bus);

        wire.HasMandatoryLimiter = effective.Count > bus.Chain.Count;

        foreach (AudioNode node in snapshot.Plan.Nodes)
        {
            if (node is not BusChainNode chainNode || chainNode.BusIndex != index)
            {
                continue;
            }

            ModifierChain chain = chainNode.Chain;
            ChainParams parameters = snapshot.BusChainOf(index);

            for (int link = 0; link < chain.Count; link++)
            {
                wire.Chain.Add(BuildLink(
                    link < effective.Count ? effective[link] : null,
                    chain,
                    link,
                    parameters.IsBypassed(link)));

                // D6. What the limiter is taking off, read from the modifier's own telemetry. It is
                // an activity light rather than a meter: once a second is enough to answer "is it
                // working", which is the only question the bus strip asks.
                if (chain.Modifiers[link].Descriptor.Id == GraphCompiler.MandatoryLimiterId
                    || chain.Modifiers[link].Descriptor.Id == "vam.limiter")
                {
                    wire.LimiterReductionDb = chain.Telemetry[link].GainReductionDb;
                }
            }
        }
    }

    static ModifierState BuildLink(
        Vam.Engine.Modifiers.ModifierSetting? setting,
        ModifierChain chain,
        int link,
        bool isBypassed)
    {
        Modifier modifier = chain.Modifiers[link];

        ModifierState state = new()
        {
            LinkId = chain.LinkIds[link],
            ModifierId = modifier.Descriptor.Id,
            Name = modifier.Descriptor.Name,
            IsBypassed = isBypassed,
            CostFraction = 0
        };

        foreach (ParameterDescriptor descriptor in modifier.Parameters)
        {
            state.Parameters.Add(new ParameterState
            {
                Id = descriptor.Id,
                Name = descriptor.Name,
                Unit = descriptor.Unit,
                Value = setting is not null && setting.Values.TryGetValue(descriptor.Id, out float saved)
                    ? descriptor.Clamp(saved)
                    : descriptor.Default,
                Minimum = descriptor.Minimum,
                Maximum = descriptor.Maximum
            });
        }

        return state;
    }

    string DeviceName(Vam.Engine.Devices.Abstractions.AudioDeviceId id, DeviceDirection direction)
    {
        if (id.IsNone || engine.Backend is not { } backend)
        {
            return string.Empty;
        }

        foreach (AudioDeviceInfo device in backend.Enumerate(direction))
        {
            if (device.Id == id)
            {
                return device.FriendlyName;
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// Fills in a strip's chain, from the compiled plan rather than from the configuration.
    /// </summary>
    /// <remarks>
    /// The configuration knows which modifiers a strip has and what an operator set; only the
    /// compiled chain knows what parameters those modifiers actually declare, and what the defaults
    /// are for the ones nobody has touched. Reading it from the configuration alone sent a console
    /// a chain with no parameters in it — an equaliser with no bands and a compressor with no
    /// threshold, which is a panel an operator cannot use.
    /// </remarks>
    static void AddChannelChain(ChannelState state, ChannelConfig channel, GraphSnapshot snapshot, int index)
    {
        foreach (AudioNode node in snapshot.Plan.Nodes)
        {
            if (node is not ChainNode chainNode || chainNode.ChannelIndex != index)
            {
                continue;
            }

            ModifierChain chain = chainNode.Chain;
            ChainParams parameters = snapshot.ChainOf(index);

            for (int link = 0; link < chain.Count; link++)
            {
                state.Chain.Add(BuildLink(
                    link < channel.Chain.Count ? channel.Chain[link] : null,
                    chain,
                    link,
                    parameters.IsBypassed(link)));
            }
        }
    }

    static double LevelOf(GraphConfig config, int channelIndex, int busIndex)
    {
        foreach (SendConfig send in config.Sends)
        {
            if (send.ChannelIndex == channelIndex && send.BusIndex == busIndex)
            {
                return send.LevelDb;
            }
        }

        return 0;
    }

    /// <summary>The handful of numbers the status bar shows. U1.</summary>
    /// <remarks>
    /// Carried with the console rather than behind GetDiagnostics: the status bar is on every view,
    /// and an operator glancing at it must not be paying for a drift history nobody opened.
    /// </remarks>
    EngineHealth BuildHealth() => new()
    {
        Load = engine.Load,

        // What is left, which is the number an operator actually reads, because it is the one that
        // runs out. Clamped rather than allowed to go negative: a callback that took two blocks has
        // no headroom, and "minus a hundred per cent" is not a thing anybody can act on.
        Headroom = Math.Clamp(1.0 - engine.Load, 0.0, 1.0),
        Dropouts = engine.Dropouts.TotalRecorded,
        UptimeTicks = engine.StartedAt is { } started ? (DateTimeOffset.UtcNow - started).Ticks : 0,
        IsTimerFallback = engine.Clock is null || engine.Clock.PrimaryDeviceId.IsNone
    };

    ChannelState BuildChannel(GraphConfig config, int index)
    {
        ChannelConfig channel = config.Channels[index];

        ChannelState state = new()
        {
            Index = index,
            Name = channel.Name,
            DeviceId = channel.DeviceId.Value,
            DeviceName = channel.Name,
            ChannelCount = channel.ChannelCount,
            TrimDb = channel.TrimDb,
            FaderDb = channel.FaderDb,
            IsMuted = (channel.Flags & ChannelFlags.Muted) != 0,
            IsSoloed = (channel.Flags & ChannelFlags.Soloed) != 0,
            IsPolarityInverted = (channel.Flags & ChannelFlags.PolarityInverted) != 0,
            IsPreFadeListen = (channel.Flags & ChannelFlags.PreFadeListen) != 0,
            IsMonoFold = (channel.Flags & ChannelFlags.MonoFold) != 0,
            ParticipatesInAutomix = channel.ParticipatesInAutomix,
            AutomixWeight = channel.AutomixWeight,
            Pan = channel.Pan,
            Colour = channel.Colour,
            PresetName = channel.PresetName,

            // B12. An operator about to save over a preset needs to know whether what they are
            // saving is what they think.
            IsPresetModified = engine.Presets.IsModified(channel.PresetName, channel.Chain),
            DeviceState = "unknown"
        };

        // D2. The per-pair send level, so the console can show and change it without a second call.
        GraphSnapshot sends = engine.Graph!.Publisher.Current;

        for (int bus = 0; bus < sends.Sends.BusCount; bus++)
        {
            state.SendLevelsDb.Add(LevelOf(config, index, bus));
        }

        // The name the operating system gives the endpoint, beside the name the operator gave the
        // strip. "Mayor" and "Trust USB Microphone" are both worth having on a strip that has
        // stopped producing sound.
        if (engine.Backend is { } backend)
        {
            foreach (AudioDeviceInfo device in backend.Enumerate(DeviceDirection.Capture))
            {
                if (device.Id == channel.DeviceId)
                {
                    state.DeviceName = device.FriendlyName;
                    break;
                }
            }
        }

        if (index < engine.Channels.Count)
        {
            DeviceTelemetry telemetry = engine.Channels.Channels[index].GetTelemetry();

            state.MeasuredSampleRate = telemetry.MeasuredSampleRate;
            state.DriftPpm = telemetry.DriftPpm;
            state.DeviceState = telemetry.State.ToString();
        }

        AddChannelChain(state, channel, sends, index);

        return state;
    }

    WireAutomixState BuildAutomix(GraphConfig config)
    {
        WireAutomixState state = new()
        {
            IsBypassed = config.IsAutomixBypassed,
            DepthDb = config.AutomixDepthDb,
            ResponseMs = config.AutomixResponseMilliseconds
        };

        if (engine.AutomixState() is { } automix)
        {
            state.OpenMicrophones = automix.NumberOfOpenMicrophones;

            foreach (float share in automix.Shares)
            {
                state.Shares.Add(share);
            }

            foreach (float gain in automix.GainsDb)
            {
                state.GainsDb.Add(gain);
            }
        }

        return state;
    }

    RecordingState BuildRecording()
    {
        RecordingState state = new() { IsRecording = engine.Recording?.IsRecording ?? false };

        if (engine.Recording is not { } recording)
        {
            return state;
        }

        foreach (Vam.Engine.Recording.RecordingTrack track in recording.Tracks)
        {
            // The longest track, not the sum of them. Every consumer reads this as how long the
            // recording has been running, and summing made a three-track session count three
            // seconds per second - a clock beside the session clock, disagreeing with it.
            state.FramesWritten = Math.Max(state.FramesWritten, track.FramesWritten);

            // Summed, because this one really is a total: a frame lost on any track is a frame lost.
            state.DroppedFrames += track.DroppedFrames;
        }

        return state;
    }
}
