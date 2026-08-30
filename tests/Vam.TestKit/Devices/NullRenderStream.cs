using Vam.Engine.Devices.Abstractions;

namespace Vam.TestKit.Devices;

/// <summary>
/// A render device that pulls buffers on demand and keeps what it was given, so a test can look at
/// what would have been played.
/// </summary>
/// <remarks>
/// Pumped explicitly rather than by a clock, for the same reason as
/// <see cref="NullCaptureStream"/>: a timing-dependent test is a flaky test.
/// </remarks>
public sealed class NullRenderStream : IRenderStream
{
    /// <summary>How many buffers' worth this fake will hand over in one callback.</summary>
    const int MaxBurstBuffers = 8;

    readonly float[] buffer;
    readonly double effectiveSampleRate;

    RenderCallback? onBufferNeeded;
    double pendingFrames;

    internal NullRenderStream(AudioDeviceId deviceId, NullDeviceOptions options, AudioStreamFormat format)
    {
        DeviceId = deviceId;
        Format = format;

        effectiveSampleRate = format.SampleRate * (1.0 + (options.DriftPpm / 1_000_000.0));
        // Room for a burst. A real endpoint can ask for its whole buffer in one callback, which is
        // several blocks; a fake sized for exactly one could never be asked to prove what happens.
        buffer = new float[format.BufferFrames * format.ChannelCount * MaxBurstBuffers];
    }

    /// <inheritdoc />
    public AudioDeviceId DeviceId { get; }

    /// <inheritdoc />
    public DeviceDirection Direction => DeviceDirection.Render;

    /// <inheritdoc />
    public AudioStreamFormat Format { get; }

    /// <inheritdoc />
    public DeviceStreamState State { get; private set; } = DeviceStreamState.Stopped;

    /// <inheritdoc />
    public long UnderrunCount { get; private set; }

    /// <summary>
    /// The rate this device really runs at, once <see cref="NullDeviceOptions.DriftPpm"/> is applied.
    /// </summary>
    public double EffectiveSampleRate => effectiveSampleRate;

    /// <summary>Frames pulled since the stream started, including any played as silence.</summary>
    public long FramesRendered { get; private set; }

    /// <summary>
    /// The most recent buffer, as the device would have played it - including any silence
    /// substituted for frames the callback did not supply.
    /// </summary>
    public ReadOnlySpan<float> LastBuffer => buffer.AsSpan(0, LastFrameCount * Format.ChannelCount);

    /// <summary>Frames in <see cref="LastBuffer"/>.</summary>
    public int LastFrameCount { get; private set; }

    /// <inheritdoc />
    public void Start(RenderCallback onBufferNeeded)
    {
        ArgumentNullException.ThrowIfNull(onBufferNeeded);

        this.onBufferNeeded = onBufferNeeded;
        State = DeviceStreamState.Running;
    }

    /// <inheritdoc />
    public void Stop() => State = DeviceStreamState.Stopped;

    /// <summary>Pulls exactly one buffer, ignoring drift. What most tests want.</summary>
    public void PumpBuffer() => Pull(Format.BufferFrames);

    /// <summary>
    /// Pulls an arbitrary number of frames in one callback, the way a real endpoint does.
    /// </summary>
    /// <remarks>
    /// WASAPI in shared mode asks for whatever space is free, which on the priming call before a
    /// stream starts is the whole endpoint buffer - several blocks, not one. Without a way to say
    /// that here, every test agrees with every other test that a callback is exactly one block, and
    /// the one place that assumption is wrong is the one place it is never exercised.
    /// </remarks>
    /// <param name="frameCount">How many frames the device is asking for.</param>
    public void PumpFrames(int frameCount)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(frameCount, Format.BufferFrames * MaxBurstBuffers);

        Pull(frameCount);
    }

    /// <summary>
    /// Pulls whatever the device would have consumed in <paramref name="elapsed"/>, at its real
    /// rate rather than its nominal one. The fractional remainder carries across calls.
    /// </summary>
    /// <param name="elapsed">How much time passed.</param>
    /// <returns>Frames pulled.</returns>
    public int Pump(TimeSpan elapsed)
    {
        pendingFrames += elapsed.TotalSeconds * effectiveSampleRate;

        int whole = (int)pendingFrames;

        if (whole <= 0)
        {
            return 0;
        }

        pendingFrames -= whole;

        int pulled = 0;

        while (pulled < whole)
        {
            int chunk = Math.Min(Format.BufferFrames, whole - pulled);
            Pull(chunk);
            pulled += chunk;
        }

        return pulled;
    }

    /// <summary>Marks the device as gone, the way an unplugged device would be.</summary>
    public void SimulateRemoval() => State = DeviceStreamState.Absent;

    /// <inheritdoc />
    public void Dispose() => Stop();

    void Pull(int frameCount)
    {
        if (State != DeviceStreamState.Running || onBufferNeeded is null)
        {
            return;
        }

        Span<float> block = buffer.AsSpan(0, frameCount * Format.ChannelCount);
        int supplied = onBufferNeeded(block, frameCount);

        if (supplied < frameCount)
        {
            // Short fill is not an error. Silence for the remainder and count it - blocking to
            // wait for the rest would turn a click into a stall.
            block[(supplied * Format.ChannelCount)..].Clear();
            UnderrunCount++;
        }

        LastFrameCount = frameCount;
        FramesRendered += frameCount;
    }
}
