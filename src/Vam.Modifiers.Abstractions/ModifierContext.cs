namespace Vam.Modifiers.Abstractions;

/// <summary>
/// Everything a modifier is given for one block, and nothing it can keep.
/// </summary>
/// <remarks>
/// <para>
/// A <c>ref struct</c>, and that is the load-bearing decision in this whole ABI. It cannot be
/// captured by a lambda, stored in a field, boxed or put in a collection, so "you may not retain
/// this buffer past the call" is something the compiler refuses to let a third-party author write
/// rather than a sentence in documentation they may never read.
/// </para>
/// <para>
/// <b>Parameters arrive as a flat span indexed by ordinal, never a dictionary.</b> A dictionary
/// lookup on the audio thread is a hash, a bounds check and very possibly a string. The values are
/// already smoothed — the host does that, once per block, so no modifier has to carry
/// interpolation and none of them can get it wrong.
/// </para>
/// <para>
/// Inside the audio path.
/// </para>
/// </remarks>
public readonly ref struct ModifierContext
{
    readonly Span<float> audio;
    readonly ReadOnlySpan<float> parameters;
    readonly Span<float> scratch;
    readonly ref ModifierTelemetry telemetry;

    /// <summary>Wraps one block for one modifier.</summary>
    /// <param name="audio">The channels to process, in place.</param>
    /// <param name="parameters">Smoothed parameter values, indexed by ordinal.</param>
    /// <param name="scratch">Working space the modifier may use and must not depend on between blocks.</param>
    /// <param name="telemetry">Where to report what it is doing. Owned by the host.</param>
    /// <param name="channelCount">Channels in <paramref name="audio"/>.</param>
    /// <param name="frameCount">Frames in each channel.</param>
    /// <param name="stride">Distance between the start of one channel and the next.</param>
    public ModifierContext(
        Span<float> audio,
        ReadOnlySpan<float> parameters,
        Span<float> scratch,
        ref ModifierTelemetry telemetry,
        int channelCount,
        int frameCount,
        int stride)
    {
        this.audio = audio;
        this.parameters = parameters;
        this.scratch = scratch;
        this.telemetry = ref telemetry;

        ChannelCount = channelCount;
        FrameCount = frameCount;
        Stride = stride;
    }

    /// <summary>Channels this block carries.</summary>
    public int ChannelCount { get; }

    /// <summary>Frames in each channel.</summary>
    public int FrameCount { get; }

    /// <summary>Distance between the start of one channel and the next within the audio span.</summary>
    public int Stride { get; }

    /// <summary>Smoothed parameter values, indexed by the ordinal of their descriptor.</summary>
    public ReadOnlySpan<float> Parameters => parameters;

    /// <summary>Working space, at least one block long. Its contents between blocks are undefined.</summary>
    public Span<float> Scratch => scratch;

    /// <summary>Where to report gain reduction and level.</summary>
    public ref ModifierTelemetry Telemetry => ref telemetry;

    /// <summary>One channel of audio, to be modified in place.</summary>
    /// <param name="index">Which channel.</param>
    /// <returns>The samples.</returns>
    public Span<float> Channel(int index) => audio.Slice(index * Stride, FrameCount);
}
