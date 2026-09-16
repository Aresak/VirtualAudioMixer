namespace Vam.Engine.Graph;

/// <summary>
/// Which plane of the arena holds what.
/// </summary>
/// <remarks>
/// <para>
/// Worked out once when the plan is compiled, so every node knows its buffers by index and nothing
/// looks anything up per block.
/// </para>
/// <para>
/// Every strip gets two sets of planes, pre-fader and post-fader, and that is not waste. A monitor
/// bus takes the pre-fader tap so the operator riding a fader for the stream does not change what
/// the person wearing the headphones hears — which means both signals genuinely exist at once, and
/// recomputing one from the other would mean dividing by a gain that may be zero.
/// </para>
/// </remarks>
public sealed class GraphLayout
{
    readonly int[] preFaderPlane;
    readonly int[] postFaderPlane;
    readonly int[] channelWidth;
    readonly int[] busPlane;
    readonly int[] busWidth;

    /// <summary>Works out the plane assignment for a console of a given shape.</summary>
    /// <param name="channelWidths">Channels each strip carries, after any mono fold.</param>
    /// <param name="busWidths">Channels each bus carries.</param>
    public GraphLayout(IReadOnlyList<int> channelWidths, IReadOnlyList<int> busWidths)
    {
        ArgumentNullException.ThrowIfNull(channelWidths);
        ArgumentNullException.ThrowIfNull(busWidths);

        preFaderPlane = new int[channelWidths.Count];
        postFaderPlane = new int[channelWidths.Count];
        channelWidth = [.. channelWidths];
        busPlane = new int[busWidths.Count];
        busWidth = [.. busWidths];

        int next = 0;

        for (int channel = 0; channel < channelWidths.Count; channel++)
        {
            preFaderPlane[channel] = next;
            next += channelWidths[channel];

            postFaderPlane[channel] = next;
            next += channelWidths[channel];
        }

        for (int bus = 0; bus < busWidths.Count; bus++)
        {
            busPlane[bus] = next;
            next += busWidths[bus];
        }

        PlaneCount = Math.Max(next, 1);
    }

    /// <summary>Total mono planes the arena must hold.</summary>
    public int PlaneCount { get; }

    /// <summary>Strips.</summary>
    public int ChannelCount => channelWidth.Length;

    /// <summary>Buses.</summary>
    public int BusCount => busWidth.Length;

    /// <summary>First plane of a strip's signal before the fader.</summary>
    /// <param name="channelIndex">Which strip.</param>
    /// <returns>The plane index.</returns>
    public int PreFaderPlane(int channelIndex) => preFaderPlane[channelIndex];

    /// <summary>First plane of a strip's signal after the fader.</summary>
    /// <param name="channelIndex">Which strip.</param>
    /// <returns>The plane index.</returns>
    public int PostFaderPlane(int channelIndex) => postFaderPlane[channelIndex];

    /// <summary>Channels a strip carries.</summary>
    /// <param name="channelIndex">Which strip.</param>
    /// <returns>Its width in planes.</returns>
    public int ChannelWidth(int channelIndex) => channelWidth[channelIndex];

    /// <summary>First plane of a bus.</summary>
    /// <param name="busIndex">Which bus.</param>
    /// <returns>The plane index.</returns>
    public int BusPlane(int busIndex) => busPlane[busIndex];

    /// <summary>Channels a bus carries.</summary>
    /// <param name="busIndex">Which bus.</param>
    /// <returns>Its width in planes.</returns>
    public int BusWidth(int busIndex) => busWidth[busIndex];
}
