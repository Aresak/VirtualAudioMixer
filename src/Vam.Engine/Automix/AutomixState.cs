namespace Vam.Engine.Automix;

/// <summary>
/// What the automixer is doing right now, for the console to draw. C9 and C4.
/// </summary>
/// <remarks>
/// <para>
/// The share bar and its history are not decoration. They are how an operator learns to trust the
/// automixer instead of reaching for the faders, and an operator who does not trust it will turn it
/// off during the first meeting that matters.
/// </para>
/// <para>
/// Written by the audio thread into pre-allocated arrays and read off thread whenever a meter frame
/// is built. Values may be a block out of date, which is fine for something a person looks at.
/// </para>
/// </remarks>
public sealed class AutomixState
{
    readonly float[] shares;
    readonly float[] gainsDb;

    /// <summary>Sizes the state for a console.</summary>
    /// <param name="channelCount">Strips.</param>
    public AutomixState(int channelCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(channelCount);

        shares = new float[channelCount];
        gainsDb = new float[channelCount];
    }

    /// <summary>Each strip's share of the gain, summing to one across the participants.</summary>
    public ReadOnlySpan<float> Shares => shares;

    /// <summary>What the automixer is applying to each strip, in decibels. Zero or negative.</summary>
    public ReadOnlySpan<float> GainsDb => gainsDb;

    /// <summary>
    /// The number of open microphones. C4.
    /// </summary>
    /// <remarks>
    /// <b>From the participation ratio, not from counting channels above a threshold.</b> A count is
    /// discontinuous: a microphone hovering at the boundary steps the whole bus by three decibels
    /// over and over, which is far more audible than the thing the count was correcting. The
    /// participation ratio is continuous by construction, lands on exactly N when N microphones
    /// share equally and on one when a single microphone holds everything — and it is free, because
    /// the shares have already been computed.
    /// </remarks>
    public float NumberOfOpenMicrophones { get; private set; } = 1f;

    /// <summary>Writes one block's result. Audio thread.</summary>
    /// <param name="index">Which strip.</param>
    /// <param name="share">Its share.</param>
    /// <param name="gainDb">What is being applied to it.</param>
    public void Record(int index, float share, float gainDb)
    {
        shares[index] = share;
        gainsDb[index] = gainDb;
    }

    /// <summary>Sets the open-microphone count for this block. Audio thread.</summary>
    /// <param name="sumOfSquaredShares">The sum of every share squared.</param>
    public void RecordOpenMicrophones(float sumOfSquaredShares) =>
        NumberOfOpenMicrophones = sumOfSquaredShares <= 0f ? 0f : 1f / sumOfSquaredShares;

    /// <summary>Forgets everything.</summary>
    public void Reset()
    {
        Array.Clear(shares);
        Array.Clear(gainsDb);

        NumberOfOpenMicrophones = 1f;
    }
}
