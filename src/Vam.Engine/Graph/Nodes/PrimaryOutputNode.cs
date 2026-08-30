namespace Vam.Engine.Graph.Nodes;

/// <summary>
/// Interleaves one bus into the primary output device's buffer. D7.
/// </summary>
/// <remarks>
/// The last node in the plan, and the only one that writes anywhere outside the arena. Planar
/// becomes interleaved here, at the edge, for the same reason it stopped being interleaved at the
/// other edge: exactly one place has to care.
/// </remarks>
public sealed class PrimaryOutputNode(GraphLayout layout, int busIndex, int outputChannelCount) : AudioNode
{
    /// <inheritdoc />
    public override void Process(ref RenderContext context)
    {
        Span<float> output = context.Output;

        if (output.IsEmpty)
        {
            return;
        }

        int frames = Math.Min(context.FrameCount, output.Length / outputChannelCount);
        int busWidth = layout.BusWidth(busIndex);
        int busFirst = layout.BusPlane(busIndex);

        for (int channel = 0; channel < outputChannelCount; channel++)
        {
            // A mono bus reaches both sides of a stereo output rather than only the left.
            ReadOnlySpan<float> plane = context.Plane(busFirst + (channel % busWidth));

            for (int frame = 0; frame < frames; frame++)
            {
                output[(frame * outputChannelCount) + channel] = plane[frame];
            }
        }
    }
}
