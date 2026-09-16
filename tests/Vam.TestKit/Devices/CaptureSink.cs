using Vam.Engine.Devices.Abstractions;

namespace Vam.TestKit.Devices;

/// <summary>
/// Catches captured audio into a pre-allocated buffer so a test can look at it.
/// </summary>
/// <remarks>
/// Stands in for the ring buffer the real engine writes into, and obeys the same rule: the
/// callback copies and returns, and allocates nothing. Give <see cref="OnSamplesCaptured"/> to
/// <see cref="ICaptureStream.Start"/> once - a delegate created per callback would allocate inside
/// the audio path.
/// </remarks>
public sealed class CaptureSink
{
    readonly float[] buffer;

    /// <summary>Creates a sink able to hold <paramref name="capacitySamples"/> samples.</summary>
    /// <param name="capacitySamples">Total samples, which is frames times channels.</param>
    public CaptureSink(int capacitySamples)
    {
        buffer = new float[capacitySamples];
    }

    /// <summary>Frames delivered to this sink since it was created.</summary>
    public long TotalFrames { get; private set; }

    /// <summary>Times the callback ran.</summary>
    public int CallbackCount { get; private set; }

    /// <summary>Frames in the most recent buffer.</summary>
    public int LastFrameCount { get; private set; }

    /// <summary>Samples that would not fit, which should always be zero in a correct test.</summary>
    public long DroppedSamples { get; private set; }

    /// <summary>Everything captured so far, in order.</summary>
    public ReadOnlySpan<float> Written => buffer.AsSpan(0, WrittenSamples);

    /// <summary>Samples written so far.</summary>
    public int WrittenSamples { get; private set; }

    /// <summary>
    /// Give this to <see cref="ICaptureStream.Start"/>. Runs inside the audio path: copy and return.
    /// </summary>
    /// <param name="samples">The captured buffer, valid only for this call.</param>
    /// <param name="frameCount">Frames in the buffer.</param>
    public void OnSamplesCaptured(ReadOnlySpan<float> samples, int frameCount)
    {
        int room = buffer.Length - WrittenSamples;

        if (samples.Length > room)
        {
            samples[..room].CopyTo(buffer.AsSpan(WrittenSamples));
            WrittenSamples += room;
            DroppedSamples += samples.Length - room;
        }
        else
        {
            samples.CopyTo(buffer.AsSpan(WrittenSamples));
            WrittenSamples += samples.Length;
        }

        TotalFrames += frameCount;
        LastFrameCount = frameCount;
        CallbackCount++;
    }

    /// <summary>Forgets everything captured, keeping the buffer.</summary>
    public void Reset()
    {
        WrittenSamples = 0;
        TotalFrames = 0;
        CallbackCount = 0;
        LastFrameCount = 0;
        DroppedSamples = 0;
    }
}
