namespace Vam.Engine.Graph.Nodes;

/// <summary>
/// Sums every strip into one bus at its send level. D2, D2a, D4 and D5.
/// </summary>
/// <remarks>
/// <para>
/// The send matrix has already collapsed off, muted and mix-minus-excluded into a gain of zero, so
/// this is a multiply-accumulate with no conditionals in it. That is what makes mix-minus
/// impossible to get wrong at this level: an excluded send is not a special case here, it is a
/// zero, and a zero cannot be switched on by accident.
/// </para>
/// <para>
/// Which tap each strip is read from is the bus's business, not the strip's. A monitor takes
/// pre-fader so the person in the chair keeps hearing the same thing while the operator works.
/// </para>
/// </remarks>
public sealed class BusMixNode(GraphLayout layout, int busIndex, int channelCount, float smoothing) : AudioNode
{
    // One per strip, allocated when the plan is compiled. A send switched on is the largest jump
    // this graph can make, so it is the one that most needs sliding rather than stepping.
    readonly SmoothedGain[] sendGains = new SmoothedGain[channelCount];

    /// <summary>Where this bus's planes are, for anything that needs to read them back.</summary>
    public GraphLayout Layout => layout;

    /// <summary>Which bus it sums into.</summary>
    public int BusIndex => busIndex;

    /// <inheritdoc />
    public override void Reset() => Array.Clear(sendGains);

    /// <inheritdoc />
    public override void Process(ref RenderContext context)
    {
        GraphSnapshot snapshot = context.Snapshot;
        BusParams bus = snapshot.Buses[busIndex];
        int busWidth = layout.BusWidth(busIndex);
        int busFirst = layout.BusPlane(busIndex);

        for (int plane = 0; plane < busWidth; plane++)
        {
            context.Plane(busFirst + plane).Clear();
        }

        if (bus.IsMuted)
        {
            return;
        }

        // B7. Pre-fade listen replaces what a monitor carries with the strips being inspected, and
        // it takes them before the fader so a strip pulled all the way down can still be checked.
        // Monitors only: a PFL that reached the stream would be an operator checking a microphone
        // and broadcasting the check.
        bool listening = bus.ObeysPreFadeListen && snapshot.IsAnyPreFadeListening;
        bool preFader = bus.IsPreFader || listening;

        for (int channel = 0; channel < snapshot.ChannelCount && channel < sendGains.Length; channel++)
        {
            // Solo is the operator's monitoring tool. It reaches an output bus and never the stream,
            // because one click silencing a public broadcast is a mistake that ends up in the minutes.
            bool heard = listening
                ? snapshot.IsPreFadeListened(channel)
                : !bus.ObeysSolo || snapshot.IsHeard(channel);

            // Under PFL the send matrix is bypassed too: an operator listening to a microphone wants
            // to hear it whether or not it happens to be routed to the monitor they are wearing.
            float routing = listening ? bus.Gain : snapshot.Sends.GainOf(channel, busIndex) * bus.Gain;
            float target = heard ? routing : 0f;
            float gain = sendGains[channel].Advance(target, smoothing);

            if (gain == 0f)
            {
                continue;
            }

            Accumulate(ref context, channel, busFirst, busWidth, preFader, gain, snapshot.Channels[channel]);
        }
    }

    void Accumulate(
        ref RenderContext context,
        int channelIndex,
        int busFirst,
        int busWidth,
        bool preFader,
        float gain,
        ChannelParams channel)
    {
        int width = layout.ChannelWidth(channelIndex);
        int first = preFader ? layout.PreFaderPlane(channelIndex) : layout.PostFaderPlane(channelIndex);

        for (int plane = 0; plane < busWidth; plane++)
        {
            // B8. The pan is folded into the send gain here rather than applied as a stage of its
            // own, so it costs a multiply that was happening anyway. A strip already as wide as the
            // bus keeps its own image: panning a stereo pair by moving both of its channels the same
            // way is not panning, it is a balance control that collapses the image.
            float panned = width < busWidth ? gain * channel.PanFor(plane, busWidth) : gain;

            // A mono strip is heard across a stereo bus rather than only on the left; a strip wider
            // than the bus folds its extra channels down onto the ones that exist.
            Span<float> destination = context.Plane(busFirst + plane);

            for (int source = plane; source < width; source += busWidth)
            {
                ReadOnlySpan<float> from = context.Plane(first + source);

                for (int frame = 0; frame < destination.Length; frame++)
                {
                    destination[frame] += from[frame] * panned;
                }
            }

            if (width < busWidth)
            {
                CopyNarrowSource(ref context, first, width, plane, destination, panned);
            }
        }
    }

    static void CopyNarrowSource(
        ref RenderContext context,
        int first,
        int width,
        int plane,
        Span<float> destination,
        float gain)
    {
        if (plane < width)
        {
            return;
        }

        ReadOnlySpan<float> from = context.Plane(first + (plane % width));

        for (int frame = 0; frame < destination.Length; frame++)
        {
            destination[frame] += from[frame] * gain;
        }
    }
}
