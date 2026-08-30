using Microsoft.Extensions.Logging;
using Vam.Engine.Automix;
using Vam.Engine.Configuration;
using Vam.Engine.Devices;
using Vam.Engine.Devices.Abstractions;
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

    IAudioBackend? backend;
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
    }

    /// <summary>The live input channels, for telemetry.</summary>
    public DeviceInputChannelRegistry Channels { get; }

    /// <summary>What modifiers exist.</summary>
    public ModifierRegistry Modifiers { get; }

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

        Clock.SetConsumer(Graph.Render);
        Clock.Poll();

        Meters = BuildMeterPublisher();

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

    void OpenDevices(GraphConfig config)
    {
        foreach (ChannelConfig channel in config.Channels)
        {
            Supervisor!.Track(
                channel.DeviceId,
                new DeviceInputChannelOptions
                {
                    NominalSampleRate = options.SampleRate,
                    ChannelCount = Math.Max(channel.ChannelCount, 1),
                    BlockFrames = options.BlockFrames,
                    RingCapacityFrames = options.SampleRate / 8,
                    TargetFillFrames = options.SampleRate / 40,
                    CorrectionInterval = options.CorrectionInterval
                },
                new CaptureOptions(ShareMode.Shared, TimeSpan.FromMilliseconds(20), Math.Max(channel.ChannelCount, 1)));
        }
    }

    void StartRecording(GraphConfig config)
    {
        if (!options.RecordAutomatically)
        {
            return;
        }

        string directory = Path.Combine(
            options.RecordingDirectory,
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

            return;
        }

        Graph!.BindRecording(Recording);
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

            sinceCorrection += interval;
            sinceMeters += interval;

            if (sinceCorrection >= options.CorrectionInterval)
            {
                Channels.UpdateCorrections(sinceCorrection);
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
