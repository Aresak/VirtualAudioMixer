using Vam.Engine.Dsp.Extensions;
using Vam.Engine.Metering;

namespace Vam.Engine.Graph.Nodes;

/// <summary>
/// Leaves a peak and a sum of squares for every strip and every bus. F1 to F7.
/// </summary>
/// <remarks>
/// Two numbers per meter and no arithmetic beyond a comparison and an add. Everything a meter
/// actually shows — decibels, ballistics, whether it is peak or RMS or VU — happens off this thread,
/// which is why the operator can change the ballistics without the engine knowing anything about it.
/// </remarks>
public sealed class MeterNode(GraphLayout layout, MeterCells channels, MeterCells buses) : AudioNode
{
    /// <summary>What the strips left behind.</summary>
    public MeterCells Channels => channels;

    /// <summary>What the buses left behind.</summary>
    public MeterCells Buses => buses;

    /// <inheritdoc />
    public override void Reset()
    {
        channels.Clear();
        buses.Clear();
    }

    /// <inheritdoc />
    public override void Process(ref RenderContext context)
    {
        for (int channel = 0; channel < layout.ChannelCount && channel < channels.Count; channel++)
        {
            Accumulate(ref context, channels, channel, layout.PostFaderPlane(channel), layout.ChannelWidth(channel));
        }

        for (int bus = 0; bus < layout.BusCount && bus < buses.Count; bus++)
        {
            Accumulate(ref context, buses, bus, layout.BusPlane(bus), layout.BusWidth(bus));
        }
    }

    static void Accumulate(ref RenderContext context, MeterCells cells, int index, int firstPlane, int width)
    {
        float peak = 0f;
        double sum = 0.0;
        int frames = 0;

        for (int plane = 0; plane < width; plane++)
        {
            ReadOnlySpan<float> samples = context.Plane(firstPlane + plane);

            peak = Math.Max(peak, samples.PeakAbs());
            sum += samples.MeanSquare() * samples.Length;
            frames += samples.Length;
        }

        cells.Accumulate(index, peak, sum, frames);
    }
}
