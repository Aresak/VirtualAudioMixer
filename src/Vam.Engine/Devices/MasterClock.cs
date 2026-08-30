using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Vam.Engine.Devices.Abstractions;

namespace Vam.Engine.Devices;

/// <summary>
/// Decides when a block's worth of time has passed, and pulls every input in step with it.
/// </summary>
/// <remarks>
/// <para>
/// The primary output's render callback is the clock. Everything else follows it: each input's
/// servo holds its ring against this rate, so the whole engine advances on whatever the one device
/// designated primary actually does, rather than on a timer that agrees with nothing.
/// </para>
/// <para>
/// <b>The timer fallback matters more than it sounds.</b> A council session where somebody unplugs
/// the monitor headphones must not stop recording, and recording is the thing that makes a bad
/// session recoverable. With no render device at all the clock keeps running on its own timer: the
/// audio is going nowhere, but it is still being pulled, still being mixed, and still being written
/// to disk.
/// </para>
/// <para>
/// Promotion and every change of primary happen on the control thread and take effect at a block
/// boundary, because the alternative is swapping the thing that defines "now" halfway through a
/// block.
/// </para>
/// </remarks>
public sealed class MasterClock : IDisposable
{
    readonly IAudioBackend backend;
    readonly DeviceInputChannelRegistry channels;
    readonly ILogger<MasterClock> logger;
    readonly MasterClockOptions options;
    readonly float[] arena;
    readonly BlockSlice[] slices;
    readonly float[] fallbackOutput;
    readonly TimeSpan blockDuration;

    IRenderStream? primary;
    MixCallback? consumer;
    Thread? fallbackThread;
    CancellationTokenSource? fallbackStopping;
    long blocksRendered;

    /// <summary>Builds a clock and everything it will ever allocate.</summary>
    /// <param name="backend">Where render devices are opened.</param>
    /// <param name="channels">The inputs to pull, in registry order.</param>
    /// <param name="options">Sizes and the nominal rate.</param>
    public MasterClock(IAudioBackend backend, DeviceInputChannelRegistry channels, MasterClockOptions options)
        : this(backend, channels, options, Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance)
    {
    }

    /// <summary>Builds a clock that reports promotions and losses.</summary>
    /// <param name="backend">Where render devices are opened.</param>
    /// <param name="channels">The inputs to pull, in registry order.</param>
    /// <param name="options">Sizes and the nominal rate.</param>
    /// <param name="loggerFactory">Where promotions and the fallback are reported.</param>
    public MasterClock(
        IAudioBackend backend,
        DeviceInputChannelRegistry channels,
        MasterClockOptions options,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.BlockFrames, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.SampleRate, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxDevices, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxChannelsPerDevice, 1);

        this.backend = backend;
        this.channels = channels;
        this.options = options;

        logger = loggerFactory.CreateLogger<MasterClock>();

        arena = new float[options.MaxDevices * options.MaxChannelsPerDevice * options.BlockFrames];
        slices = new BlockSlice[options.MaxDevices];
        fallbackOutput = new float[options.MaxChannelsPerDevice * options.BlockFrames];
        blockDuration = TimeSpan.FromSeconds((double)options.BlockFrames / options.SampleRate);
    }

    /// <summary>Raised when the primary changes, is lost, or the fallback takes over. Control thread.</summary>
    public event EventHandler<AudioDeviceId>? PrimaryChanged;

    /// <summary>The device currently keeping time, or none when the fallback is running.</summary>
    public AudioDeviceId PrimaryDeviceId => primary?.DeviceId ?? AudioDeviceId.None;

    /// <summary>Whether the engine is running on its own timer because no output remains.</summary>
    public bool IsOnFallbackTimer => fallbackThread is not null;

    /// <summary>Blocks pulled since the clock started.</summary>
    public long BlocksRendered => Interlocked.Read(ref blocksRendered);

    /// <summary>
    /// Sets what receives each set of blocks.
    /// </summary>
    /// <remarks>
    /// Stored once. With none set the clock still runs and plays silence, which is what EPIC-03
    /// replaces rather than a placeholder to be removed.
    /// </remarks>
    /// <param name="consumer">The graph, or null for silence.</param>
    public void SetConsumer(MixCallback? consumer) => this.consumer = consumer;

    /// <summary>
    /// Makes a render device the timebase, replacing whatever was keeping time before.
    /// </summary>
    /// <param name="deviceId">Which device.</param>
    /// <returns>Whether it opened.</returns>
    public bool SetPrimary(AudioDeviceId deviceId)
    {
        try
        {
            IRenderStream stream = backend.OpenRender(
                deviceId,
                new RenderOptions(ShareMode.Shared, blockDuration));

            StopFallback();

            IRenderStream? previous = primary;

            primary = stream;
            stream.Start(Fill);

            // Stopped after the new one is running, so there is no moment with no clock at all.
            previous?.Dispose();

            logger.LogInformation("Master clock is now {DeviceId}.", deviceId);
            PrimaryChanged?.Invoke(this, deviceId);

            return true;
        }
        catch (Exception error)
        {
            logger.LogWarning(error, "Could not make {DeviceId} the master clock.", deviceId);
            return false;
        }
    }

    /// <summary>
    /// Checks the primary is still alive and promotes another output if it is not.
    /// </summary>
    /// <remarks>
    /// Control thread, on the same loop as the device supervisor. Promotion is never decided on the
    /// audio thread — the thread whose deadline is at stake is the wrong one to be enumerating
    /// devices on.
    /// </remarks>
    public void Poll()
    {
        if (primary is not null && primary.State is DeviceStreamState.Running or DeviceStreamState.Stopped)
        {
            return;
        }

        if (primary is not null)
        {
            logger.LogWarning("Master clock {DeviceId} is {State}. Promoting another output.", primary.DeviceId, primary.State);

            primary.Dispose();
            primary = null;
        }

        Promote();
    }

    /// <summary>Stops the clock, whichever way it is running.</summary>
    public void Stop()
    {
        StopFallback();

        primary?.Dispose();
        primary = null;
    }

    /// <inheritdoc />
    public void Dispose() => Stop();

    /// <summary>
    /// Finds another render device to keep time with, or starts the timer if there is none.
    /// </summary>
    void Promote()
    {
        foreach (AudioDeviceInfo candidate in backend.Enumerate(DeviceDirection.Render))
        {
            if (SetPrimary(candidate.Id))
            {
                return;
            }
        }

        StartFallback();
    }

    void StartFallback()
    {
        if (fallbackThread is not null)
        {
            return;
        }

        logger.LogWarning(
            "No render device remains. The engine is running on its own timer so the recording continues.");

        fallbackStopping = new CancellationTokenSource();

        fallbackThread = new Thread(RunFallback)
        {
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal,
            Name = "master-clock-fallback"
        };

        fallbackThread.Start();
        PrimaryChanged?.Invoke(this, AudioDeviceId.None);
    }

    void StopFallback()
    {
        if (fallbackThread is null)
        {
            return;
        }

        fallbackStopping?.Cancel();
        fallbackThread.Join(TimeSpan.FromSeconds(2));
        fallbackThread = null;

        fallbackStopping?.Dispose();
        fallbackStopping = null;
    }

    void RunFallback()
    {
        CancellationToken stopping = fallbackStopping?.Token ?? CancellationToken.None;
        Stopwatch elapsed = Stopwatch.StartNew();
        long blocks = 0;

        while (!stopping.IsCancellationRequested)
        {
            // Paced against elapsed time rather than by sleeping a fixed interval, so the drift of
            // the sleep itself does not accumulate into the engine's idea of how much time has passed.
            TimeSpan due = blockDuration * blocks;
            TimeSpan wait = due - elapsed.Elapsed;

            if (wait > TimeSpan.Zero)
            {
                Thread.Sleep(wait);
            }

            Fill(fallbackOutput.AsSpan(0, options.BlockFrames), options.BlockFrames);
            blocks++;
        }
    }

    /// <summary>
    /// The render callback, and the whole point of the class. Inside the audio path.
    /// </summary>
    int Fill(Span<float> output, int frameCount)
    {
        int deviceCount = 0;
        int offset = 0;

        // Pulled in registry order, and every device every block. A device that is absent produces
        // silence from an empty ring rather than being skipped, so the set handed to the graph has
        // the same shape whatever is plugged in.
        for (int index = 0; index < channels.Count && deviceCount < slices.Length; index++)
        {
            DeviceInputChannel channel = channels.Channels[index];
            int width = frameCount * channel.ChannelCount;

            if (offset + width > arena.Length)
            {
                break;
            }

            channel.Pull(arena.AsSpan(offset, width));
            slices[deviceCount] = new BlockSlice(offset, channel.ChannelCount);

            offset += width;
            deviceCount++;
        }

        Interlocked.Increment(ref blocksRendered);

        if (consumer is null)
        {
            output.Clear();
            return frameCount;
        }

        MixBlocks blocks = new(arena.AsSpan(0, offset), slices.AsSpan(0, deviceCount), frameCount);

        return consumer(blocks, output, frameCount);
    }
}
