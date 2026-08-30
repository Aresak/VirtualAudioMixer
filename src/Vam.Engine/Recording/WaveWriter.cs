using System.Buffers.Binary;

namespace Vam.Engine.Recording;

/// <summary>
/// Writes one track as a WAV file that survives being longer than four gigabytes.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every file starts as RF64-capable, whether or not it ends up needing it.</b> A four-hour
/// session projects to about fifteen gigabytes across the tracks, and one twenty-four bit mono track
/// on its own is 2.07 gigabytes — under the four gigabyte limit a plain RIFF header can express, but
/// only just, and the stream bus is stereo. Discovering at hour four that the record of a public
/// meeting has a size field that wrapped is not a recoverable mistake.
/// </para>
/// <para>
/// The mechanism is the one the standard describes: reserve a <c>JUNK</c> chunk of exactly the size
/// a <c>ds64</c> would take when the file is created, and if the file outgrows RIFF, rewrite that
/// chunk in place on the way out. Reserving it costs twenty-eight bytes in a file that never needs
/// it, and not reserving it would mean rewriting the whole file to make room.
/// </para>
/// <para>
/// Twenty-four bit, because that is what the epic asks for and because a recording that is the legal
/// record of a meeting should not have been through a lossy step or a needless requantisation.
/// </para>
/// </remarks>
public sealed class WaveWriter : IDisposable
{
    /// <summary>Bytes a <c>ds64</c> chunk body takes, and therefore what the placeholder reserves.</summary>
    public const int Ds64BodyBytes = 28;

    /// <summary>Bytes per sample. Twenty-four bit.</summary>
    public const int BytesPerSample = 3;

    /// <summary>The largest size a plain RIFF header can express.</summary>
    public const long RiffLimitBytes = 0xFFFFFFFFL;

    /// <summary>Full scale for a signed twenty-four bit sample.</summary>
    const float FullScale = 8388607f;

    readonly FileStream stream;
    readonly byte[] block;
    readonly int channelCount;

    long dataBytes;
    bool isFinished;

    /// <summary>Creates the file and writes its header.</summary>
    /// <param name="path">Where to write.</param>
    /// <param name="sampleRate">The rate to declare.</param>
    /// <param name="channelCount">Channels to declare.</param>
    /// <param name="maxFramesPerWrite">Largest block that will be handed over at once.</param>
    public WaveWriter(string path, int sampleRate, int channelCount, int maxFramesPerWrite)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfLessThan(sampleRate, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(channelCount, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFramesPerWrite, 1);

        Path = path;
        SampleRate = sampleRate;

        this.channelCount = channelCount;

        block = new byte[maxFramesPerWrite * channelCount * BytesPerSample];
        stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.Read, 1 << 16);

        WriteHeader();
    }

    /// <summary>Where the file is.</summary>
    public string Path { get; }

    /// <summary>The rate declared in the header.</summary>
    public int SampleRate { get; }

    /// <summary>Frames written so far.</summary>
    public long FrameCount => dataBytes / (channelCount * BytesPerSample);

    /// <summary>Bytes of audio written so far.</summary>
    public long DataBytes => dataBytes;

    /// <summary>Whether the file has outgrown what a plain RIFF header can express.</summary>
    public bool NeedsRf64 => dataBytes + HeaderBytes > RiffLimitBytes;

    static int HeaderBytes => 12 + 8 + Ds64BodyBytes + 8 + 16 + 8;

    /// <summary>Writes one block of interleaved samples.</summary>
    /// <param name="samples">The audio, interleaved at the declared channel count.</param>
    public void Write(ReadOnlySpan<float> samples)
    {
        ObjectDisposedException.ThrowIf(isFinished, this);

        int bytes = samples.Length * BytesPerSample;

        for (int index = 0; index < samples.Length; index++)
        {
            int value = (int)Math.Clamp(samples[index] * FullScale, -FullScale, FullScale);
            int at = index * BytesPerSample;

            block[at] = (byte)value;
            block[at + 1] = (byte)(value >> 8);
            block[at + 2] = (byte)(value >> 16);
        }

        stream.Write(block, 0, bytes);
        dataBytes += bytes;
    }

    /// <summary>
    /// Patches the sizes and closes the file.
    /// </summary>
    /// <remarks>
    /// Called on every path out, including a fault. A file whose sizes were never patched is a file
    /// no player will open, and it will contain a whole meeting.
    /// </remarks>
    public void Finish()
    {
        if (isFinished)
        {
            return;
        }

        isFinished = true;

        if (NeedsRf64)
        {
            PatchAsRf64();
        }
        else
        {
            PatchAsRiff();
        }

        stream.Flush(flushToDisk: true);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Finish();
        stream.Dispose();
    }

    /// <remarks>
    /// Over the fourteen-statement limit, deliberately. A RIFF header is a fixed sequence of fields
    /// in a fixed order, and every statement here writes one of them. Grouping them into helpers
    /// would hide the one thing a reader needs to check: that the order matches the specification.
    /// </remarks>
    void WriteHeader()
    {
        Span<byte> header = stackalloc byte[HeaderBytes];

        header.Clear();

        // RIFF for now. If the file outgrows it, these four bytes become RF64 and the JUNK below
        // becomes the ds64 that carries the real sizes.
        "RIFF"u8.CopyTo(header);
        BinaryPrimitives.WriteUInt32LittleEndian(header[4..], 0);
        "WAVE"u8.CopyTo(header[8..]);

        "JUNK"u8.CopyTo(header[12..]);
        BinaryPrimitives.WriteUInt32LittleEndian(header[16..], Ds64BodyBytes);

        int at = 20 + Ds64BodyBytes;

        "fmt "u8.CopyTo(header[at..]);
        BinaryPrimitives.WriteUInt32LittleEndian(header[(at + 4)..], 16);
        BinaryPrimitives.WriteUInt16LittleEndian(header[(at + 8)..], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(header[(at + 10)..], (ushort)channelCount);
        BinaryPrimitives.WriteUInt32LittleEndian(header[(at + 12)..], (uint)SampleRate);
        BinaryPrimitives.WriteUInt32LittleEndian(header[(at + 16)..], (uint)(SampleRate * channelCount * BytesPerSample));
        BinaryPrimitives.WriteUInt16LittleEndian(header[(at + 20)..], (ushort)(channelCount * BytesPerSample));
        BinaryPrimitives.WriteUInt16LittleEndian(header[(at + 22)..], BytesPerSample * 8);

        at += 24;

        "data"u8.CopyTo(header[at..]);
        BinaryPrimitives.WriteUInt32LittleEndian(header[(at + 4)..], 0);

        stream.Write(header);
    }

    void PatchAsRiff()
    {
        Span<byte> value = stackalloc byte[4];

        BinaryPrimitives.WriteUInt32LittleEndian(value, (uint)(dataBytes + HeaderBytes - 8));
        WriteAt(4, value);

        BinaryPrimitives.WriteUInt32LittleEndian(value, (uint)dataBytes);
        WriteAt(HeaderBytes - 4, value);
    }

    void PatchAsRf64()
    {
        Span<byte> value = stackalloc byte[8];

        "RF64"u8.CopyTo(value);
        WriteAt(0, value[..4]);

        // Both thirty-two bit fields say "look in the ds64" by carrying the maximum value.
        BinaryPrimitives.WriteUInt32LittleEndian(value, 0xFFFFFFFF);
        WriteAt(4, value[..4]);
        WriteAt(HeaderBytes - 4, value[..4]);

        "ds64"u8.CopyTo(value[..4]);
        WriteAt(12, value[..4]);

        Span<byte> body = stackalloc byte[Ds64BodyBytes];

        body.Clear();

        BinaryPrimitives.WriteInt64LittleEndian(body, dataBytes + HeaderBytes - 8);
        BinaryPrimitives.WriteInt64LittleEndian(body[8..], dataBytes);
        BinaryPrimitives.WriteInt64LittleEndian(body[16..], FrameCount);
        BinaryPrimitives.WriteUInt32LittleEndian(body[24..], 0);

        WriteAt(20, body);
    }

    void WriteAt(long position, ReadOnlySpan<byte> value)
    {
        long resume = stream.Position;

        stream.Position = position;
        stream.Write(value);
        stream.Position = resume;
    }
}
