using Vam.Engine.Devices.Abstractions;

namespace Vam.Engine.Devices.Extensions;

/// <summary>
/// Checks that an opened stream carries the format it was opened for.
/// </summary>
/// <remarks>
/// A backend is contracted to grant the rate and width asked for or to throw, so this should be
/// unreachable. It exists because the failure it prevents is completely silent. A ring fed at a rate
/// it is not drained at underruns for the length of the session, one fed a width it was not built
/// for reads interleaved channels as consecutive frames, and a clock source at the wrong rate runs
/// the entire graph at that rate. None of the three announces itself; all three are found hours
/// later, in a recording.
/// </remarks>
public static class AudioStreamExtensions
{
    /// <summary>
    /// Throws unless the stream delivers the format it was asked for, closing it first.
    /// </summary>
    /// <remarks>
    /// Control thread, at the open. Every caller already treats a failed open as "leave it closed
    /// and try again", which is the right outcome here too: a strip that is silent and retrying is
    /// better than one that is audible and wrong.
    /// </remarks>
    /// <param name="stream">What the backend opened.</param>
    /// <param name="sampleRateHz">The rate the engine needs.</param>
    /// <param name="channelCount">The width the engine needs, or 0 when any is acceptable.</param>
    /// <exception cref="UnsupportedAudioFormatException">The stream carries something else.</exception>
    public static void RequireFormat(this IAudioStream stream, int sampleRateHz, int channelCount)
    {
        ArgumentNullException.ThrowIfNull(stream);

        AudioStreamFormat granted = stream.Format;

        if (granted.SampleRate == sampleRateHz && (channelCount is 0 || granted.ChannelCount == channelCount))
        {
            return;
        }

        stream.Dispose();

        throw new UnsupportedAudioFormatException(
            $"{stream.DeviceId.Value} opened at {granted.SampleRate} Hz {granted.ChannelCount} ch, and the engine "
            + $"asked for {sampleRateHz} Hz {channelCount} ch. Nothing between the device and the graph converts "
            + "between the two, so it is left closed rather than run at a format it does not agree with.");
    }
}
