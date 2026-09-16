using Vam.Engine.Dsp;

namespace Vam.Engine.Graph.Nodes;

/// <summary>
/// Delays the strips that finished early so every strip is the same age.
/// </summary>
/// <remarks>
/// <para>
/// <b>Without this the automixer is wrong, and wrong in a way that is hard to diagnose.</b> A strip
/// with a denoise in it is a frame behind one without — twenty milliseconds at forty-eight
/// kilohertz. Gain sharing compares those strips against each other, so unaligned it hands the gain
/// to whichever microphone happens to be earliest rather than to whoever is speaking, and the
/// symptom is an automixer that favours one councillor for no reason anybody can see.
/// </para>
/// <para>
/// It sits before the automixer and after the chains, which is the only place the latencies are all
/// known and none of the comparisons have happened yet.
/// </para>
/// </remarks>
public sealed class LatencyAlignNode : AudioNode
{
    readonly GraphLayout layout;
    readonly DelayLine[][] delays;
    readonly int[] channels;

    /// <summary>Builds the delays that bring every strip to the same age.</summary>
    /// <param name="layout">Where each strip's planes are.</param>
    /// <param name="latencies">Each strip's own latency, in samples.</param>
    public LatencyAlignNode(GraphLayout layout, IReadOnlyList<int> latencies)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(latencies);

        this.layout = layout;

        int longest = 0;

        foreach (int latency in latencies)
        {
            longest = Math.Max(longest, latency);
        }

        List<int> aligned = [];
        List<DelayLine[]> lines = [];

        for (int channel = 0; channel < latencies.Count; channel++)
        {
            int catchUp = longest - latencies[channel];

            // A strip that is already the slowest needs no line at all, and neither does a console
            // where nothing has any latency - which is most of them.
            if (catchUp <= 0)
            {
                continue;
            }

            int width = layout.ChannelWidth(channel);
            DelayLine[] perPlane = new DelayLine[width];

            for (int plane = 0; plane < width; plane++)
            {
                perPlane[plane] = new DelayLine(catchUp) { DelaySamples = catchUp };
            }

            aligned.Add(channel);
            lines.Add(perPlane);
        }

        channels = [.. aligned];
        delays = [.. lines];
        LongestLatencySamples = longest;
    }

    /// <summary>The latency every strip is brought up to.</summary>
    public int LongestLatencySamples { get; }

    /// <summary>Strips that needed delaying at all.</summary>
    public int AlignedChannelCount => channels.Length;

    /// <inheritdoc />
    public override void Reset()
    {
        foreach (DelayLine[] channel in delays)
        {
            foreach (DelayLine line in channel)
            {
                line.Reset();
            }
        }
    }

    /// <inheritdoc />
    public override void Process(ref RenderContext context)
    {
        for (int index = 0; index < channels.Length; index++)
        {
            int first = layout.PostFaderPlane(channels[index]);

            for (int plane = 0; plane < delays[index].Length; plane++)
            {
                delays[index][plane].Process(context.Plane(first + plane));
            }
        }
    }
}
