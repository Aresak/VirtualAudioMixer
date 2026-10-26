using Vam.Protocol.V1;
using Vam.Ui.State;

namespace Vam.Ui.Extensions;

/// <summary>What the console draws a strip as, from what the engine said about it.</summary>
public static class ChannelStateExtensions
{
    /// <summary>
    /// The colour this strip is drawn in, wherever it appears.
    /// </summary>
    /// <remarks>
    /// The strip, the channel panel, the share band and its legend have to agree. An operator who
    /// coloured the mayor's microphone red and then reads the share history in blue cannot read the
    /// history at all.
    /// </remarks>
    /// <param name="channel">The strip.</param>
    /// <returns>A CSS colour.</returns>
    public static string ToStripColour(this ChannelState channel)
    {
        ArgumentNullException.ThrowIfNull(channel);

        return string.IsNullOrWhiteSpace(channel.Colour) ? StripPalette.For(channel.Index) : channel.Colour;
    }
}
