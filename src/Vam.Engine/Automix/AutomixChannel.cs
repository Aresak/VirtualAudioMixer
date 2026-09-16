namespace Vam.Engine.Automix;

/// <summary>
/// How one strip takes part in gain sharing. C2.
/// </summary>
/// <remarks>
/// Participation is per strip and off by default for anything that is not a microphone in the room.
/// The audience feed and the online return are the obvious cases: including them means the
/// automixer shares gain with a loudspeaker that is playing back what it just sent, which is a loop
/// with a very slow period and an unpleasant sound.
/// </remarks>
/// <param name="Participates">Whether this strip is part of the sharing at all.</param>
/// <param name="Weight">
/// How much louder or quieter this microphone reads than the others for the same voice. A lectern
/// microphone somebody leans into and a table microphone a metre away are not comparable without it.
/// </param>
public readonly record struct AutomixChannel(bool Participates, float Weight)
{
    /// <summary>A strip taking part on equal terms.</summary>
    public static AutomixChannel Equal => new(true, 1f);

    /// <summary>A strip that is not a microphone in the room.</summary>
    public static AutomixChannel Excluded => new(false, 1f);
}
