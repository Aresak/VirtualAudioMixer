using Vam.Engine.Modifiers;

namespace Vam.Engine.Graph.Nodes;

/// <summary>
/// Runs one bus's modifier chain, in place, after the sum and before the meter. D6.
/// </summary>
/// <remarks>
/// <para>
/// The same framework as a strip's chain, on the summed bus rather than on one microphone.
/// Equalisation for the room a feed is going into, a compressor for the dynamic range a stream can
/// carry, and a limiter at the end.
/// </para>
/// <para>
/// <b>After the sum and before the meter, and both halves of that matter.</b> After, because
/// limiting each microphone separately does not stop the sum of them clipping. Before, because a
/// bus meter has to show what the bus is actually sending: a meter reading the pre-limiter signal
/// would sit at plus three all evening while the stream went out at minus one, and an operator would
/// spend the meeting chasing a number that was never true.
/// </para>
/// </remarks>
public sealed class BusChainNode(GraphLayout layout, int busIndex, ModifierChain chain, float smoothing) : AudioNode
{
    /// <summary>The chain this node runs, for telemetry and the cost guard.</summary>
    public ModifierChain Chain => chain;

    /// <summary>Which bus it belongs to.</summary>
    public int BusIndex => busIndex;

    /// <inheritdoc />
    public override void Reset() => chain.Reset();

    /// <inheritdoc />
    public override void Process(ref RenderContext context)
    {
        if (chain.Count == 0)
        {
            return;
        }

        chain.Process(
            context.Region(layout.BusPlane(busIndex), layout.BusWidth(busIndex)),
            context.Snapshot.BusChainOf(busIndex),
            context.FrameCount,
            context.Stride,
            smoothing);
    }
}
