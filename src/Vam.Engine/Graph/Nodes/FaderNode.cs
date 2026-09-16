namespace Vam.Engine.Graph.Nodes;

/// <summary>
/// The fader, the mute and the solo mask, producing a strip's post-fader signal. B7 and B8.
/// </summary>
/// <remarks>
/// Writes into separate planes rather than scaling in place, because the pre-fader signal is still
/// wanted: a monitor bus takes it, and it must not move when the operator rides the fader for the
/// stream.
/// </remarks>
public sealed class FaderNode(GraphLayout layout, int channelIndex, float smoothing) : AudioNode
{
    SmoothedGain gain;

    /// <inheritdoc />
    public override void Reset() => gain.Reset(0f);

    /// <inheritdoc />
    public override void Process(ref RenderContext context)
    {
        GraphSnapshot snapshot = context.Snapshot;
        int width = layout.ChannelWidth(channelIndex);
        int source = layout.PreFaderPlane(channelIndex);
        int destination = layout.PostFaderPlane(channelIndex);

        ChannelParams parameters = snapshot.Channels[channelIndex];

        // Mute and fault, and deliberately not solo. Solo belongs to a bus, not to a strip: applied
        // here it would be a global mute and would reach the stream, so one operator listening to
        // one microphone would silence a public broadcast. BusMixNode decides which buses obey it.
        //
        // Slid towards rather than jumped to, because a step in gain is a click and the fader is
        // the control an operator actually drags.
        float target = parameters.IsSilent ? 0f : parameters.FaderGain;
        float applied = gain.Advance(target, smoothing);

        for (int plane = 0; plane < width; plane++)
        {
            ReadOnlySpan<float> from = context.Plane(source + plane);
            Span<float> to = context.Plane(destination + plane);

            for (int frame = 0; frame < to.Length; frame++)
            {
                to[frame] = from[frame] * applied;
            }
        }
    }
}
