namespace Vam.Engine.Modifiers;

/// <summary>
/// One chain's settings, frozen into the snapshot.
/// </summary>
/// <remarks>
/// <para>
/// Targets rather than values. The host slides towards these once per block, so what a modifier
/// actually sees is a smoothed version and a change in any of them is a swell rather than a step.
/// </para>
/// <para>
/// Bypass is a bit mask rather than an array of flags, so the audio thread reads the whole chain's
/// bypass state in one load. Sixty-four links per chain is far past anything a person would build.
/// </para>
/// </remarks>
public sealed class ChainParams
{
    /// <summary>Most links one chain may hold, set by the width of the bypass mask.</summary>
    public const int MaximumLinks = 64;

    readonly float[] targets;

    /// <summary>Builds one chain's settings.</summary>
    /// <param name="targets">Every link's parameters, laid end to end in ordinal order.</param>
    /// <param name="bypassMask">One bit per link, set when the link is bypassed.</param>
    public ChainParams(float[] targets, ulong bypassMask)
    {
        ArgumentNullException.ThrowIfNull(targets);

        this.targets = targets;
        BypassMask = bypassMask;
    }

    /// <summary>A chain with nothing in it.</summary>
    public static ChainParams Empty { get; } = new([], 0UL);

    /// <summary>One bit per link, set when the link is bypassed.</summary>
    public ulong BypassMask { get; }

    /// <summary>Every link's parameters, in ordinal order.</summary>
    public ReadOnlySpan<float> Targets => targets;

    /// <summary>Whether one link is switched out.</summary>
    /// <param name="linkIndex">Which link.</param>
    /// <returns>Whether it is bypassed.</returns>
    public bool IsBypassed(int linkIndex) => (BypassMask & (1UL << linkIndex)) != 0;

    /// <summary>Produces settings with one link's bypass changed.</summary>
    /// <param name="linkIndex">Which link.</param>
    /// <param name="isBypassed">Whether to switch it out.</param>
    /// <returns>The new settings.</returns>
    public ChainParams WithBypass(int linkIndex, bool isBypassed)
    {
        ulong bit = 1UL << linkIndex;

        return new ChainParams(targets, isBypassed ? BypassMask | bit : BypassMask & ~bit);
    }

    /// <summary>Produces settings with one parameter changed and the rest copied.</summary>
    /// <param name="ordinal">Which parameter, across the whole chain.</param>
    /// <param name="value">Its new target.</param>
    /// <returns>The new settings.</returns>
    public ChainParams WithParameter(int ordinal, float value)
    {
        float[] changed = [.. targets];
        changed[ordinal] = value;

        return new ChainParams(changed, BypassMask);
    }
}
