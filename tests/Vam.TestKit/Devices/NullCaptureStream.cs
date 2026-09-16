using Vam.Engine.Devices.Abstractions;

namespace Vam.TestKit.Devices;

/// <summary>
/// A capture device that produces a known signal at a deliberately inexact rate, pumped by the
/// test rather than by a clock.
/// </summary>
/// <remarks>
/// <para>
/// Driven explicitly instead of from a thread, so a test that depends on timing does not exist. A
/// test that fails at random is worse than no test - it gets disabled within a week and then
/// nothing is protecting anything.
/// </para>
/// <para>
/// Everything the pump touches is allocated when the stream opens, because the callback it invokes
/// is inside the audio path and the assertion around it has to be able to pass.
/// </para>
/// </remarks>
public sealed class NullCaptureStream : ICaptureStream
{
    readonly float[] buffer;
    readonly NullDeviceOptions options;
    readonly double effectiveSampleRate;

    CaptureCallback? onSamplesCaptured;
    double pendingFrames;
    double tonePhase;
    long rampPosition;

    internal NullCaptureStream(AudioDeviceId deviceId, NullDeviceOptions options, AudioStreamFormat format)
    {
        DeviceId = deviceId;
        Format = format;
        this.options = options;

        effectiveSampleRate = format.SampleRate * (1.0 + (options.DriftPpm / 1_000_000.0));
        buffer = new float[format.BufferFrames * format.ChannelCount];
    }

    /// <inheritdoc />
    public AudioDeviceId DeviceId { get; }

    /// <inheritdoc />
    public DeviceDirection Direction => DeviceDirection.Capture;

    /// <inheritdoc />
    public AudioStreamFormat Format { get; }

    /// <inheritdoc />
    public DeviceStreamState State { get; private set; } = DeviceStreamState.Stopped;

    /// <summary>
    /// The rate this device really runs at, once <see cref="NullDeviceOptions.DriftPpm"/> is
    /// applied. What a drift estimator is supposed to discover.
    /// </summary>
    public double EffectiveSampleRate => effectiveSampleRate;

    /// <summary>Frames delivered since the stream started.</summary>
    public long FramesCaptured { get; private set; }

    /// <inheritdoc />
    public void Start(CaptureCallback onSamplesCaptured)
    {
        ArgumentNullException.ThrowIfNull(onSamplesCaptured);

        this.onSamplesCaptured = onSamplesCaptured;
        State = DeviceStreamState.Running;
    }

    /// <inheritdoc />
    public void Stop() => State = DeviceStreamState.Stopped;

    /// <summary>
    /// Delivers exactly one buffer, ignoring drift. What most tests want.
    /// </summary>
    public void PumpBuffer() => Deliver(Format.BufferFrames);

    /// <summary>
    /// Delivers whatever the device would have produced in <paramref name="elapsed"/>, at its real
    /// rate rather than its nominal one.
    /// </summary>
    /// <remarks>
    /// The fractional remainder carries across calls, so a device 50 ppm fast really does deliver
    /// more frames than nominal over a long run rather than rounding the difference away every
    /// time. Rounding it away would hide the exact problem this class exists to reproduce.
    /// </remarks>
    /// <param name="elapsed">How much time passed.</param>
    /// <returns>Frames delivered.</returns>
    public int Pump(TimeSpan elapsed)
    {
        pendingFrames += elapsed.TotalSeconds * effectiveSampleRate;

        int whole = (int)pendingFrames;

        if (whole <= 0)
        {
            return 0;
        }

        pendingFrames -= whole;

        int delivered = 0;

        while (delivered < whole)
        {
            int chunk = Math.Min(Format.BufferFrames, whole - delivered);
            Deliver(chunk);
            delivered += chunk;
        }

        return delivered;
    }

    /// <summary>Marks the device as gone, the way an unplugged device would be.</summary>
    public void SimulateRemoval() => State = DeviceStreamState.Absent;

    /// <summary>Marks the device as failed.</summary>
    public void SimulateFault() => State = DeviceStreamState.Faulted;

    /// <inheritdoc />
    public void Dispose() => Stop();

    void Deliver(int frameCount)
    {
        if (State != DeviceStreamState.Running || onSamplesCaptured is null)
        {
            return;
        }

        Span<float> block = buffer.AsSpan(0, frameCount * Format.ChannelCount);
        Fill(block, frameCount);

        FramesCaptured += frameCount;
        onSamplesCaptured(block, frameCount);
    }

    void Fill(Span<float> block, int frameCount)
    {
        switch (options.Signal)
        {
            case NullSignal.Silence:
                block.Clear();
                break;

            case NullSignal.Tone:
                FillTone(block, frameCount);
                break;

            case NullSignal.Ramp:
                FillRamp(block, frameCount);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(block), options.Signal, "Unknown null signal.");
        }
    }

    void FillTone(Span<float> block, int frameCount)
    {
        double increment = 2.0 * Math.PI * options.ToneFrequencyHz / effectiveSampleRate;
        int channels = Format.ChannelCount;

        for (int frame = 0; frame < frameCount; frame++)
        {
            float sample = (float)Math.Sin(tonePhase);
            tonePhase += increment;

            for (int channel = 0; channel < channels; channel++)
            {
                block[(frame * channels) + channel] = sample;
            }
        }

        // Kept bounded so phase stays precise across a long soak rather than losing resolution.
        tonePhase %= 2.0 * Math.PI;
    }

    void FillRamp(Span<float> block, int frameCount)
    {
        int channels = Format.ChannelCount;

        for (int frame = 0; frame < frameCount; frame++)
        {
            float sample = rampPosition++;

            for (int channel = 0; channel < channels; channel++)
            {
                block[(frame * channels) + channel] = sample;
            }
        }
    }
}
