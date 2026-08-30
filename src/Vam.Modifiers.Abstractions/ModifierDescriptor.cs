namespace Vam.Modifiers.Abstractions;

/// <summary>
/// What a modifier is and what it needs, declared before any audio reaches it.
/// </summary>
/// <remarks>
/// <para>
/// Every one of these is checked when the chain is built, on the control thread, and never again.
/// A chain whose channel counts do not fit is refused there with a message naming the link that
/// does not fit — rather than discovered inside a callback, which is the one place that cannot
/// report anything.
/// </para>
/// </remarks>
/// <param name="Id">Stable identifier, persisted in presets and configurations.</param>
/// <param name="Name">What the console calls it.</param>
/// <param name="ChannelsIn">Channels it consumes, or zero for "however many it is given".</param>
/// <param name="ChannelsOut">Channels it produces, or zero for "the same as it was given".</param>
/// <param name="LatencySamples">
/// Delay it introduces. Declared rather than measured, because the automixer has to align channels
/// against each other before it compares them — without alignment it hands gain to whichever
/// channel happens to be ten milliseconds ahead.
/// </param>
/// <param name="CanProcessInPlace">
/// Whether it can read and write the same buffer. False means the host provides scratch and copies,
/// which costs a pass over memory, so it is worth being honest about.
/// </param>
public readonly record struct ModifierDescriptor(
    string Id,
    string Name,
    int ChannelsIn,
    int ChannelsOut,
    int LatencySamples,
    bool CanProcessInPlace)
{
    /// <summary>Whether this modifier adapts to whatever channel count it is handed.</summary>
    public bool IsChannelAgnostic => ChannelsIn == 0;

    /// <summary>How many channels come out, given how many went in.</summary>
    /// <param name="channelsIn">What it is being handed.</param>
    /// <returns>What it will produce.</returns>
    public int ChannelsOutFor(int channelsIn) => ChannelsOut == 0 ? channelsIn : ChannelsOut;

    /// <summary>Whether this modifier will accept a given channel count.</summary>
    /// <param name="channelsIn">What it is being handed.</param>
    /// <returns>Whether it fits.</returns>
    public bool Accepts(int channelsIn) => IsChannelAgnostic || ChannelsIn == channelsIn;
}
