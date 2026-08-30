namespace Vam.Protocol;

/// <summary>What is true about a strip, packed into one byte of the meter frame.</summary>
/// <remarks>
/// In the frame rather than in the console state, because these change as fast as the meters do and
/// a client that had to ask for them separately would draw a mute a quarter of a second late.
/// </remarks>
[Flags]
public enum MeterFlags : byte
{
    /// <summary>Nothing special.</summary>
    None = 0,

    /// <summary>The operator muted it.</summary>
    Muted = 1,

    /// <summary>The operator soloed it.</summary>
    Soloed = 2,

    /// <summary>Its device failed. The strip is silent and the session continues.</summary>
    Faulted = 4,

    /// <summary>
    /// The automixer has it at or near the depth floor.
    /// </summary>
    /// <remarks>
    /// Drawn as a grey meter rather than a moving one, so an operator can see at a glance which
    /// microphones the automixer is holding down and which are simply quiet.
    /// </remarks>
    Ducked = 8,

    /// <summary>Its device is not present.</summary>
    Absent = 16,

    /// <summary>
    /// It has reached full scale since the indicator was last cleared. F1.
    /// </summary>
    /// <remarks>
    /// Latched. A clip is one block in four hundred, so an operator watching sixteen strips has
    /// about two milliseconds to catch it — which means they never would. It stays lit until somebody
    /// clears it, and clearing it is how they say they have seen it.
    /// </remarks>
    Clipped = 32
}
