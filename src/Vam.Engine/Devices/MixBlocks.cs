namespace Vam.Engine.Devices;

/// <summary>
/// One block of audio from every input device, as the graph will receive it.
/// </summary>
/// <remarks>
/// <para>
/// A <c>ref struct</c> on purpose. It cannot be captured, stored or boxed, so "you may not keep
/// these buffers past the call" is enforced by the compiler rather than written in a comment
/// nobody reads. The same reasoning EPIC-03 applies to its render context, applied here first
/// because this is where the buffers enter the graph.
/// </para>
/// <para>
/// Written with an explicit constructor against the house rule that requires primary constructors:
/// a <c>ref struct</c> may not capture a ref-like primary constructor parameter into an instance
/// member (CS9110), so the fields have to be spelled out. The language is talking, not a preference.
/// </para>
/// <para>
/// Inside the audio path.
/// </para>
/// </remarks>
public readonly ref struct MixBlocks
{
    readonly ReadOnlySpan<float> arena;
    readonly ReadOnlySpan<BlockSlice> slices;
    readonly int frameCount;

    /// <summary>Wraps one block's worth of every device.</summary>
    /// <param name="arena">Every device's block, laid end to end.</param>
    /// <param name="slices">Where each device's block starts and how wide it is.</param>
    /// <param name="frameCount">Frames in each block.</param>
    public MixBlocks(ReadOnlySpan<float> arena, ReadOnlySpan<BlockSlice> slices, int frameCount)
    {
        this.arena = arena;
        this.slices = slices;
        this.frameCount = frameCount;
    }

    /// <summary>Devices in this set.</summary>
    public int Count => slices.Length;

    /// <summary>Frames in every block.</summary>
    public int FrameCount => frameCount;

    /// <summary>Takes one device's block.</summary>
    /// <param name="index">Which device, in the registry's order.</param>
    /// <returns>Interleaved samples, <see cref="FrameCount"/> frames of that device's channel count.</returns>
    public ReadOnlySpan<float> this[int index]
    {
        get
        {
            BlockSlice slice = slices[index];

            return arena.Slice(slice.Offset, frameCount * slice.ChannelCount);
        }
    }

    /// <summary>Channels in one device's block.</summary>
    /// <param name="index">Which device.</param>
    /// <returns>Its channel count.</returns>
    public int ChannelCountOf(int index) => slices[index].ChannelCount;
}
