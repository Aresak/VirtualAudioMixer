namespace Vam.Engine.Graph;

/// <summary>
/// How much of every input reaches every bus. D2, D2a and D4.
/// </summary>
/// <remarks>
/// <para>
/// Two parallel arrays with different readers. <see cref="Gains"/> is what the audio thread walks:
/// a flat block of floats where off, muted and excluded have all already become zero, so the mix
/// loop is a multiply-accumulate with no branches in it. <see cref="States"/> is what the console
/// reads to explain a silent send to a person.
/// </para>
/// <para>
/// Immutable once published. A send change produces a new matrix; the audio thread never sees a
/// half-updated one.
/// </para>
/// </remarks>
public sealed class SendMatrix
{
    readonly float[] gains;
    readonly SendState[] states;

    /// <summary>Builds a matrix of a given shape, with everything off.</summary>
    /// <param name="channelCount">Input strips.</param>
    /// <param name="busCount">Buses.</param>
    public SendMatrix(int channelCount, int busCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(channelCount);
        ArgumentOutOfRangeException.ThrowIfNegative(busCount);

        ChannelCount = channelCount;
        BusCount = busCount;

        gains = new float[channelCount * busCount];
        states = new SendState[channelCount * busCount];
    }

    SendMatrix(SendMatrix other)
    {
        ChannelCount = other.ChannelCount;
        BusCount = other.BusCount;

        gains = [.. other.gains];
        states = [.. other.states];
    }

    /// <summary>Input strips this matrix covers.</summary>
    public int ChannelCount { get; }

    /// <summary>Buses this matrix covers.</summary>
    public int BusCount { get; }

    /// <summary>
    /// The effective gains, row per input. Read by the audio thread; a span, so walking it allocates
    /// nothing.
    /// </summary>
    public ReadOnlySpan<float> Gains => gains;

    /// <summary>What the console shows. Control thread only.</summary>
    public ReadOnlySpan<SendState> States => states;

    /// <summary>Where one pair sits in the flat arrays.</summary>
    /// <param name="channelIndex">Which input.</param>
    /// <param name="busIndex">Which bus.</param>
    /// <returns>The index into <see cref="Gains"/> and <see cref="States"/>.</returns>
    public int IndexOf(int channelIndex, int busIndex) => (channelIndex * BusCount) + busIndex;

    /// <summary>Copies this matrix so one pair can be changed without disturbing the published one.</summary>
    /// <returns>A mutable copy.</returns>
    public SendMatrix ToBuilder() => new(this);

    /// <summary>
    /// Sets one pair. Control thread, before publication.
    /// </summary>
    /// <param name="channelIndex">Which input.</param>
    /// <param name="busIndex">Which bus.</param>
    /// <param name="state">Why it is at this level.</param>
    /// <param name="gain">The send level, linear. Ignored unless the state is <see cref="SendState.On"/>.</param>
    public void Set(int channelIndex, int busIndex, SendState state, float gain)
    {
        int index = IndexOf(channelIndex, busIndex);

        states[index] = state;

        // Collapsed here, once, rather than branched on per block. Everything that means "no audio"
        // becomes the same zero, which is why the mix loop has no conditionals in it.
        gains[index] = state == SendState.On ? gain : 0.0f;
    }

    /// <summary>The level one pair is carrying.</summary>
    /// <param name="channelIndex">Which input.</param>
    /// <param name="busIndex">Which bus.</param>
    /// <returns>Linear gain, zero when the send is off or excluded.</returns>
    public float GainOf(int channelIndex, int busIndex) => gains[IndexOf(channelIndex, busIndex)];

    /// <summary>Why one pair is at the level it is.</summary>
    /// <param name="channelIndex">Which input.</param>
    /// <param name="busIndex">Which bus.</param>
    /// <returns>The state the console shows.</returns>
    public SendState StateOf(int channelIndex, int busIndex) => states[IndexOf(channelIndex, busIndex)];
}
