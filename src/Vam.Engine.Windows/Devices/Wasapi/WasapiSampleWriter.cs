using NAudio.Wave;
using Vam.Engine.Devices.Abstractions;

namespace Vam.Engine.Windows.Devices.Wasapi;

/// <summary>
/// Hands the fill delegate somewhere to write, and gets what it wrote into the device's buffer.
/// </summary>
/// <remarks>
/// <para>
/// The mirror of <see cref="WasapiSampleReader"/>, with one asymmetry worth having: when the device
/// takes float — which is every shared-mode endpoint — the graph writes <b>straight into WASAPI's
/// own buffer</b> and there is no copy at all, not even an allocation-free one. Only a device that
/// wants 16-bit needs the scratch and the conversion.
/// </para>
/// <para>
/// Inside the audio path. Everything is sized when the stream opens.
/// </para>
/// </remarks>
public sealed class WasapiSampleWriter
{
    /// <summary>Scale from the -1..1 range the engine works in to a signed 16-bit sample.</summary>
    const float Pcm16Scale = 32767.0f;

    readonly float[] scratch;
    readonly int channelCount;
    readonly bool isFloat;

    /// <summary>Prepares a writer for one stream's format.</summary>
    /// <param name="format">What the device granted.</param>
    /// <param name="maxFrames">Largest buffer the device will ask to have filled.</param>
    /// <exception cref="UnsupportedAudioFormatException">The format is one no conversion has been written for.</exception>
    public WasapiSampleWriter(WaveFormat format, int maxFrames)
    {
        ArgumentNullException.ThrowIfNull(format);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFrames, 1);

        channelCount = format.Channels;
        isFloat = IsFloat(format);

        if (!isFloat && format.BitsPerSample != 16)
        {
            throw new UnsupportedAudioFormatException(
                $"No conversion is written for {format.BitsPerSample}-bit {format.Encoding}. "
                + "VAM writes 32-bit float, which is what shared mode always grants, and 16-bit PCM.");
        }

        // A float device is written into directly, so the scratch is never touched. Sized to one
        // frame rather than to zero because an empty array is a special case waiting to be hit.
        scratch = isFloat ? new float[channelCount] : new float[maxFrames * channelCount];
    }

    /// <summary>Channels interleaved in every frame.</summary>
    public int ChannelCount => channelCount;

    /// <summary>Whether the device takes float, in which case writing costs nothing at all.</summary>
    public bool IsFloatFormat => isFloat;

    /// <summary>
    /// Gives the fill delegate somewhere to write.
    /// </summary>
    /// <param name="buffer">What <c>GetBuffer</c> returned.</param>
    /// <param name="frameCount">Frames the device is asking for.</param>
    /// <returns>Where to write. Valid until <see cref="Commit"/>.</returns>
    public Span<float> Prepare(nint buffer, int frameCount)
    {
        int sampleCount = frameCount * channelCount;

        if (!isFloat)
        {
            return scratch.AsSpan(0, sampleCount);
        }

        unsafe
        {
            return new Span<float>((void*)buffer, sampleCount);
        }
    }

    /// <summary>
    /// Finishes the buffer, silencing whatever the delegate did not fill.
    /// </summary>
    /// <remarks>
    /// The shortfall is written as silence rather than left alone. WASAPI's buffer holds whatever
    /// was played last time, so leaving it would replay a fragment of old audio - a stutter, which
    /// is far more noticeable than the gap it is standing in for.
    /// </remarks>
    /// <param name="buffer">What <c>GetBuffer</c> returned.</param>
    /// <param name="frameCount">Frames the device asked for.</param>
    /// <param name="framesWritten">Frames the delegate actually filled.</param>
    public void Commit(nint buffer, int frameCount, int framesWritten)
    {
        int filled = Math.Clamp(framesWritten, 0, frameCount) * channelCount;
        int total = frameCount * channelCount;

        if (isFloat)
        {
            unsafe
            {
                new Span<float>((void*)buffer, total)[filled..].Clear();
            }

            return;
        }

        unsafe
        {
            Span<short> destination = new((void*)buffer, total);

            for (int index = 0; index < filled; index++)
            {
                destination[index] = (short)(Math.Clamp(scratch[index], -1.0f, 1.0f) * Pcm16Scale);
            }

            destination[filled..].Clear();
        }
    }

    static bool IsFloat(WaveFormat format)
    {
        if (format.Encoding == WaveFormatEncoding.IeeeFloat)
        {
            return true;
        }

        return format is WaveFormatExtensible extensible
            && extensible.ToStandardWaveFormat().Encoding == WaveFormatEncoding.IeeeFloat;
    }
}
