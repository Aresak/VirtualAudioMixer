namespace Vam.Engine.Windows.Tests.Devices;

/// <summary>
/// Stands in for the mix graph, and measures what the render thread allocated getting here.
/// </summary>
/// <remarks>
/// The same trick as <see cref="CaptureProbe"/>, and for the same reason: the fill delegate runs on
/// the device's own thread, so the only place that thread's allocation counter can be read is
/// inside the delegate.
/// </remarks>
public sealed class RenderProbe
{
    const int WarmupCallbacks = 32;
    const float ToneAmplitude = 0.05f;

    readonly double phaseIncrement;
    readonly int channelCount;
    readonly int fillFramesOf;

    long previousAllocatedBytes;
    double phase;

    /// <summary>Creates a probe producing a quiet tone.</summary>
    /// <param name="sampleRate">The stream's rate, so the tone comes out at the frequency asked for.</param>
    /// <param name="channelCount">Channels to write per frame.</param>
    /// <param name="toneHz">Frequency to generate.</param>
    /// <param name="fillFramesOf">
    /// How many of every hundred frames asked for to actually fill, so a deliberate shortfall can be
    /// provoked. A hundred means fill everything.
    /// </param>
    public RenderProbe(int sampleRate, int channelCount, double toneHz = 440.0, int fillFramesOf = 100)
    {
        this.channelCount = channelCount;
        this.fillFramesOf = fillFramesOf;

        phaseIncrement = 2.0 * Math.PI * toneHz / sampleRate;
    }

    /// <summary>Buffers the device asked for.</summary>
    public int CallbackCount { get; private set; }

    /// <summary>Frames actually written.</summary>
    public long FramesWritten { get; private set; }

    /// <summary>Bytes the render thread allocated per buffer, once past the warm-up.</summary>
    public long AllocatedBytesInSteadyState { get; private set; }

    /// <summary>Buffers the allocation figure covers.</summary>
    public int MeasuredCallbacks { get; private set; }

    /// <summary>Shaped to be handed straight to <c>IRenderStream.Start</c>.</summary>
    /// <param name="destination">Where to write interleaved samples.</param>
    /// <param name="frameCount">Frames wanted.</param>
    /// <returns>Frames written, which may deliberately be fewer.</returns>
    public int OnBufferNeeded(Span<float> destination, int frameCount)
    {
        long allocatedBytes = GC.GetAllocatedBytesForCurrentThread();

        CallbackCount++;

        int wanted = frameCount * fillFramesOf / 100;

        for (int frame = 0; frame < wanted; frame++)
        {
            float sample = (float)(Math.Sin(phase) * ToneAmplitude);
            phase += phaseIncrement;

            for (int channel = 0; channel < channelCount; channel++)
            {
                destination[(frame * channelCount) + channel] = sample;
            }
        }

        // Kept bounded so the phase stays precise across a long run rather than losing resolution.
        phase %= 2.0 * Math.PI;
        FramesWritten += wanted;

        if (CallbackCount > WarmupCallbacks && previousAllocatedBytes != 0)
        {
            AllocatedBytesInSteadyState += allocatedBytes - previousAllocatedBytes;
            MeasuredCallbacks++;
        }

        previousAllocatedBytes = allocatedBytes;
        return wanted;
    }
}
