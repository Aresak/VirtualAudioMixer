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

    /// <summary>How many polls a supposedly running clock may produce nothing for.</summary>
    const int SilentPollsBeforePromoting = 5;

    IRenderStream? primary;
    MixCallback? consumer;
    AudioDeviceId preferred = AudioDeviceId.None;
    Thread? fallbackThread;
    CancellationTokenSource? fallbackStopping;
    long blocksRendered;
    long blocksAtLastPoll;
    int silentPolls;

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

    /// <summary>
    /// The device that should be keeping time, when it can.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The primary bus plays out through the clock, so the clock's device <b>is</b> where the mix
    /// goes. Left to promotion the answer is whichever render endpoint happens to enumerate first
    /// and open — which on a laptop is as likely to be a wireless headset as the interface somebody
    /// chose, and a wireless clock makes every microphone in the room appear to drift hundreds of
    /// parts per million against it.
    /// </para>
    /// <para>
    /// Preference, not a requirement. When it cannot be opened, or it goes away mid-meeting,
    /// promotion still finds something else rather than leaving the session without a timebase — and
    /// it comes back here the moment this device can be opened again.
    /// </para>
    /// </remarks>
    public AudioDeviceId Preferred
    {
        get => preferred;
        set
        {
            if (value == preferred)
            {
                return;
            }

            preferred = value;

            // Taken now rather than at the next loss. Somebody who has just chosen where the mix
            // goes should hear it there, not after the current device fails.
            if (!value.IsNone && PrimaryDeviceId != value)
            {
                SetPrimary(value);
            }
        }
    }

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

            // Reset with the device, or the polls a dead one accumulated would condemn its
            // replacement before it had rendered anything.
            blocksAtLastPoll = Interlocked.Read(ref blocksRendered);
            silentPolls = 0;

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
            if (!HasStalled())
            {
                return;
            }

            logger.LogWarning(
                "Master clock {DeviceId} says it is {State} but has asked for nothing. Promoting another output.",
                primary.DeviceId,
                primary.State);
        }

        if (primary is not null)
        {
            if (primary.State is not (DeviceStreamState.Running or DeviceStreamState.Stopped))
            {
                logger.LogWarning("Master clock {DeviceId} is {State}. Promoting another output.", primary.DeviceId, primary.State);
            }

            AudioDeviceId failed = primary.DeviceId;

            primary.Dispose();
            primary = null;

            // Excluded from this promotion only. The preference itself is kept: forgetting it would
            // mean one hiccup silently abandoning the output somebody chose, for the rest of the
            // session. It is tried again the next time the clock needs replacing, by which point the
            // device may be back.
            Promote(failed);

            return;
        }

        Promote(AudioDeviceId.None);
    }

    /// <summary>
    /// Whether the clock has opened a device that is not asking for anything.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The worst failure this design has, and until now nothing looked for it. An endpoint can open,
    /// report itself Running and never fire a render callback — an HDMI display with nothing
    /// listening does exactly this. Every input ring then fills and overruns, the meters sit still,
    /// and no single component is in a position to say what is wrong.
    /// </para>
    /// <para>
    /// Several polls rather than one, so a device that is merely slow to start is not thrown away
    /// before it has begun.
    /// </para>
    /// </remarks>
    bool HasStalled()
    {
        long rendered = Interlocked.Read(ref blocksRendered);

        silentPolls = rendered == blocksAtLastPoll ? silentPolls + 1 : 0;
        blocksAtLastPoll = rendered;

        return silentPolls >= SilentPollsBeforePromoting;
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
    void Promote(AudioDeviceId avoid)
    {
        // The chosen one first, every time - including after a loss, so a device that comes back
        // takes the clock back rather than leaving the session on whatever stood in for it.
        if (!preferred.IsNone && preferred != avoid && SetPrimary(preferred))
        {
            return;
        }

        foreach (AudioDeviceInfo candidate in backend.Enumerate(DeviceDirection.Render))
        {
            if (candidate.Id != preferred && candidate.Id != avoid && SetPrimary(candidate.Id))
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
    /// <remarks>
    /// <para>
    /// A device may ask for more than one block. WASAPI's priming call before the stream starts
    /// hands over the whole endpoint buffer, which in shared mode is several blocks, and the graph
    /// is built for exactly one: its arena has one plane per stride and reading past it throws on
    /// the audio thread, inside the device's own Start.
    /// </para>
    /// <para>
    /// Rendered in block-sized pieces rather than clamped. Clamping would prime the device with a
    /// buffer that is mostly silence and then play it, which is a gap at the start of every session
    /// on every device whose period is larger than a block - meaning most of them.
    /// </para>
    /// </remarks>
    int Fill(Span<float> output, int frameCount)
    {
        if (frameCount <= options.BlockFrames)
        {
            return FillBlock(output, frameCount);
        }

        // Exact: the span is interleaved frames of one device's channel count.
        int channels = output.Length / frameCount;
        int done = 0;

        while (done < frameCount)
        {
            int chunk = Math.Min(options.BlockFrames, frameCount - done);
            int produced = FillBlock(output.Slice(done * channels, chunk * channels), chunk);

            done += produced;

            if (produced < chunk)
            {
                break;
            }
        }

        return done;
    }

    int FillBlock(Span<float> output, int frameCount)
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
