using Vam.Engine.Devices;

namespace Vam.Engine.Graph;

/// <summary>
/// What a node is given for one block, and nothing more.
/// </summary>
/// <remarks>
/// <para>
/// A <c>ref struct</c>, passed by <c>ref</c>. It cannot be captured, stored, boxed or put in a
/// field, so "you may not keep these buffers past this call" stops being a comment a third-party
/// modifier author might not read and becomes something the compiler refuses to let them write.
/// </para>
/// <para>
/// Inside the audio path.
/// </para>
/// </remarks>
public ref struct RenderContext
{
    readonly RenderArena arena;
    readonly MixBlocks inputs;
    readonly Span<float> output;
    readonly GraphSnapshot snapshot;
    readonly int frameCount;

    /// <summary>
    /// Wraps one block's working set.
    /// </summary>
    /// <remarks>
    /// Five arguments, against the house limit of three or four. Splitting them into a parameter
    /// object would mean allocating one per block, which is the single thing this type exists to
    /// avoid, and every one of the five is genuinely per-block.
    /// </remarks>
    /// <param name="arena">Where the planes live.</param>
    /// <param name="inputs">One block from each device.</param>
    /// <param name="output">Where the primary output's audio goes, interleaved.</param>
    /// <param name="snapshot">The parameters in force for this block. Never changes mid-block.</param>
    /// <param name="frameCount">Frames to render.</param>
    public RenderContext(
        RenderArena arena,
        MixBlocks inputs,
        Span<float> output,
        GraphSnapshot snapshot,
        int frameCount)
    {
        this.arena = arena;
        this.inputs = inputs;
        this.output = output;
        this.snapshot = snapshot;
        this.frameCount = frameCount;
    }

    /// <summary>Frames this block is rendering.</summary>
    public readonly int FrameCount => frameCount;

    /// <summary>The parameters in force. Taken once per block and never re-read mid-block.</summary>
    public readonly GraphSnapshot Snapshot => snapshot;

    /// <summary>One block from each input device, in registry order.</summary>
    public readonly MixBlocks Inputs => inputs;

    /// <summary>Where the primary output's audio goes, interleaved at that device's channel count.</summary>
    public readonly Span<float> Output => output;

    /// <summary>Distance between the start of one plane and the next.</summary>
    /// <remarks>
    /// Not the same as <see cref="FrameCount"/> when a block comes up short, which is why anything
    /// addressing more than one plane at a time has to be told both.
    /// </remarks>
    public readonly int Stride => arena.BlockFrames;

    /// <summary>A run of planes as one contiguous span, for anything working on a whole strip.</summary>
    /// <param name="firstPlane">First plane of the run.</param>
    /// <param name="planeCount">How many.</param>
    /// <returns>The run, at full plane stride.</returns>
    public readonly Span<float> Region(int firstPlane, int planeCount) => arena.Region(firstPlane, planeCount);

    /// <summary>One mono working plane.</summary>
    /// <param name="index">Which plane.</param>
    /// <returns>The plane, <see cref="FrameCount"/> frames long.</returns>
    public readonly Span<float> Plane(int index) => arena.Plane(index, frameCount);
}
