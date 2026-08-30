using Vam.Engine.Devices;

namespace Vam.Engine.Graph.Nodes;

/// <summary>
/// Sends a bus to a device that is not the one keeping time. D7.
/// </summary>
/// <remarks>
/// <para>
/// The primary output goes straight into the render callback's own buffer, because the mix thread
/// is that device's thread. Every other output belongs to a device with its own crystal running at
/// its own rate, so its audio goes into a ring and that device's thread takes it out — which is
/// where the drift servo lives.
/// </para>
/// <para>
/// Interleaved here, at the edge, for the same reason it stopped being interleaved at the input
/// edge: exactly one place has to care.
/// </para>
/// </remarks>
public sealed class BusOutputNode : AudioNode
{
    readonly GraphLayout layout;
    readonly BusOutputChannel destination;
    readonly float[] interleaved;
    readonly int busIndex;
    readonly int width;

    /// <summary>Wires one bus to one device.</summary>
    /// <param name="layout">Where the bus's planes are.</param>
    /// <param name="busIndex">Which bus.</param>
    /// <param name="destination">The device's rate-adapting channel.</param>
    /// <param name="blockFrames">Frames per block, to size the scratch once.</param>
    public BusOutputNode(GraphLayout layout, int busIndex, BusOutputChannel destination, int blockFrames)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(destination);

        this.layout = layout;
        this.busIndex = busIndex;
        this.destination = destination;

        width = destination.ChannelCount;
        interleaved = new float[blockFrames * width];
    }

    /// <inheritdoc />
    public override void Process(ref RenderContext context)
    {
        int frames = Math.Min(context.FrameCount, interleaved.Length / width);
        int busWidth = layout.BusWidth(busIndex);
        int busFirst = layout.BusPlane(busIndex);

        for (int channel = 0; channel < width; channel++)
        {
            // A mono bus reaches both sides of a stereo output rather than only the left.
            ReadOnlySpan<float> plane = context.Plane(busFirst + (channel % busWidth));

            for (int frame = 0; frame < frames; frame++)
            {
                interleaved[(frame * width) + channel] = plane[frame];
            }
        }

        destination.WriteBlock(interleaved.AsSpan(0, frames * width), frames);
    }
}
