using System.Security.Cryptography;
using Vam.Engine.Recording.Extensions;
using Vam.Engine.Recording.Flac;

namespace Vam.Engine.Recording;

/// <summary>Writes one track as a twenty-four bit FLAC file.</summary>
/// <remarks>
/// <para>
/// Lossless, like the WAV beside it: decoded, it gives back the integers the WAV would have held.
/// The encoder is written here rather than taken from a library; the pull request that added it says
/// why.
/// </para>
/// <para>
/// The STREAMINFO block is written with what is not yet known left at zero, and patched on the way
/// out with the length and the MD5 of the audio, the same way <see cref="WaveWriter"/> patches its
/// sizes. A file that was never finished still decodes; it only lacks its length and checksum.
/// </para>
/// </remarks>
public sealed class FlacWriter : ITrackWriter
{
    const int MetadataOffset = 4;
    const int StreamInfoBytes = 34;
    const int BytesPerSample = FlacFrameEncoder.BitsPerSample / 8;

    readonly FileStream stream;
    readonly FlacFrameEncoder encoder;
    readonly FlacBitWriter streamInfo = new(MetadataOffset + StreamInfoBytes);
    readonly IncrementalHash checksum = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
    readonly int[][] pending;
    readonly byte[] pcm;
    readonly int channelCount;

    int pendingFrames;
    long frameNumber;
    long frameCount;
    long bytesWritten;
    int minFrameBytes;
    int maxFrameBytes;
    bool isFinished;

    /// <summary>Creates the file and writes its header.</summary>
    /// <param name="path">Where to write.</param>
    /// <param name="sampleRate">The rate to declare.</param>
    /// <param name="channelCount">Channels to declare, one to eight.</param>
    /// <param name="maxFramesPerWrite">Largest block that will be handed over at once.</param>
    public FlacWriter(string path, int sampleRate, int channelCount, int maxFramesPerWrite)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfLessThan(sampleRate, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFramesPerWrite, 1);

        encoder = new FlacFrameEncoder(sampleRate, channelCount);

        Path = path;
        SampleRate = sampleRate;

        this.channelCount = channelCount;

        pending = new int[channelCount][];

        for (int channel = 0; channel < channelCount; channel++)
        {
            pending[channel] = new int[FlacFrameEncoder.BlockFrames];
        }

        pcm = new byte[maxFramesPerWrite * channelCount * BytesPerSample];
        stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.Read, 1 << 16);

        stream.Write("fLaC"u8);
        WriteStreamInfo(stackalloc byte[16]);

        bytesWritten = stream.Position;
    }

    /// <inheritdoc />
    public string Path { get; }

    /// <summary>The rate declared in the header.</summary>
    public int SampleRate { get; }

    /// <inheritdoc />
    public long FrameCount => frameCount;

    /// <inheritdoc />
    /// <remarks>Counted rather than read off the stream, which the console asks from another thread.</remarks>
    public long BytesWritten => bytesWritten;

    /// <inheritdoc />
    public void Write(ReadOnlySpan<float> samples)
    {
        ObjectDisposedException.ThrowIf(isFinished, this);

        int frames = samples.Length / channelCount;
        int consumed = 0;

        while (consumed < frames)
        {
            int take = Math.Min(frames - consumed, FlacFrameEncoder.BlockFrames - pendingFrames);

            Take(samples.Slice(consumed * channelCount, take * channelCount));
            consumed += take;

            if (pendingFrames == FlacFrameEncoder.BlockFrames)
            {
                EncodePending();
            }
        }
    }

    /// <inheritdoc />
    public void Finish()
    {
        if (isFinished)
        {
            return;
        }

        isFinished = true;

        if (pendingFrames > 0)
        {
            EncodePending();
        }

        Span<byte> digest = stackalloc byte[16];

        checksum.GetHashAndReset(digest);

        long resume = stream.Position;

        stream.Position = MetadataOffset;
        WriteStreamInfo(digest);
        stream.Position = resume;

        stream.Flush(flushToDisk: true);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Finish();
        stream.Dispose();
        checksum.Dispose();
    }

    // De-interleaves into the pending block, and hashes the same integers as the little-endian bytes
    // a WAV would hold, which is what FLAC's MD5 is defined over.
    void Take(ReadOnlySpan<float> samples)
    {
        for (int index = 0; index < samples.Length; index++)
        {
            int value = samples[index].ToPcm24();
            int at = index * BytesPerSample;

            pending[index % channelCount][pendingFrames + (index / channelCount)] = value;
            pcm[at] = (byte)value;
            pcm[at + 1] = (byte)(value >> 8);
            pcm[at + 2] = (byte)(value >> 16);
        }

        checksum.AppendData(pcm, 0, samples.Length * BytesPerSample);
        pendingFrames += samples.Length / channelCount;
        frameCount += samples.Length / channelCount;
    }

    void EncodePending()
    {
        ReadOnlySpan<byte> frame = encoder.Encode(pending, pendingFrames, frameNumber);

        stream.Write(frame);
        bytesWritten += frame.Length;

        minFrameBytes = minFrameBytes == 0 ? frame.Length : Math.Min(minFrameBytes, frame.Length);
        maxFrameBytes = Math.Max(maxFrameBytes, frame.Length);
        frameNumber++;
        pendingFrames = 0;
    }

    /// <remarks>
    /// The metadata block header and STREAMINFO, field by field in the specification's order. Block
    /// sizes are the nominal one even for a stream shorter than a block, which decoders accept.
    /// </remarks>
    void WriteStreamInfo(ReadOnlySpan<byte> digest)
    {
        streamInfo.Reset();

        // Last metadata block, type 0, and its length.
        streamInfo.WriteBits(0x80, 8);
        streamInfo.WriteBits(StreamInfoBytes, 24);

        streamInfo.WriteBits(FlacFrameEncoder.BlockFrames, 16);
        streamInfo.WriteBits(FlacFrameEncoder.BlockFrames, 16);
        streamInfo.WriteBits((uint)minFrameBytes, 24);
        streamInfo.WriteBits((uint)maxFrameBytes, 24);
        streamInfo.WriteBits((uint)SampleRate, 20);
        streamInfo.WriteBits((uint)(channelCount - 1), 3);
        streamInfo.WriteBits(FlacFrameEncoder.BitsPerSample - 1, 5);
        streamInfo.WriteBits((uint)(frameCount >> 32), 4);
        streamInfo.WriteBits((uint)frameCount, 32);

        stream.Write(streamInfo.Written);
        stream.Write(digest);
    }
}
