using NAudio.Wave;
using Vam.Engine.Devices.Abstractions;

namespace Vam.Engine.Windows.Devices.Wasapi;

/// <summary>
/// Turns the buffer WASAPI hands back into interleaved floats, without allocating.
/// </summary>
/// <remarks>
/// <para>
/// One per open stream, its scratch buffer sized at open. Inside the audio path, so the format is
/// decided once here rather than branched on per sample: the shared-mode engine is float32 on every
/// machine this will run on, and exclusive mode is whatever the hardware does, which for the
/// microphones in reach is 16-bit.
/// </para>
/// <para>
/// A device whose format is neither is refused when the stream opens, not when audio starts. A
/// callback is no place to discover that a conversion was never written.
/// </para>
/// </remarks>
public sealed class WasapiSampleReader
{
    /// <summary>Scale from a signed 16-bit sample to the -1..1 range the engine works in.</summary>
    const float Pcm16Scale = 1.0f / 32768.0f;

    readonly float[] scratch;
    readonly int channelCount;
    readonly bool isFloat;

    /// <summary>Prepares a reader for one stream's format.</summary>
    /// <param name="format">What the device granted.</param>
    /// <param name="maxFrames">Largest packet the device can hand over in one callback.</param>
    /// <exception cref="UnsupportedAudioFormatException">The format is one no conversion has been written for.</exception>
    public WasapiSampleReader(WaveFormat format, int maxFrames)
    {
        ArgumentNullException.ThrowIfNull(format);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFrames, 1);

        channelCount = format.Channels;
        isFloat = IsFloat(format);

        if (!isFloat && format.BitsPerSample != 16)
        {
            throw new UnsupportedAudioFormatException(
                $"No conversion is written for {format.BitsPerSample}-bit {format.Encoding}. "
                + "VAM reads 32-bit float, which is what shared mode always grants, and 16-bit PCM, "
                + "which is what the hardware usually grants in exclusive mode.");
        }

        scratch = new float[maxFrames * channelCount];
    }

    /// <summary>Channels interleaved in every frame.</summary>
    public int ChannelCount => channelCount;

    /// <summary>Whether the device delivers float already, in which case reading is a copy and nothing more.</summary>
    public bool IsFloatFormat => isFloat;

    /// <summary>
    /// Reads one packet.
    /// </summary>
    /// <remarks>
    /// Inside the audio path. The returned span points at this reader's own buffer and is valid
    /// only until the next call - the caller is expected to write it into a ring and forget it.
    /// </remarks>
    /// <param name="buffer">What <c>GetBuffer</c> returned. Owned by WASAPI until the buffer is released.</param>
    /// <param name="frameCount">Frames in the packet.</param>
    /// <param name="isSilent">
    /// Whether WASAPI flagged the packet as silence. It is entitled to hand back a buffer whose
    /// contents are undefined in that case, so the samples must be produced rather than copied.
    /// </param>
    /// <returns>Interleaved floats, <paramref name="frameCount"/> frames of <see cref="ChannelCount"/>.</returns>
    public ReadOnlySpan<float> Read(nint buffer, int frameCount, bool isSilent)
    {
        Span<float> destination = scratch.AsSpan(0, frameCount * channelCount);

        if (isSilent)
        {
            destination.Clear();
            return destination;
        }

        unsafe
        {
            if (isFloat)
            {
                new ReadOnlySpan<float>((void*)buffer, destination.Length).CopyTo(destination);
                return destination;
            }

            ReadOnlySpan<short> source = new((void*)buffer, destination.Length);

            for (int index = 0; index < destination.Length; index++)
            {
                destination[index] = source[index] * Pcm16Scale;
            }
        }

        return destination;
    }

    static bool IsFloat(WaveFormat format)
    {
        if (format.Encoding == WaveFormatEncoding.IeeeFloat)
        {
            return true;
        }

        // Shared mode nearly always reports Extensible rather than a plain tag, and the encoding
        // that matters is then the sub-format rather than the one on the face of it.
        return format is WaveFormatExtensible extensible
            && extensible.ToStandardWaveFormat().Encoding == WaveFormatEncoding.IeeeFloat;
    }
}
