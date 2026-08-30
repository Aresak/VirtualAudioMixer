using Vam.Engine.Devices;

namespace Vam.Engine.Recording;

/// <summary>
/// One track being recorded: a ring the audio thread writes into and a file a writer thread drains
/// it into.
/// </summary>
/// <remarks>
/// <para>
/// <b>The audio thread's entire contribution is one ring write.</b> If the ring is full it drops the
/// block and counts it, and that is the whole failure path — because a full disk must not be able to
/// take a live broadcast down. The writer thread on the other side may block on the disk for as long
/// as the disk takes, and that split is the reason this epic can promise what it promises.
/// </para>
/// <para>
/// The ring holds two seconds, which is the budget for a disk hiccup. Longer would only delay the
/// moment a genuinely failing disk becomes visible.
/// </para>
/// </remarks>
public sealed class RecordingTrack : IDisposable
{
    /// <summary>How much the ring holds. The budget for a disk that pauses.</summary>
    public const double RingSeconds = 2.0;

    readonly AudioRingBuffer ring;
    readonly WaveWriter writer;
    readonly float[] drain;

    long droppedFrames;

    /// <summary>Opens a track and its file.</summary>
    /// <param name="name">What this track is, for the console and for the file name.</param>
    /// <param name="path">Where to write.</param>
    /// <param name="format">Rate, channels and block size.</param>
    public RecordingTrack(string name, string path, RecordingFormat format)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(format);

        Name = name;

        ring = new AudioRingBuffer((int)(format.SampleRate * RingSeconds), format.ChannelCount);
        writer = new WaveWriter(path, format.SampleRate, format.ChannelCount, format.BlockFrames);
        drain = new float[format.BlockFrames * format.ChannelCount];
    }

    /// <summary>What this track is.</summary>
    public string Name { get; }

    /// <summary>Where its file is.</summary>
    public string Path => writer.Path;

    /// <summary>Frames written to disk so far.</summary>
    public long FramesWritten => writer.FrameCount;

    /// <summary>
    /// Frames the audio thread could not hand over because the ring was full. Monotonic.
    /// </summary>
    /// <remarks>
    /// Counted rather than tolerated. A recording with a gap in it is still a recording, but one
    /// nobody was told about is a recording somebody will trust and should not.
    /// </remarks>
    public long DroppedFrames => Interlocked.Read(ref droppedFrames);

    /// <summary>Whether the file has outgrown a plain RIFF header.</summary>
    public bool NeedsRf64 => writer.NeedsRf64;

    /// <summary>
    /// Hands one block to the recorder. Audio thread, inside the audio path.
    /// </summary>
    /// <param name="samples">Interleaved audio.</param>
    /// <param name="frameCount">Frames in it.</param>
    /// <returns>Whether it fitted.</returns>
    public bool Capture(ReadOnlySpan<float> samples, int frameCount)
    {
        if (ring.TryWrite(samples))
        {
            return true;
        }

        Interlocked.Add(ref droppedFrames, frameCount);

        return false;
    }

    /// <summary>
    /// Moves whatever is waiting into the file. Writer thread; may block on the disk.
    /// </summary>
    /// <returns>Frames written this time.</returns>
    public int Drain()
    {
        int total = 0;

        while (true)
        {
            int frames = ring.Read(drain);

            if (frames == 0)
            {
                return total;
            }

            writer.Write(drain.AsSpan(0, frames * ring.ChannelCount));
            total += frames;
        }
    }

    /// <summary>Drains what is left and closes the file with its sizes patched.</summary>
    public void Finish()
    {
        Drain();
        writer.Finish();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Finish();
        writer.Dispose();
    }
}
