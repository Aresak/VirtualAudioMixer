using Vam.Engine.Devices;

namespace Vam.Engine.Graph.Nodes;

/// <summary>
/// Takes one device's block into a strip's planes, and applies the head stage.
/// </summary>
/// <remarks>
/// <para>
/// Trim (A8), polarity (A11) and mono fold (B8a) all happen here, in one pass, because each is a
/// multiply or a sign on a sample that is being copied anyway. Splitting them into three nodes
/// would mean three passes over the same memory for arithmetic that costs less than the loads.
/// </para>
/// <para>
/// This is also where interleaved becomes planar. Everything downstream works one channel at a
/// time, so it happens once here rather than as a gather in every kernel.
/// </para>
/// </remarks>
public sealed class InputNode(GraphLayout layout, int channelIndex, int deviceIndex) : AudioNode
{
    /// <inheritdoc />
    public override void Process(ref RenderContext context)
    {
        ChannelParams parameters = context.Snapshot.Channels[channelIndex];
        int width = layout.ChannelWidth(channelIndex);
        int firstPlane = layout.PreFaderPlane(channelIndex);

        if (deviceIndex >= context.Inputs.Count)
        {
            // The device this strip expects is not in this block's set. Silence rather than a
            // reach past the end - a strip whose device went away must be quiet, not a crash.
            ClearPlanes(ref context, firstPlane, width);
            return;
        }

        ReadOnlySpan<float> block = context.Inputs[deviceIndex];
        int sourceWidth = context.Inputs.ChannelCountOf(deviceIndex);
        float gain = parameters.TrimGain;

        if ((parameters.Flags & ChannelFlags.PolarityInverted) != 0)
        {
            gain = -gain;
        }

        if ((parameters.Flags & ChannelFlags.MonoFold) != 0)
        {
            Fold(ref context, block, sourceWidth, firstPlane, gain);
            return;
        }

        Split(ref context, block, sourceWidth, firstPlane, width, gain);
    }

    static void ClearPlanes(ref RenderContext context, int firstPlane, int width)
    {
        for (int plane = 0; plane < width; plane++)
        {
            context.Plane(firstPlane + plane).Clear();
        }
    }

    void Fold(ref RenderContext context, ReadOnlySpan<float> block, int sourceWidth, int firstPlane, float gain)
    {
        Span<float> destination = context.Plane(firstPlane);

        // Averaged rather than summed. Summing two correlated channels is six decibels of level the
        // operator did not ask for, and the first thing they would do is pull the trim back down.
        float scale = gain / sourceWidth;

        for (int frame = 0; frame < destination.Length; frame++)
        {
            float sum = 0f;
            int offset = frame * sourceWidth;

            for (int channel = 0; channel < sourceWidth; channel++)
            {
                sum += block[offset + channel];
            }

            destination[frame] = sum * scale;
        }
    }

    void Split(
        ref RenderContext context,
        ReadOnlySpan<float> block,
        int sourceWidth,
        int firstPlane,
        int width,
        float gain)
    {
        for (int plane = 0; plane < width; plane++)
        {
            Span<float> destination = context.Plane(firstPlane + plane);

            // A strip wider than its device repeats the last channel it has rather than going
            // silent halfway across. A mono microphone on a stereo strip is heard on both sides.
            int source = Math.Min(plane, sourceWidth - 1);

            for (int frame = 0; frame < destination.Length; frame++)
            {
                destination[frame] = block[(frame * sourceWidth) + source] * gain;
            }
        }
    }
}
