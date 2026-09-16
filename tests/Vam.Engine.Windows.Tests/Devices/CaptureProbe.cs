namespace Vam.Engine.Windows.Tests.Devices;

/// <summary>
/// Stands in for the ring buffer a real strip would write into, and measures what the device thread
/// allocated while getting here.
/// </summary>
/// <remarks>
/// <para>
/// The allocation figure is the interesting part and it cannot be taken with
/// <c>AllocationAssert</c>, which reads a counter for the thread it is called on. The capture
/// callback runs on the device's own thread, so the only place that thread's counter can be read is
/// inside the callback itself.
/// </para>
/// <para>
/// Reading it at the top of every callback and differencing gives exactly the right region: the
/// tail of the previous callback, the buffer release, the next <c>GetBuffer</c>, the format
/// conversion, and the dispatch back into here. That is the whole of what VAM does per packet.
/// </para>
/// </remarks>
public sealed class CaptureProbe
{
    /// <summary>
    /// Callbacks ignored before measuring starts. The first packets carry first-call JIT of the
    /// whole capture path, which allocates once and would otherwise be reported as if it repeated.
    /// </summary>
    const int WarmupCallbacks = 64;

    long previousAllocatedBytes;

    /// <summary>Frames delivered so far.</summary>
    public long FramesCaptured { get; private set; }

    /// <summary>Callbacks so far, warm-up included.</summary>
    public int CallbackCount { get; private set; }

    /// <summary>Channels seen in the last packet, derived from what actually arrived.</summary>
    public int ChannelCount { get; private set; }

    /// <summary>Largest absolute sample seen. Zero means the device delivered nothing but silence.</summary>
    public float PeakLevel { get; private set; }

    /// <summary>Bytes the device thread allocated per packet, once past the warm-up.</summary>
    public long AllocatedBytesInSteadyState { get; private set; }

    /// <summary>Callbacks the allocation figure covers.</summary>
    public int MeasuredCallbacks { get; private set; }

    /// <summary>Shaped to be handed straight to <c>ICaptureStream.Start</c>.</summary>
    /// <param name="samples">Interleaved samples for this packet.</param>
    /// <param name="frameCount">Frames in the packet.</param>
    public void OnSamplesCaptured(ReadOnlySpan<float> samples, int frameCount)
    {
        long allocatedBytes = GC.GetAllocatedBytesForCurrentThread();

        CallbackCount++;
        FramesCaptured += frameCount;

        if (frameCount > 0)
        {
            ChannelCount = samples.Length / frameCount;
        }

        foreach (float sample in samples)
        {
            float magnitude = Math.Abs(sample);

            if (magnitude > PeakLevel)
            {
                PeakLevel = magnitude;
            }
        }

        if (CallbackCount > WarmupCallbacks && previousAllocatedBytes != 0)
        {
            AllocatedBytesInSteadyState += allocatedBytes - previousAllocatedBytes;
            MeasuredCallbacks++;
        }

        previousAllocatedBytes = allocatedBytes;
    }
}
