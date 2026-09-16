using Vam.Engine.Modifiers;

namespace Vam.Engine.Graph.Nodes;

/// <summary>
/// Runs one strip's modifier chain, in place, between the head stage and the fader. B0.
/// </summary>
/// <remarks>
/// <para>
/// The position is the contract. Everything before this is the fixed head — trim, polarity, mono
/// fold — and everything after it is the fixed tail, the fader and later the automixer's gain. The
/// operator composes what happens in between and cannot move either anchor, because the anchors are
/// where they are in the plan rather than being first and last by convention.
/// </para>
/// <para>
/// In place on the pre-fader planes, so a monitor bus taking the pre-fader tap hears the chain's
/// output. That is what an operator expects: the denoise and the gate are part of the microphone,
/// not part of the mix.
/// </para>
/// </remarks>
public sealed class ChainNode(GraphLayout layout, int channelIndex, ModifierChain chain, float smoothing) : AudioNode
{
    /// <summary>The chain this node runs, for telemetry and the cost guard.</summary>
    public ModifierChain Chain => chain;

    /// <summary>Which strip it belongs to.</summary>
    public int ChannelIndex => channelIndex;

    /// <inheritdoc />
    public override void Reset() => chain.Reset();

    /// <inheritdoc />
    public override void Process(ref RenderContext context)
    {
        if (chain.Count == 0)
        {
            return;
        }

        int width = layout.ChannelWidth(channelIndex);

        chain.Process(
            context.Region(layout.PreFaderPlane(channelIndex), width),
            context.Snapshot.ChainOf(channelIndex),
            context.FrameCount,
            context.Stride,
            smoothing);
    }
}
