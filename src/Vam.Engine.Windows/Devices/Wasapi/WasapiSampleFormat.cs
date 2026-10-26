using NAudio.Wave;

namespace Vam.Engine.Windows.Devices.Wasapi;

/// <summary>
/// Which device formats the sample reader and writer can convert, asked once so the open path and
/// the converters cannot disagree.
/// </summary>
public static class WasapiSampleFormat
{
    /// <summary>Bit depth of the one integer format a conversion is written for.</summary>
    const int Pcm16Bits = 16;

    /// <summary>Whether a stream in this format can be read and written: float, or 16-bit PCM.</summary>
    /// <param name="format">The format a device would grant.</param>
    /// <returns>Whether a conversion is written for it.</returns>
    public static bool IsConvertible(WaveFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);

        return IsFloat(format) || format.BitsPerSample == Pcm16Bits;
    }

    /// <summary>Whether the samples are float, looking through an extensible format to its sub-format.</summary>
    /// <param name="format">The format a device would grant.</param>
    /// <returns>Whether reading it is a copy.</returns>
    public static bool IsFloat(WaveFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);

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
