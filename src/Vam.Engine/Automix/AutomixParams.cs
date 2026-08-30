namespace Vam.Engine.Automix;

/// <summary>
/// The automixer's settings, frozen into the snapshot.
/// </summary>
/// <remarks>
/// One response knob rather than an attack and a release, because an operator setting up a council
/// chamber has one question — how quickly should it follow the conversation — and two controls that
/// have to be set in relation to each other is two chances to get it wrong.
/// </remarks>
public sealed class AutomixParams
{
    readonly AutomixChannel[] channels;

    /// <summary>Builds the settings.</summary>
    /// <param name="channels">How each strip takes part.</param>
    /// <param name="depthDb">How far a strip that is not being spoken into is turned down.</param>
    /// <param name="responseMilliseconds">How quickly the sharing follows the conversation.</param>
    /// <param name="isBypassed">Whether the whole automixer is switched out. C10.</param>
    public AutomixParams(AutomixChannel[] channels, float depthDb, float responseMilliseconds, bool isBypassed)
    {
        ArgumentNullException.ThrowIfNull(channels);

        this.channels = channels;

        DepthDb = depthDb;
        ResponseMilliseconds = responseMilliseconds;
        IsBypassed = isBypassed;
    }

    /// <summary>An automixer with nothing taking part, switched out.</summary>
    public static AutomixParams Empty { get; } = new([], -15f, 120f, isBypassed: true);

    /// <summary>How each strip takes part.</summary>
    public ReadOnlySpan<AutomixChannel> Channels => channels;

    /// <summary>
    /// How far a strip nobody is speaking into is turned down. C3.
    /// </summary>
    /// <remarks>
    /// A floor rather than a mute. Turning an unused microphone all the way off makes the room sound
    /// dead between speakers, and the moment somebody starts talking the whole ambience arrives with
    /// them. Fifteen decibels down is enough to stop six microphones summing their room noise and
    /// little enough that the chamber still sounds like a room.
    /// </remarks>
    public float DepthDb { get; }

    /// <summary>How quickly the sharing follows the conversation.</summary>
    public float ResponseMilliseconds { get; }

    /// <summary>Whether the whole automixer is switched out. C10.</summary>
    public bool IsBypassed { get; }

    /// <summary>Produces settings with the bypass changed and everything else shared.</summary>
    /// <param name="isBypassed">Whether to switch it out.</param>
    /// <returns>The new settings.</returns>
    public AutomixParams WithBypass(bool isBypassed) =>
        new(channels, DepthDb, ResponseMilliseconds, isBypassed);
}
