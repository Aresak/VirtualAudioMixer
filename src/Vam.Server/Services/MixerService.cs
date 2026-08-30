using Grpc.Core;
using Vam.Engine.Devices;
using Vam.Engine.Devices.Abstractions;
using Vam.Engine.Recording;
using Vam.Modifiers.Abstractions;
using Vam.Engine.Graph;
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
public sealed class MixerService(VamEngine engine, ILogger<MixerService> logger) : Mixer.MixerBase
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
    public override Task<CommandReply> Apply(Command request, ServerCallContext context)
    {
        if (engine.Graph is null)
        {
            return Task.FromResult(Refuse("The engine is not running."));
        }

        try
        {
            // Three commands change what the engine owns rather than what the graph holds - a device
            // to open, a modifier to build, a file to create - so they are answered here where the
            // engine is, and everything else goes to the graph.
            return Task.FromResult(DispatchEngine(request) ?? Dispatch(engine.Graph, request));
        }
        catch (Exception error)
        {
            logger.LogError(error, "A command failed.");
            return Task.FromResult(Refuse(error.Message));
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
    public override Task<DiagnosticsState> GetDiagnostics(Empty request, ServerCallContext context) =>
        Task.FromResult(MixerDiagnostics.Build(engine));

    /// <inheritdoc />
    public override async Task StreamMeters(Empty request, IServerStreamWriter<MeterFrame> responses, ServerCallContext context)
    {
        TimeSpan interval = TimeSpan.FromSeconds(1.0 / MeterPublisher.FramesPerSecond);

        while (!context.CancellationToken.IsCancellationRequested)
        {
            if (engine.Meters is { } meters)
            {
                await responses.WriteAsync(BuildFrame(meters), context.CancellationToken).ConfigureAwait(false);
            }

            await Task.Delay(interval, context.CancellationToken).ConfigureAwait(false);
        }
    }

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

        try
        {
            await foreach (EngineEvent message in queue.Reader.ReadAllAsync(context.CancellationToken).ConfigureAwait(false))
            {
                await responses.WriteAsync(message, context.CancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // The client went away. Not an error; consoles come and go and the session continues.
        }
        finally
        {
            if (engine.Supervisor is { } detach)
            {
                detach.Changed -= OnDeviceChange;
            }
        }
    }

    CommandReply? DispatchEngine(Command request)
    {
        switch (request.KindCase)
        {
            case Command.KindOneofCase.AddChannel:
                return engine.AddChannel(new Vam.Engine.Graph.ChannelConfig
                {
                    DeviceId = new AudioDeviceId(request.AddChannel.DeviceId),
                    Name = request.AddChannel.Name,
                    ChannelCount = Math.Max(request.AddChannel.ChannelCount, 1),
                    ParticipatesInAutomix = request.AddChannel.ParticipatesInAutomix
                }) >= 0
                    ? Accept()
                    : Refuse("The engine is not running.");

            case Command.KindOneofCase.AddModifier:
                return AddLink(request.AddModifier);

            case Command.KindOneofCase.SetRecording:
                if (!request.SetRecording.Recording)
                {
                    return engine.StopRecording() ? Accept() : Refuse("Nothing was recording.");
                }

                DiskVerdict verdict = engine.StartRecording(
                    string.IsNullOrWhiteSpace(request.SetRecording.Directory) ? null : request.SetRecording.Directory);

                // The disk's answer, in the disk's own words. "Recording did not start" tells an
                // operator nothing they can act on; "there is room for forty minutes" does.
                return verdict.CanStart ? Accept() : Refuse(verdict.Description);

            default:
                return null;
        }
    }

    CommandReply AddLink(AddModifier request)
    {
        if (engine.Graph is not { } graph
            || request.ChannelIndex < 0
            || request.ChannelIndex >= graph.Config.Channels.Count)
        {
            return Refuse($"There is no strip {request.ChannelIndex}.");
        }

        if (engine.Modifiers.Create(request.ModifierId) is null)
        {
            return Refuse($"This engine has no modifier called '{request.ModifierId}'.");
        }

        List<Vam.Engine.Modifiers.ModifierSetting> chain = graph.Config.Channels[request.ChannelIndex].Chain;
        int at = Math.Clamp(request.AtIndex, 0, chain.Count);

        chain.Insert(at, new Vam.Engine.Modifiers.ModifierSetting
        {
            LinkId = Guid.NewGuid().ToString("n"),
            ModifierId = request.ModifierId
        });

        graph.Recompile();

        return Accept();
    }

    static CommandReply Refuse(string reason) => new() { Accepted = false, Reason = reason };

    static CommandReply Accept() => new() { Accepted = true, Reason = string.Empty };

    static CommandReply Dispatch(GraphController graph, Command request)
    {
        switch (request.KindCase)
        {
            case Command.KindOneofCase.SetFader:
                graph.Submit(GraphCommand.SetFader(request.SetFader.ChannelIndex, request.SetFader.Decibels));
                break;

            case Command.KindOneofCase.SetTrim:
                graph.Submit(GraphCommand.SetTrim(request.SetTrim.ChannelIndex, request.SetTrim.Decibels));
                break;

            case Command.KindOneofCase.SetFlag:
                // Whether a strip takes part in gain sharing is not one of the audio thread's flag
                // bits - it decides what the compiler builds, not what the mix does - so it is set
                // by rewriting the strip. It arrives here because to an operator it is the same
                // kind of thing as mute: a switch on a channel.
                if (string.Equals(request.SetFlag.Flag, "ParticipatesInAutomix", StringComparison.OrdinalIgnoreCase))
                {
                    return Rewrite(graph, request.SetFlag.ChannelIndex,
                        channel => channel with { ParticipatesInAutomix = request.SetFlag.Enabled });
                }

                if (!Enum.TryParse(request.SetFlag.Flag, ignoreCase: true, out ChannelFlags flag))
                {
                    return Refuse($"There is no flag called '{request.SetFlag.Flag}'.");
                }

                graph.Submit(GraphCommand.SetFlag(request.SetFlag.ChannelIndex, flag, request.SetFlag.Enabled));
                break;

            case Command.KindOneofCase.SetBusGain:
                graph.Submit(GraphCommand.SetBusGain(request.SetBusGain.BusIndex, request.SetBusGain.Decibels));
                break;

            case Command.KindOneofCase.SetBusMuted:
                graph.Submit(GraphCommand.SetBusMuted(request.SetBusMuted.BusIndex, request.SetBusMuted.Muted));
                break;

            case Command.KindOneofCase.SetSend:
                return ApplySend(graph, request.SetSend);

            case Command.KindOneofCase.AddBus:
                graph.AddBus(new BusConfig
                {
                    Name = request.AddBus.Name,
                    Role = Enum.TryParse(request.AddBus.Role, ignoreCase: true, out BusRole role) ? role : BusRole.Output,
                    ChannelCount = Math.Max(request.AddBus.ChannelCount, 1),
                    OutputDeviceId = new Vam.Engine.Devices.Abstractions.AudioDeviceId(request.AddBus.OutputDeviceId)
                });

                return Accept();

            case Command.KindOneofCase.RemoveBus:
                return graph.RemoveBus(request.RemoveBus.BusIndex)
                    ? Accept()
                    : Refuse($"There is no bus {request.RemoveBus.BusIndex}.");

            case Command.KindOneofCase.SetAutomix:
                graph.Config.IsAutomixBypassed = request.SetAutomix.Bypassed;
                graph.Config.AutomixDepthDb = request.SetAutomix.DepthDb;
                graph.Config.AutomixResponseMilliseconds = request.SetAutomix.ResponseMs;
                graph.Submit(GraphCommand.SetFader(0, graph.Config.Channels.Count > 0 ? graph.Config.Channels[0].FaderDb : 0));
                break;

            case Command.KindOneofCase.SetModifierBypass:
                return ApplyBypass(graph, request.SetModifierBypass);

            case Command.KindOneofCase.SetModifierParameter:
                return ApplyParameter(graph, request.SetModifierParameter);

            case Command.KindOneofCase.SetChannelName:
                return Rewrite(graph, request.SetChannelName.ChannelIndex,
                    channel => channel with { Name = request.SetChannelName.Name });

            case Command.KindOneofCase.SetChannelColour:
                return Rewrite(graph, request.SetChannelColour.ChannelIndex,
                    channel => channel with { Colour = request.SetChannelColour.Colour });

            case Command.KindOneofCase.RemoveChannel:
                return graph.RemoveChannel(request.RemoveChannel.ChannelIndex)
                    ? Accept()
                    : Refuse($"There is no strip {request.RemoveChannel.ChannelIndex}.");

            case Command.KindOneofCase.MoveChannel:
                return graph.MoveChannel(request.MoveChannel.FromIndex, request.MoveChannel.ToIndex)
                    ? Accept()
                    : Refuse("A strip cannot be moved to a place that is not there.");

            case Command.KindOneofCase.SetBusName:
                if (request.SetBusName.BusIndex < 0 || request.SetBusName.BusIndex >= graph.Config.Buses.Count)
                {
                    return Refuse($"There is no bus {request.SetBusName.BusIndex}.");
                }

                graph.Config.Buses[request.SetBusName.BusIndex] =
                    graph.Config.Buses[request.SetBusName.BusIndex] with { Name = request.SetBusName.Name };

                graph.Recompile();

                return Accept();

            case Command.KindOneofCase.RemoveModifier:
                return RemoveLink(graph, request.RemoveModifier);

            case Command.KindOneofCase.MoveModifier:
                return MoveLink(graph, request.MoveModifier);

            default:
                return Refuse("The command carried nothing.");
        }

        graph.Pump();

        return Accept();
    }

    /// <summary>Rewrites one strip's configuration and recompiles.</summary>
    /// <remarks>
    /// <see cref="ChannelConfig"/> is a record with init-only properties, so a change is a new one
    /// put back in place. That is what makes a half-applied strip impossible: either the whole thing
    /// went in or none of it did.
    /// </remarks>
    static CommandReply Rewrite(GraphController graph, int index, Func<ChannelConfig, ChannelConfig> change)
    {
        if (index < 0 || index >= graph.Config.Channels.Count)
        {
            return Refuse($"There is no strip {index}.");
        }

        graph.Config.Channels[index] = change(graph.Config.Channels[index]);
        graph.Recompile();

        return Accept();
    }

    static CommandReply RemoveLink(GraphController graph, RemoveModifier request)
    {
        if (request.ChannelIndex < 0 || request.ChannelIndex >= graph.Config.Channels.Count)
        {
            return Refuse($"There is no strip {request.ChannelIndex}.");
        }

        List<Vam.Engine.Modifiers.ModifierSetting> chain = graph.Config.Channels[request.ChannelIndex].Chain;

        if (request.LinkIndex < 0 || request.LinkIndex >= chain.Count)
        {
            return Refuse($"Strip {request.ChannelIndex} has no link {request.LinkIndex}.");
        }

        chain.RemoveAt(request.LinkIndex);
        graph.Recompile();

        return Accept();
    }

    static CommandReply MoveLink(GraphController graph, MoveModifier request)
    {
        if (request.ChannelIndex < 0 || request.ChannelIndex >= graph.Config.Channels.Count)
        {
            return Refuse($"There is no strip {request.ChannelIndex}.");
        }

        List<Vam.Engine.Modifiers.ModifierSetting> chain = graph.Config.Channels[request.ChannelIndex].Chain;

        if (request.FromIndex < 0 || request.FromIndex >= chain.Count
            || request.ToIndex < 0 || request.ToIndex >= chain.Count)
        {
            return Refuse("A link cannot be moved to a place that is not there.");
        }

        // Order is the configuration, not an incidental list order: a gate before a denoise and a
        // gate after one are different microphones. B0.
        Vam.Engine.Modifiers.ModifierSetting moving = chain[request.FromIndex];

        chain.RemoveAt(request.FromIndex);
        chain.Insert(request.ToIndex, moving);

        graph.Recompile();

        return Accept();
    }

    static CommandReply ApplySend(GraphController graph, SetSend request)
    {
        GraphSnapshot before = graph.Publisher.Current;

        if (request.ChannelIndex < before.Sends.ChannelCount
            && request.BusIndex < before.Sends.BusCount
            && before.Sends.StateOf(request.ChannelIndex, request.BusIndex) == EngineSendState.ExcludedMixMinus)
        {
            // Said out loud rather than silently doing nothing. An operator clicking a send that
            // does not respond needs to know it is mix-minus and not a broken button.
            return Refuse(
                "That send is excluded by mix-minus: the bus feeds the device this microphone belongs to, "
                + "and sending it there would play somebody their own voice, late.");
        }

        graph.Submit(GraphCommand.SetSend(request.ChannelIndex, request.BusIndex, request.On, request.Decibels));
        graph.Pump();

        return Accept();
    }

    static CommandReply ApplyBypass(GraphController graph, SetModifierBypass request)
    {
        if (request.ChannelIndex < 0 || request.ChannelIndex >= graph.Config.Channels.Count)
        {
            return Refuse($"There is no strip {request.ChannelIndex}.");
        }

        List<Vam.Engine.Modifiers.ModifierSetting> chain = graph.Config.Channels[request.ChannelIndex].Chain;

        if (request.LinkIndex < 0 || request.LinkIndex >= chain.Count)
        {
            return Refuse($"Strip {request.ChannelIndex} has no link {request.LinkIndex}.");
        }

        chain[request.LinkIndex] = chain[request.LinkIndex] with { IsBypassed = request.Bypassed };
        graph.Recompile();

        return Accept();
    }

    static CommandReply ApplyParameter(GraphController graph, SetModifierParameter request)
    {
        if (request.ChannelIndex < 0 || request.ChannelIndex >= graph.Config.Channels.Count)
        {
            return Refuse($"There is no strip {request.ChannelIndex}.");
        }

        List<Vam.Engine.Modifiers.ModifierSetting> chain = graph.Config.Channels[request.ChannelIndex].Chain;

        if (request.LinkIndex < 0 || request.LinkIndex >= chain.Count)
        {
            return Refuse($"Strip {request.ChannelIndex} has no link {request.LinkIndex}.");
        }

        chain[request.LinkIndex].Values[request.ParameterId] = (float)request.Value;
        graph.Recompile();

        return Accept();
    }

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
                (byte)(reading.IsDucked ? MeterFlags.Ducked : MeterFlags.None)));
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

        for (int index = 0; index < config.Buses.Count; index++)
        {
            BusConfig bus = config.Buses[index];

            state.Buses.Add(new BusState
            {
                Index = index,
                Name = bus.Name,
                Role = bus.Role.ToString(),
                ChannelCount = bus.ChannelCount,
                GainDb = bus.GainDb,
                IsMuted = bus.IsMuted,
                OutputDeviceId = bus.OutputDeviceId.Value
            });

            // D4's exclusions, read off the compiled matrix rather than off the configuration. The
            // engine works them out from the declared pairing; the console shows the answer, and
            // there is deliberately no way for it to send a different one.
            for (int channel = 0; channel < snapshot.Sends.ChannelCount; channel++)
            {
                if (snapshot.Sends.StateOf(channel, index) == EngineSendState.ExcludedMixMinus)
                {
                    state.Buses[index].ExcludedChannels.Add(channel);
                }
            }
        }

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

        state.Automix = BuildAutomix(config);
        state.Recording = BuildRecording();

        return state;
    }

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
            IsMonoFold = (channel.Flags & ChannelFlags.MonoFold) != 0,
            ParticipatesInAutomix = channel.ParticipatesInAutomix,
            AutomixWeight = channel.AutomixWeight,
            Colour = channel.Colour,
            DeviceState = "unknown"
        };

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

        foreach (Vam.Engine.Modifiers.ModifierSetting link in channel.Chain)
        {
            state.Chain.Add(new ModifierState
            {
                LinkId = link.LinkId,
                ModifierId = link.ModifierId,
                Name = link.ModifierId,
                IsBypassed = link.IsBypassed
            });
        }

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
            state.FramesWritten += track.FramesWritten;
            state.DroppedFrames += track.DroppedFrames;
        }

        return state;
    }
}
