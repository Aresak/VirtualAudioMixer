using Vam.Engine.Automix;
using Vam.Engine.Modifiers;

namespace Vam.Engine.Graph;

/// <summary>
/// Every parameter the graph needs, frozen. The audio thread only ever reads one of these.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the seam the whole architecture rests on.</b> Configuration is built on the control
/// thread, published with one write of a reference, and never touched again. There is no lock
/// anywhere in the audio path because there is nothing to lock: the audio thread holds a snapshot
/// for the length of a block, and any change the operator makes produces a different one.
/// </para>
/// <para>
/// <b>Structural sharing is the point, not an optimisation.</b> A fader move copies the channel
/// array with one element changed and keeps the plan and the send matrix reference-identical. That
/// is about two kilobytes on the control thread and nothing at all on the audio thread — which is
/// what lets a dragged fader publish fifty times a second without the collector noticing.
/// </para>
/// </remarks>
public sealed class GraphSnapshot
{
    readonly ChannelParams[] channels;
    readonly BusParams[] buses;
    readonly ChainParams[] chains;

    /// <summary>Builds the first snapshot for a plan.</summary>
    /// <param name="plan">The compiled graph.</param>
    /// <param name="channels">Per-strip parameters.</param>
    /// <param name="buses">Per-bus parameters.</param>
    /// <param name="sends">How much of each strip reaches each bus.</param>
    /// <param name="chains">Each strip's modifier chain settings.</param>
    /// <param name="automix">The automixer's settings.</param>
    public GraphSnapshot(
        GraphPlan plan,
        ChannelParams[] channels,
        BusParams[] buses,
        SendMatrix sends,
        ChainParams[]? chains = null,
        AutomixParams? automix = null)
        : this(
            plan,
            channels,
            buses,
            sends,
            chains ?? EmptyChains(channels.Length + buses.Length),
            automix ?? AutomixParams.Empty,
            version: 0)
    {
    }

    GraphSnapshot(
        GraphPlan plan,
        ChannelParams[] channels,
        BusParams[] buses,
        SendMatrix sends,
        ChainParams[] chains,
        AutomixParams automix,
        long version)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentNullException.ThrowIfNull(buses);
        ArgumentNullException.ThrowIfNull(sends);

        Plan = plan;
        Sends = sends;
        Version = version;

        this.channels = channels;
        this.buses = buses;
        this.chains = chains;

        Automix = automix;
        IsAnySoloed = AnySoloed(channels);
        IsAnyPreFadeListening = AnyFlagged(channels, ChannelFlags.PreFadeListen);
    }

    /// <summary>The compiled graph. Shared across every snapshot built on it.</summary>
    public GraphPlan Plan { get; }

    /// <summary>The automixer's settings.</summary>
    public AutomixParams Automix { get; }

    /// <summary>How much of each strip reaches each bus.</summary>
    public SendMatrix Sends { get; }

    /// <summary>
    /// Monotonic. The audio thread records the highest it has seen so the control thread knows when
    /// an older snapshot can be let go.
    /// </summary>
    public long Version { get; }

    /// <summary>
    /// Whether anything is soloed, worked out once here rather than scanned per block.
    /// </summary>
    /// <remarks>
    /// The solo mask is not a per-strip property, it is a property of the whole console: with
    /// nothing soloed every unmuted strip is heard, and with anything soloed only the soloed ones
    /// are. Deciding that once, off the audio thread, is what keeps the mix loop branch-free.
    /// </remarks>
    public bool IsAnySoloed { get; }

    /// <summary>Per-strip parameters. A span, so the audio thread walks it without allocating.</summary>
    public ReadOnlySpan<ChannelParams> Channels => channels;

    /// <summary>Per-bus parameters.</summary>
    public ReadOnlySpan<BusParams> Buses => buses;

    /// <summary>Input strips.</summary>
    public int ChannelCount => channels.Length;

    /// <summary>Buses.</summary>
    public int BusCount => buses.Length;

    /// <summary>
    /// Whether one strip is heard at all this block, once mute, fault and the solo mask are settled.
    /// </summary>
    /// <param name="channelIndex">Which strip.</param>
    /// <returns>Whether it contributes.</returns>
    public bool IsHeard(int channelIndex)
    {
        ChannelParams channel = channels[channelIndex];

        if (channel.IsSilent)
        {
            return false;
        }

        return !IsAnySoloed || (channel.Flags & ChannelFlags.Soloed) != 0;
    }

    /// <summary>
    /// Whether any strip is being listened to before its fader. B7.
    /// </summary>
    public bool IsAnyPreFadeListening { get; }

    /// <summary>
    /// Whether one strip is what a monitor bus should be carrying, under pre-fade listen. B7.
    /// </summary>
    /// <remarks>
    /// PFL is the operator's inspection tool: it replaces what a monitor carries with the strips
    /// being listened to, taken before the fader so a strip pulled all the way down can still be
    /// checked. It reaches monitors only — a PFL that changed the stream would be an operator
    /// checking a microphone and broadcasting the check.
    /// </remarks>
    /// <param name="channelIndex">Which strip.</param>
    /// <returns>Whether it should be heard while a pre-fade listen is engaged.</returns>
    public bool IsPreFadeListened(int channelIndex)
    {
        ChannelParams channel = channels[channelIndex];

        return !channel.IsSilent && (channel.Flags & ChannelFlags.PreFadeListen) != 0;
    }

    /// <summary>Produces a snapshot with one strip changed and everything else shared.</summary>
    /// <param name="channelIndex">Which strip.</param>
    /// <param name="parameters">Its new parameters.</param>
    /// <returns>The new snapshot.</returns>
    public GraphSnapshot WithChannel(int channelIndex, ChannelParams parameters)
    {
        ChannelParams[] changed = [.. channels];
        changed[channelIndex] = parameters;

        return new GraphSnapshot(Plan, changed, buses, Sends, chains, Automix, Version + 1);
    }

    /// <summary>Produces a snapshot with one bus changed and everything else shared.</summary>
    /// <param name="busIndex">Which bus.</param>
    /// <param name="parameters">Its new parameters.</param>
    /// <returns>The new snapshot.</returns>
    public GraphSnapshot WithBus(int busIndex, BusParams parameters)
    {
        BusParams[] changed = [.. buses];
        changed[busIndex] = parameters;

        return new GraphSnapshot(Plan, channels, changed, Sends, chains, Automix, Version + 1);
    }

    /// <summary>Produces a snapshot with every strip replaced and the plan shared.</summary>
    /// <param name="parameters">The new per-strip parameters.</param>
    /// <returns>The new snapshot.</returns>
    public GraphSnapshot WithChannels(ChannelParams[] parameters) =>
        new(Plan, parameters, buses, Sends, chains, Automix, Version + 1);

    /// <summary>Produces a snapshot with every bus replaced and the plan shared.</summary>
    /// <param name="parameters">The new per-bus parameters.</param>
    /// <returns>The new snapshot.</returns>
    public GraphSnapshot WithBuses(BusParams[] parameters) =>
        new(Plan, channels, parameters, Sends, chains, Automix, Version + 1);

    /// <summary>Produces a snapshot with a new send matrix and everything else shared.</summary>
    /// <param name="sends">The new matrix.</param>
    /// <returns>The new snapshot.</returns>
    public GraphSnapshot WithSends(SendMatrix sends) =>
        new(Plan, channels, buses, sends, chains, Automix, Version + 1);

    /// <summary>Produces a snapshot with every chain replaced and the plan shared.</summary>
    /// <param name="parameters">The new chain settings, one per strip.</param>
    /// <returns>The new snapshot.</returns>
    public GraphSnapshot WithChains(ChainParams[] parameters) =>
        new(Plan, channels, buses, Sends, parameters, Automix, Version + 1);

    /// <summary>Produces a snapshot with one strip's chain changed and everything else shared.</summary>
    /// <param name="channelIndex">Which strip.</param>
    /// <param name="parameters">Its new chain settings.</param>
    /// <returns>The new snapshot.</returns>
    public GraphSnapshot WithChain(int channelIndex, ChainParams parameters)
    {
        ChainParams[] changed = [.. chains];
        changed[channelIndex] = parameters;

        return new GraphSnapshot(Plan, channels, buses, Sends, changed, Automix, Version + 1);
    }

    /// <summary>Produces a snapshot with one bus's chain settings changed and the plan shared.</summary>
    /// <param name="busIndex">Which bus.</param>
    /// <param name="parameters">Its new chain settings.</param>
    /// <returns>The new snapshot.</returns>
    public GraphSnapshot WithBusChain(int busIndex, ChainParams parameters)
    {
        ChainParams[] changed = [.. chains];
        changed[ChannelCount + busIndex] = parameters;

        return new GraphSnapshot(Plan, channels, buses, Sends, changed, Automix, Version + 1);
    }

    /// <summary>Produces a snapshot with new automixer settings and the plan shared.</summary>
    /// <param name="parameters">The new settings.</param>
    /// <returns>The new snapshot.</returns>
    public GraphSnapshot WithAutomix(AutomixParams parameters) =>
        new(Plan, channels, buses, Sends, chains, parameters, Version + 1);

    /// <summary>One strip's chain settings.</summary>
    /// <param name="channelIndex">Which strip.</param>
    /// <returns>Its settings, or an empty chain when it has none.</returns>
    public ChainParams ChainOf(int channelIndex) =>
        channelIndex >= 0 && channelIndex < ChannelCount ? chains[channelIndex] : ChainParams.Empty;

    /// <summary>
    /// One bus's chain settings. D6.
    /// </summary>
    /// <remarks>
    /// Bus chains live in the same array as the strips', after them, rather than in a second one.
    /// A snapshot is copied on every published change, and one array copied is cheaper and harder to
    /// get out of step than two that have to agree about their lengths.
    /// </remarks>
    /// <param name="busIndex">Which bus.</param>
    /// <returns>Its chain settings, or empty.</returns>
    public ChainParams BusChainOf(int busIndex)
    {
        int at = ChannelCount + busIndex;

        return busIndex >= 0 && at < chains.Length ? chains[at] : ChainParams.Empty;
    }

    static ChainParams[] EmptyChains(int count)
    {
        ChainParams[] empty = new ChainParams[count];

        Array.Fill(empty, ChainParams.Empty);

        return empty;
    }

    static bool AnyFlagged(ChannelParams[] channels, ChannelFlags flag)
    {
        foreach (ChannelParams channel in channels)
        {
            if ((channel.Flags & flag) != 0)
            {
                return true;
            }
        }

        return false;
    }

    static bool AnySoloed(ChannelParams[] channels)
    {
        foreach (ChannelParams channel in channels)
        {
            if ((channel.Flags & ChannelFlags.Soloed) != 0)
            {
                return true;
            }
        }

        return false;
    }
}
