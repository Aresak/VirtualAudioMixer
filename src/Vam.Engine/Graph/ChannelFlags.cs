namespace Vam.Engine.Graph;

/// <summary>What is true about one input strip, as one word the audio thread reads.</summary>
/// <remarks>
/// Flags rather than several bools so the whole state of a strip arrives in one read. Inside the
/// audio path they are tested with <c>&amp;</c>, never <c>Enum.HasFlag</c>, which boxes.
/// </remarks>
[Flags]
public enum ChannelFlags
{
    /// <summary>Nothing special.</summary>
    None = 0,

    /// <summary>The operator muted it.</summary>
    Muted = 1,

    /// <summary>The operator soloed it.</summary>
    Soloed = 2,

    /// <summary>Routed to the pre-fade listen bus.</summary>
    PreFadeListen = 4,

    /// <summary>Polarity inverted. A11.</summary>
    PolarityInverted = 8,

    /// <summary>A stereo source folded to mono. B8a.</summary>
    MonoFold = 16,

    /// <summary>
    /// The strip's device failed. Set off the audio thread; the audio thread reads it and mixes
    /// silence. I1 fault isolation - a broken device takes down its own strip and nothing else.
    /// </summary>
    Faulted = 32
}
