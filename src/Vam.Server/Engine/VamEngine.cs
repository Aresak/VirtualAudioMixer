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
using Vam.Engine.Windows.Devices.Wasapi;

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
            Graph?.Pump();
            Graph?.GuardCostBudget();

            // The other half of the arrangement: the audio threads wrote numbers, and this is where
            // they become sentences somebody can read afterwards.
            dropoutPump?.Pump();

            sinceCorrection += interval;
            sinceMeters += interval;

            if (sinceCorrection >= options.CorrectionInterval)
            {
                Channels.UpdateCorrections(sinceCorrection);
                RecordDrift(sinceCorrection);
                sinceCorrection = TimeSpan.Zero;
            }

            if (sinceMeters >= meterInterval && Meters is not null)
            {
                Meters.Publish(sinceMeters, AutomixState(), Graph?.Config.AutomixDepthDb ?? -15.0);
                sinceMeters = TimeSpan.Zero;
            }
        }
    }
}
