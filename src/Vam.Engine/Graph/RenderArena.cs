namespace Vam.Engine.Graph;

/// <summary>
/// Every buffer the graph will use for one block, in one allocation.
/// </summary>
/// <remarks>
/// <para>
/// <b>One array, not an array of arrays.</b> A sixteen-channel block's working set is a few
/// kilobytes and fits in L1 — but only if it is contiguous. Planes of a jagged array land wherever
/// the collector put them, and the cache stops helping.
/// </para>
/// <para>
/// <b>Planar mono planes, not interleaved stereo.</b> Every kernel in this engine works on one
/// channel at a time, and interleaving would force a gather in each of them. Interleaving happens
/// once, at the edges, where a device demands it.
/// </para>
/// <para>
/// <b>Pinned.</b> The audio thread holds spans into this for the length of a block and the collector
/// must not move it underneath them. Allocated once per plan, so pinning costs nothing ongoing —
/// and uninitialised, because every plane is written before it is read.
/// </para>
/// </remarks>
public sealed class RenderArena
{
    readonly float[] buffer;

    /// <summary>Allocates the arena for one plan.</summary>
    /// <param name="planeCount">Mono planes needed. One per channel of every strip and every bus.</param>
    /// <param name="blockFrames">Frames in a block.</param>
    public RenderArena(int planeCount, int blockFrames)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(planeCount, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(blockFrames, 1);

        PlaneCount = planeCount;
        BlockFrames = blockFrames;

        buffer = GC.AllocateUninitializedArray<float>(planeCount * blockFrames, pinned: true);
    }

    /// <summary>Mono planes this arena holds.</summary>
    public int PlaneCount { get; }

    /// <summary>Frames in each plane.</summary>
    public int BlockFrames { get; }

    /// <summary>
    /// One mono plane, for as many frames as this block is rendering.
    /// </summary>
    /// <remarks>Inside the audio path. A slice, so it allocates nothing and bounds-checks once.</remarks>
    /// <param name="index">Which plane.</param>
    /// <param name="frameCount">Frames wanted, at most <see cref="BlockFrames"/>.</param>
    /// <returns>The plane.</returns>
    public Span<float> Plane(int index, int frameCount) =>
        buffer.AsSpan(index * BlockFrames, frameCount);

    /// <summary>Zeroes every plane. Control thread, when a plan is first installed.</summary>
    public void Clear() => Array.Clear(buffer);
}
