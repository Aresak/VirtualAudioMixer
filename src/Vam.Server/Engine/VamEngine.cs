using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Vam.Engine.Automix;
using Vam.Engine.Configuration;
using Vam.Engine.Devices;
using Vam.Engine.Devices.Abstractions;
using Vam.Engine.Diagnostics;
using Vam.Engine.Graph;
using Vam.Engine.Graph.Nodes;
using Vam.Engine.Metering;
using Vam.Engine.Modifiers;
using Vam.Engine.Recording;
using Vam.Engine.Modifiers.BuiltIn;
using Vam.Engine.Windows.Devices.Wasapi;
using Vam.Engine.Windows.Dsp;

namespace Vam.Server.Engine;

/// <summary>
/// Everything the engine is, wired together and running. G1.
/// </summary>
/// <remarks>
/// <para>
/// <b>Headless, and that is the requirement rather than a convenience.</b> This process owns the
/// devices, the graph, the clock and the recording, and it keeps running whether or not anything is
/// looking at it. A console crashing, being closed, or being killed by an operator who thought it
/// had hung must not take the meeting with it.
/// </para>
/// <para>
/// One control loop, on one thread, doing everything that is not audio: draining commands, checking
/// devices, advancing drift corrections, publishing meters and guarding the cost budget. The audio
/// threads never wait for any of it.
/// </para>
/// </remarks>
public sealed class VamEngine : IDisposable
{
    readonly EngineOptions options;
    readonly ILoggerFactory loggers;
    readonly ILogger<VamEngine> logger;
    readonly ConsoleStore store;
    readonly CancellationTokenSource stopping = new();
    readonly Dictionary<(int Channel, int Link), long> overruns = [];

    IAudioBackend? backend;
    DropoutPump? dropoutPump;
    Thread? control;
    TimeSpan sinceCorrection;
    TimeSpan sinceMeters;
    TimeSpan sinceLoad;

    readonly IAudioBackend? supplied;

    /// <summary>Builds the engine from a configuration on disk.</summary>
    /// <param name="options">How to set it up.</param>
    /// <param name="loggers">Where everything reports.</param>
    /// <param name="backend">
    /// The devices to use. Null opens the real ones. Supplying one is how the whole engine — the
    /// graph, the clock, the recording and the protocol on top of them — gets exercised on a machine
    /// with nothing plugged in, which is most machines and every CI runner.
    /// </param>
    public VamEngine(EngineOptions options, ILoggerFactory loggers, IAudioBackend? backend = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(loggers);

        this.options = options;
        this.loggers = loggers;

        supplied = backend;
        Presets = new ChainPresetStore(options.PresetPath);

        logger = loggers.CreateLogger<VamEngine>();
        store = new ConsoleStore(loggers.CreateLogger<ConsoleStore>());

        Channels = new DeviceInputChannelRegistry();
        Modifiers = ModifierRegistry.CreateDefault();
        Dropouts = new DropoutLog();
    }

    /// <summary>The live input channels, for telemetry.</summary>
    public DeviceInputChannelRegistry Channels { get; }

    /// <summary>What modifiers exist.</summary>
    public ModifierRegistry Modifiers { get; }

    /// <summary>Where the audio threads note things going wrong. I2.</summary>
    public DropoutLog Dropouts { get; }

    /// <summary>The devices this engine can see, or null before it started.</summary>
    /// <remarks>
    /// Exposed so a console can be told what is plugged into the engine's machine. A console on a
    /// laptop cannot enumerate the sound cards of the machine wired to the microphones, and asking
    /// it to would make the remote case quietly different from the local one.
    /// </remarks>
    public IAudioBackend? Backend => backend;

    /// <summary>How long the render callback has been taking. K4.</summary>
    public CallbackHistogram Callbacks { get; } = new();

    /// <summary>What the audio thread allocated. K5, and it reads zero.</summary>
    public AudioThreadAllocations Allocations { get; } = new();

    /// <summary>Drift and ring fill over the session. K2.</summary>
    public DriftHistory Drift { get; } = new();

    /// <summary>When the engine started, or null if it has not.</summary>
    /// <remarks>
    /// The engine's uptime, not the console's. G1 has the session outliving every console, so a
    /// console that showed its own connection time would be answering a different question from the
    /// one an operator is asking.
    /// </remarks>
    public DateTimeOffset? StartedAt { get; private set; }

    /// <summary>
    /// How close the audio thread is to its deadline, as a fraction of a block.
    /// </summary>
    /// <remarks>
    /// The worst block since this was last read rather than the worst ever: one bad block during
    /// startup would otherwise leave the status bar reading ninety per cent all evening, and an
    /// operator would learn to ignore it. Read once a second by the control loop.
    /// </remarks>
    public double Load { get; private set; }

    /// <summary>
    /// What mutes a strip whose device has failed. I1.
    /// </summary>
    /// <remarks>
    /// The graph has always honoured the faulted flag; nothing set it. This is what arms the single
    /// most important behaviour in the project — an error inside one strip never reaching the mix.
    /// </remarks>
    public FaultWatch? Faults { get; private set; }

    /// <summary>
    /// The render devices the secondary buses play to. D7.
    /// </summary>
    /// <remarks>
    /// The primary bus goes out through the master clock. Everything else — every monitor, every
    /// extra output — needs its own device thread, and this is what opens them. Without it a bus
    /// with an output configured fills a ring nobody drains and is simply silent.
    /// </remarks>
    public BusOutputHost? BusOutputs { get; private set; }

    /// <summary>The chain presets this engine knows about. B0d and B12.</summary>
    public ChainPresetStore Presets { get; }

    /// <summary>How many times one link has overrun its budget. K6.</summary>
    /// <remarks>
    /// Kept per link rather than as a total, because the useful question is which modifier is the
    /// expensive one and a single number cannot answer it.
    /// </remarks>
    /// <param name="channelIndex">Which strip.</param>
    /// <param name="linkIndex">Which link of its chain.</param>
    /// <returns>The count, or zero.</returns>
    public long OverrunsOf(int channelIndex, int linkIndex) =>
        overruns.TryGetValue((channelIndex, linkIndex), out long count) ? count : 0;

    /// <summary>The console. Commands go here.</summary>
    public GraphController? Graph { get; private set; }

    /// <summary>What keeps devices open across a session.</summary>
    public DeviceSupervisor? Supervisor { get; private set; }

    /// <summary>What decides when a block of time has passed.</summary>
    public MasterClock? Clock { get; private set; }

    /// <summary>What a console draws.</summary>
    public MeterPublisher? Meters { get; private set; }

    /// <summary>The recording, if one is running.</summary>
    public RecordingSession? Recording { get; private set; }

    /// <summary>What virtual endpoints this machine has, and what to say when it has none. A6 and E2.</summary>
    public VirtualEndpointReport? VirtualEndpoints { get; private set; }

    /// <summary>Whether the control loop is running.</summary>
    public bool IsRunning => control is not null;

    /// <summary>
    /// Opens the devices, builds the console and starts running.
    /// </summary>
    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        backend = supplied ?? new WasapiBackend(loggers.CreateLogger<WasapiBackend>());

        ChooseNoiseSuppressor();

        Supervisor = new DeviceSupervisor(backend, Channels, loggers);

        GraphConfig config = LoadOrDiscover(backend);

        Graph = new GraphController(config, options.BlockFrames, options.SampleRate, Modifiers);
        Graph.Overran += OnOverran;

        OpenDevices(config);
        StartRecording(config);

        Clock = new MasterClock(
            backend,
            Channels,
            new MasterClockOptions
            {
                BlockFrames = options.BlockFrames,
                SampleRate = options.SampleRate,
                MaxDevices = Math.Max(config.Channels.Count, 1) + 8,
                MaxChannelsPerDevice = 2
            },
            loggers);

        // Wrapped rather than passed straight through, so K4 and K5 measure the real callback on a
        // real machine rather than a test harness's idea of one. Both are a handful of instructions.
        Clock.SetConsumer(RenderAndMeasure);
        Clock.Poll();

        BusOutputs = new BusOutputHost(backend, loggers);
        Faults = new FaultWatch(Graph, Channels, loggers.CreateLogger<FaultWatch>());

        BindBusOutputs(config);

        dropoutPump = new DropoutPump(Dropouts, loggers.CreateLogger<DropoutPump>());
        dropoutPump.SetNames([.. config.Channels.Select(channel => channel.Name)]);

        Meters = BuildMeterPublisher();
        VirtualEndpoints = VirtualEndpointReport.From(backend);

        // Said once at startup, whichever way it went. A first-time user without a virtual driver
        // gets a sentence naming what is missing and where to get it, and everything that does not
        // need one carries on working - which is most of the actual requirement in EPIC-13.
        if (VirtualEndpoints.CanTakeConferencingAudio && VirtualEndpoints.CanReachObs)
        {
            logger.LogInformation("{Report}", VirtualEndpoints.Description);
        }
        else
        {
            logger.LogWarning("{Report}", VirtualEndpoints.Description);
        }

        control = new Thread(Run)
        {
            IsBackground = true,
            Name = "vam-control"
        };

        StartedAt = DateTimeOffset.UtcNow;

        control.Start();

        logger.LogInformation(
            "Engine running: {Channels} strips, {Buses} buses, clock on {Clock}.",
            config.Channels.Count,
            config.Buses.Count,
            Clock.PrimaryDeviceId.IsNone ? "the fallback timer" : Clock.PrimaryDeviceId.Value);
    }

    /// <summary>Saves the console, stops everything and closes the files.</summary>
    public void Stop()
    {
        if (control is not null)
        {
            stopping.Cancel();
            control.Join(TimeSpan.FromSeconds(5));
            control = null;
        }

        if (Graph is not null)
        {
            // Saved on the way out rather than only when somebody asks. An operator who spent a
            // meeting setting a console up should not lose it because the machine was shut down.
            store.Save(options.ConsolePath, Graph.Config);
        }

        dropoutPump?.Pump();
        dropoutPump?.Flush();

        Recording?.Stop();
        Clock?.Stop();
        BusOutputs?.Dispose();
        BusOutputs = null;
        Supervisor?.Dispose();

        // Only the one this engine opened. A backend somebody handed in belongs to them, and
        // disposing it here would close devices they are still using.
        if (supplied is null)
        {
            backend?.Dispose();
        }

        backend = null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Stop();

        Recording?.Dispose();
        Clock?.Dispose();
        stopping.Dispose();
    }

    /// <summary>Saves the console now. H1.</summary>
    public void SaveConsole()
    {
        if (Graph is not null)
        {
            store.Save(options.ConsolePath, Graph.Config);
        }
    }

    /// <summary>
    /// Who the voice-activity tap says is speaking. B3 and F2.
    /// </summary>
    /// <remarks>
    /// Read from the published plan on the control thread. The tap writes it on the audio thread
    /// into an array it owns; a stale block here shows a speaking dot forty milliseconds late,
    /// which is nobody's problem.
    /// </remarks>
    /// <returns>One flag per strip, or empty when there is no plan.</returns>
    ReadOnlySpan<bool> SpeakingNow()
    {
        if (Graph is not { } graph)
        {
            return default;
        }

        foreach (AudioNode node in graph.Publisher.Current.Plan.Nodes)
        {
            if (node is VoiceActivityTapNode tap)
            {
                return tap.Speaking;
            }
        }

        return default;
    }

    /// <summary>What the automixer is doing, for the meters and the console.</summary>
    /// <returns>Its state, or null when there is no graph yet.</returns>
    public AutomixState? AutomixState()
    {
        if (Graph is null)
        {
            return null;
        }

        foreach (AudioNode node in Graph.Publisher.Current.Plan.Nodes)
        {
            if (node is AutomixNode automix)
            {
                return automix.State;
            }
        }

        return null;
    }

    GraphConfig LoadOrDiscover(IAudioBackend devices)
    {
        GraphConfig config = store.Load(options.ConsolePath);

        if (config.Channels.Count > 0)
        {
            return config;
        }

        // Nothing saved, so build something that works from what is plugged in. An engine that
        // opened to an empty console and waited to be told what to do would be useless the first
        // time somebody ran it.
        logger.LogInformation("No saved console. Building one from the devices that are present.");

        foreach (AudioDeviceInfo device in devices.Enumerate(DeviceDirection.Capture))
        {
            config.InputDeviceOrder.Add(device.Id);
            config.Channels.Add(new ChannelConfig
            {
                DeviceId = device.Id,
                Name = device.FriendlyName,
                ChannelCount = 1,
                Flags = device.ChannelCount > 1 ? ChannelFlags.MonoFold : ChannelFlags.None,
                ParticipatesInAutomix = true
            });
        }

        IReadOnlyList<AudioDeviceInfo> outputs = devices.Enumerate(DeviceDirection.Render);

        config.Buses.Add(new BusConfig
        {
            Name = "Stream",
            Role = BusRole.Stream,
            ChannelCount = 2,
            OutputDeviceId = outputs.Count > 0 ? outputs[0].Id : AudioDeviceId.None
        });

        for (int channel = 0; channel < config.Channels.Count; channel++)
        {
            config.Sends.Add(new SendConfig(channel, 0, IsOn: true, LevelDb: 0));
        }

        return config;
    }

    /// <summary>
    /// Adds a strip and opens its device. U17.
    /// </summary>
    /// <param name="channel">The strip.</param>
    /// <returns>Its index, or -1 when there is no graph to add it to.</returns>
    public int AddChannel(ChannelConfig channel)
    {
        ArgumentNullException.ThrowIfNull(channel);

        if (Graph is null)
        {
            return -1;
        }

        int index = Graph.AddChannel(channel);

        Track(channel, index);

        return index;
    }

    void OpenDevices(GraphConfig config)
    {
        for (int index = 0; index < config.Channels.Count; index++)
        {
            Track(config.Channels[index], index);
        }
    }

    void Track(ChannelConfig channel, int index) =>
        Supervisor!.Track(
            channel.DeviceId,
            new DeviceInputChannelOptions
            {
                NominalSampleRate = options.SampleRate,
                ChannelCount = Math.Max(channel.ChannelCount, 1),
                BlockFrames = options.BlockFrames,
                RingCapacityFrames = options.SampleRate / 8,
                TargetFillFrames = options.SampleRate / 40,
                CorrectionInterval = options.CorrectionInterval,
                Dropouts = Dropouts,
                EndpointIndex = index
            },
            new CaptureOptions(ShareMode.Shared, TimeSpan.FromMilliseconds(20), Math.Max(channel.ChannelCount, 1)));

    /// <summary>
    /// Starts recording now, into a folder of its own. J1.
    /// </summary>
    /// <param name="directory">
    /// Where the session folder goes, or null for the configured root. The session always gets its
    /// own dated folder underneath: a recording that could land on top of an earlier one is a
    /// recording that will, eventually, and there is no undoing that.
    /// </param>
    /// <returns>What the disk had to say. A refusal explains itself.</returns>
    public DiskVerdict StartRecording(string? directory = null)
    {
        if (Recording is not null)
        {
            return new DiskVerdict(true, 0, 0, "Already recording.");
        }

        if (Graph is null)
        {
            return new DiskVerdict(false, 0, 0, "The engine is not running.");
        }

        return BeginRecording(Graph.Config, directory ?? options.RecordingDirectory);
    }

    /// <summary>Stops recording and closes the files.</summary>
    /// <returns>Whether anything was recording.</returns>
    public bool StopRecording()
    {
        if (Recording is null)
        {
            return false;
        }

        Graph?.BindRecording(null);

        Recording.Stop();
        Recording.Dispose();
        Recording = null;

        logger.LogInformation("Recording stopped.");

        return true;
    }

    void StartRecording(GraphConfig config)
    {
        if (!options.RecordAutomatically)
        {
            return;
        }

        BeginRecording(config, options.RecordingDirectory);
    }

    DiskVerdict BeginRecording(GraphConfig config, string root)
    {
        string directory = Path.Combine(
            root,
            DateTimeOffset.Now.ToString("yyyy-MM-dd_HH-mm-ss", System.Globalization.CultureInfo.InvariantCulture));

        Recording = new RecordingSession(directory, new DiskGuard(loggers.CreateLogger<DiskGuard>()), loggers.CreateLogger<RecordingSession>());

        foreach (ChannelConfig channel in config.Channels)
        {
            Recording.AddTrack(channel.Name, new RecordingFormat
            {
                SampleRate = options.SampleRate,
                ChannelCount = Math.Max(channel.ChannelCount, 1),
                BlockFrames = options.BlockFrames
            });
        }

        // E3. The stream bus, finished, beside the raw inputs — two different records and a public
        // body wants both: one to reconstruct what was said, one to show what was broadcast. Added
        // last, so its index is the channel count, which is where the compiler looks for it.
        if (config.Buses.Count > 0)
        {
            int primary = Math.Clamp(config.PrimaryBusIndex, 0, config.Buses.Count - 1);

            Recording.AddTrack($"{config.Buses[primary].Name} (bus)", new RecordingFormat
            {
                SampleRate = options.SampleRate,
                ChannelCount = Math.Max(config.Buses[primary].ChannelCount, 1),
                BlockFrames = options.BlockFrames
            });
        }

        DiskVerdict verdict = Recording.Start(options.ExpectedSessionDuration);

        if (!verdict.CanStart)
        {
            // The session runs without a recording rather than not at all. A refused recording is a
            // problem; a meeting that did not happen because of one is a worse problem.
            logger.LogError("Recording did not start: {Reason}", verdict.Description);

            Recording.Dispose();
            Recording = null;

            return verdict;
        }

        Graph!.BindRecording(Recording);

        return verdict;
    }

    MeterPublisher? BuildMeterPublisher()
    {
        foreach (AudioNode node in Graph!.Publisher.Current.Plan.Nodes)
        {
            if (node is MeterNode meters)
            {
                return new MeterPublisher(meters.Channels, meters.Buses);
            }
        }

        return null;
    }

    /// <summary>
    /// The render callback, with a stopwatch and an allocation counter around it.
    /// </summary>
    /// <remarks>
    /// Inside the audio path. Two counter reads and a subtract; nothing here allocates, and that is
    /// the point, because this is the thing measuring whether anything else does.
    /// </remarks>
    /// <param name="inputs">One block from each device.</param>
    /// <param name="output">Where the primary output's audio goes.</param>
    /// <param name="frameCount">Frames wanted.</param>
    /// <returns>Frames written.</returns>
    int RenderAndMeasure(MixBlocks inputs, Span<float> output, int frameCount)
    {
        long started = Stopwatch.GetTimestamp();

        Allocations.Begin();

        int written = Graph!.Render(inputs, output, frameCount);

        Allocations.End();
        Callbacks.Record(Stopwatch.GetTimestamp() - started, Graph.BlockTicks);

        return written;
    }

    /// <summary>
    /// Points the denoise at RNNoise when the native library is there, and says which it picked. B4.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Asked once, at startup, and never on the audio thread. RNNoise is BSD-licensed and freely
    /// available but is not shipped with VAM, so a first-time user has the managed spectral
    /// suppressor — which works, and sounds like what it is.
    /// </para>
    /// <para>
    /// Said out loud either way. An operator wondering why the denoise sounds different from the one
    /// they read about needs to be able to find out which one is running, and a silent fallback is
    /// how that question goes unanswered for a year.
    /// </para>
    /// </remarks>
    void ChooseNoiseSuppressor()
    {
        if (!RnnoiseSuppressor.IsAvailable)
        {
            logger.LogInformation(
                "RNNoise is not installed, so the denoise is the managed spectral suppressor. "
                + "Drop rnnoise.dll beside the engine to use RNNoise instead.");

            return;
        }

        Modifiers.Register("vam.denoise", static () => new DenoiseModifier(static () => new RnnoiseSuppressor()));

        logger.LogInformation("Denoise is RNNoise, through the native library.");
    }

    /// <summary>
    /// Opens a device for every bus that names one, and hands the graph the rings. D7 and E2.
    /// </summary>
    /// <remarks>
    /// Called at startup and after every change to the buses, because a bus can be added, removed or
    /// re-aimed while the meeting runs. The primary bus is skipped: it is the master clock's, and
    /// opening it twice would be two threads playing to one endpoint.
    /// </remarks>
    /// <summary>
    /// Points a strip at a different capture endpoint and opens it.
    /// </summary>
    /// <remarks>
    /// A microphone that came back on a different endpoint, or an operator who assigned the wrong
    /// one, has to be fixable in place. Deleting the strip and building it again would take its
    /// sends, its chain and its name with it.
    /// </remarks>
    /// <param name="channelIndex">Which strip.</param>
    /// <param name="deviceId">Its new endpoint.</param>
    /// <returns>Whether there was a graph to change.</returns>
    public bool RetargetChannel(int channelIndex, AudioDeviceId deviceId)
    {
        if (Graph is not { } graph || channelIndex < 0 || channelIndex >= graph.Config.Channels.Count)
        {
            return false;
        }

        ChannelConfig channel = graph.Config.Channels[channelIndex] with { DeviceId = deviceId };

        graph.Config.Channels[channelIndex] = channel;

        Track(channel, channelIndex);
        graph.Recompile();

        return true;
    }

    /// <summary>
    /// Re-opens the bus outputs after the buses changed. D7.
    /// </summary>
    /// <remarks>
    /// Called by the control surface after anything that adds, removes or re-aims a bus. A bus whose
    /// output was changed and not re-bound would keep playing to the device it used to have, which
    /// is worse than silence: it is audio arriving somewhere nobody expects it.
    /// </remarks>
    public void RebindBusOutputs()
    {
        if (Graph is { } graph)
        {
            BindBusOutputs(graph.Config);
        }
    }

    void BindBusOutputs(GraphConfig config)
    {
        if (Graph is null || BusOutputs is null)
        {
            return;
        }

        List<BusOutputRequest> wanted = [];

        for (int bus = 0; bus < config.Buses.Count; bus++)
        {
            if (bus == config.PrimaryBusIndex || config.Buses[bus].OutputDeviceId.IsNone)
            {
                continue;
            }

            wanted.Add(new BusOutputRequest(bus, config.Buses[bus].OutputDeviceId, config.Buses[bus].ChannelCount));
        }

        BusOutputs.Reconcile(wanted, new DeviceInputChannelOptions
        {
            NominalSampleRate = options.SampleRate,
            ChannelCount = 2,
            BlockFrames = options.BlockFrames,
            RingCapacityFrames = options.SampleRate / 8,
            TargetFillFrames = options.SampleRate / 40,
            CorrectionInterval = options.CorrectionInterval,
            Dropouts = Dropouts
        });

        for (int bus = 0; bus < config.Buses.Count; bus++)
        {
            Graph.BindBusOutput(bus, BusOutputs.ChannelOf(bus));
        }

        Graph.Recompile();
    }

    void OnOverran(object? sender, ModifierOverrun overrun)
    {
        // On the control thread: the guard runs there, not in the callback that noticed.
        (int, int) key = (overrun.ChannelIndex, overrun.LinkIndex);

        overruns[key] = overruns.TryGetValue(key, out long count) ? count + 1 : 1;
    }

    void RecordDrift(TimeSpan elapsed)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        for (int index = 0; index < Channels.Count; index++)
        {
            DeviceTelemetry telemetry = Channels.Channels[index].GetTelemetry();

            Drift.Record(new DriftSample(
                index,
                now,
                telemetry.DriftPpm,
                telemetry.FillPercentage,

                // What the servo is actually applying, which is not the same as what the estimator
                // measured: in a closed loop the two converge, and the gap between them while they
                // do is the thing worth looking at on a chart.
                (telemetry.Ratio - 1.0) * 1_000_000.0));
        }
    }

    void Run()
    {
        TimeSpan interval = options.ControlInterval;
        TimeSpan meterInterval = TimeSpan.FromSeconds(1.0 / MeterPublisher.FramesPerSecond);

        while (!stopping.IsCancellationRequested)
        {
            Thread.Sleep(interval);

            Supervisor?.Poll(interval);
            Clock?.Poll();

            // I1, before the graph pumps, so a strip whose device died this tick is already silent
            // in the snapshot the next block renders from.
            Faults?.Poll();
            Graph?.Pump();
            Graph?.GuardCostBudget();

            // The other half of the arrangement: the audio threads wrote numbers, and this is where
            // they become sentences somebody can read afterwards.
            dropoutPump?.Pump();

            // Once a second, so the status bar has a number that means "now" rather than "ever".
            sinceLoad += interval;

            if (sinceLoad >= TimeSpan.FromSeconds(1))
            {
                Load = Callbacks.TakeRecentWorst();
                sinceLoad = TimeSpan.Zero;
            }

            sinceCorrection += interval;
            sinceMeters += interval;

            if (sinceCorrection >= options.CorrectionInterval)
            {
                Channels.UpdateCorrections(sinceCorrection);

                // A monitor's device has its own clock and drifts against the graph exactly as an
                // input does. Left uncorrected its ring empties, and somebody's headphones start
                // clicking an hour in.
                BusOutputs?.UpdateCorrections(sinceCorrection);
                RecordDrift(sinceCorrection);
                sinceCorrection = TimeSpan.Zero;
            }

            if (sinceMeters >= meterInterval && Meters is not null)
            {
                Meters.Publish(
                    sinceMeters,
                    AutomixState(),
                    Graph?.Config.AutomixDepthDb ?? -15.0,
                    SpeakingNow());
                sinceMeters = TimeSpan.Zero;
            }
        }
    }
}
