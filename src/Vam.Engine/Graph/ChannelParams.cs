namespace Vam.Engine.Graph;

/// <summary>
/// Everything the audio thread needs to know about one input strip for one block.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately data only, and deliberately linear rather than decibels. A conversion in here would
/// be a conversion per block per strip, and the control thread has all the time in the world to do
/// it once instead.
/// </para>
/// <para>
/// This is <b>snapshot</b>, not state: written before publication and never again. Filter histories
/// and envelopes live with the nodes, because putting them here would mean copying them on every
/// fader move, and then a parameter change either clicks or races.
/// </para>
/// </remarks>
/// <param name="TrimGain">Input trim as a linear gain. A8.</param>
/// <param name="FaderGain">Fader position as a linear gain. B8.</param>
/// <param name="Flags">Mute, solo, polarity, mono fold and fault. B7, A11, B8a.</param>
/// <param name="ChannelCount">Channels this strip carries. One for a microphone, two for a stereo pair.</param>
public readonly record struct ChannelParams(
    float TrimGain,
    float FaderGain,
    ChannelFlags Flags,
    int ChannelCount)
{
    /// <summary>A strip at unity with nothing switched on.</summary>
    public static ChannelParams Unity => new(1.0f, 1.0f, ChannelFlags.None, 1);

    /// <summary>Whether this strip contributes anything at all this block.</summary>
    /// <remarks>
    /// Muted, faulted and never-routed all collapse to the same thing by the time the mix runs -
    /// a gain of zero - so the audio path has no special case for any of them.
    /// </remarks>
    public bool IsSilent => (Flags & (ChannelFlags.Muted | ChannelFlags.Faulted)) != 0;
}
