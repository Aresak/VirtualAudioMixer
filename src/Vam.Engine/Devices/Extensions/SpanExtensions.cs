namespace Vam.Engine.Devices.Extensions;

/// <summary>
/// De-interleaving, for the device side of the audio path.
/// </summary>
/// <remarks>
/// Its own namespace so <see cref="Span{T}"/> does not sprout VAM methods everywhere in
/// IntelliSense — import it where a device buffer is being taken apart and nowhere else.
/// </remarks>
public static class SpanExtensions
{
    /// <summary>
    /// Copies one strip's run of channels out of an interleaved device buffer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Inside the audio path. No allocation, no bounds surprises: the map was validated at
    /// configuration time, and nothing here re-checks it. That is the deal `ChannelMap.Validate`
    /// exists to make good on.
    /// </para>
    /// <para>
    /// The output stays interleaved at the source's own channel count, so a stereo pair arrives as
    /// a stereo strip and a single channel as a mono one, with no branch on which it was.
    /// </para>
    /// </remarks>
    /// <param name="interleaved">The device buffer, <paramref name="deviceChannelCount"/> channels per frame.</param>
    /// <param name="source">Which channels to take.</param>
    /// <param name="deviceChannelCount">Channels the device interleaves.</param>
    /// <param name="destination">Where to write. Its length decides how many frames are copied.</param>
    public static void ExtractInto(
        this ReadOnlySpan<float> interleaved,
        ChannelSource source,
        int deviceChannelCount,
        Span<float> destination)
    {
        int wanted = source.ChannelCount;
        int frames = destination.Length / wanted;
        int available = interleaved.Length / deviceChannelCount;

        if (frames > available)
        {
            frames = available;
        }

        // A whole-buffer copy when the strip takes every channel the device has, in order. The
        // common case for a mono USB microphone, and worth not walking a loop for.
        if (wanted == deviceChannelCount && source.FirstChannel == 0)
        {
            interleaved[..(frames * deviceChannelCount)].CopyTo(destination);
            return;
        }

        for (int frame = 0; frame < frames; frame++)
        {
            int from = (frame * deviceChannelCount) + source.FirstChannel;
            int to = frame * wanted;

            for (int channel = 0; channel < wanted; channel++)
            {
                destination[to + channel] = interleaved[from + channel];
            }
        }
    }
}
