using Vam.Engine.Devices;

namespace Vam.Engine.Graph;

/// <summary>
/// The compiled graph: which nodes run, in what order, over which buffers.
/// </summary>
/// <remarks>
/// <para>
/// A flat array walked in order, not a tree walked recursively. The order was decided when the plan
/// was compiled — topologically, once, on the control thread — so the audio thread does no graph
/// traversal at all. It is a <c>for</c> loop over an array of sealed classes doing hundreds of
/// nanoseconds of work each, which is one perfectly predicted indirect branch per node.
/// </para>
/// <para>
/// A plan is immutable and is shared by every snapshot built on it. A fader move produces a new
/// snapshot whose plan is reference-identical to the old one, which is why a parameter change costs
/// two kilobytes on the control thread and nothing at all on the audio thread.
/// </para>
/// </remarks>
public sealed class GraphPlan
{
    readonly AudioNode[] nodes;

    /// <summary>Assembles a plan.</summary>
    /// <param name="arena">The buffers these nodes address.</param>
    /// <param name="nodes">The nodes, already in topological order.</param>
    public GraphPlan(RenderArena arena, AudioNode[] nodes)
    {
        ArgumentNullException.ThrowIfNull(arena);
        ArgumentNullException.ThrowIfNull(nodes);

        Arena = arena;
        this.nodes = nodes;
    }

    /// <summary>Where this plan's buffers live.</summary>
    public RenderArena Arena { get; }

    /// <summary>The nodes, in the order they run.</summary>
    public ReadOnlySpan<AudioNode> Nodes => nodes;

    /// <summary>
    /// Runs every node once, in order.
    /// </summary>
    /// <remarks>Inside the audio path. This is the render path, and everything reachable from it.</remarks>
    /// <param name="inputs">One block from each device.</param>
    /// <param name="output">Where the primary output's audio goes.</param>
    /// <param name="snapshot">Parameters for this block. Taken once and not re-read.</param>
    /// <param name="frameCount">Frames to render.</param>
    public void Render(MixBlocks inputs, Span<float> output, GraphSnapshot snapshot, int frameCount)
    {
        RenderContext context = new(Arena, inputs, output, snapshot, frameCount);

        for (int index = 0; index < nodes.Length; index++)
        {
            nodes[index].Process(ref context);
        }
    }

    /// <summary>Clears every node's memory. Control thread.</summary>
    public void Reset()
    {
        Arena.Clear();

        foreach (AudioNode node in nodes)
        {
            node.Reset();
        }
    }
}
