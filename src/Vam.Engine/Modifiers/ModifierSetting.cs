namespace Vam.Engine.Modifiers;

/// <summary>
/// One link of a chain, as configuration.
/// </summary>
/// <remarks>
/// <para>
/// Values are keyed by parameter identifier, never by position. The audio thread reads parameters
/// by ordinal because that is fast; a saved configuration reads them by name because that is
/// stable. A modifier that reorders its parameters in version two would otherwise silently load a
/// saved threshold into its ratio.
/// </para>
/// </remarks>
public sealed record ModifierSetting
{
    /// <summary>Which modifier, by the identifier in its descriptor.</summary>
    public required string ModifierId { get; init; }

    /// <summary>
    /// This link's own identity, assigned when it was added to the chain and persisted with it.
    /// </summary>
    /// <remarks>
    /// Distinct from the modifier identifier because a chain may hold two of the same modifier and
    /// they are not interchangeable - they carry different histories. This is what lets a reorder
    /// keep the instances it already had instead of restarting every filter in the chain.
    /// </remarks>
    public string LinkId { get; init; } = Guid.NewGuid().ToString("n");

    /// <summary>Whether this link is switched out. B0b.</summary>
    public bool IsBypassed { get; init; }

    /// <summary>Parameter values, keyed by parameter identifier. Anything absent takes its default.</summary>
    public Dictionary<string, float> Values { get; init; } = [];

    /// <summary>
    /// A detached copy, with its own identity and its own values.
    /// </summary>
    /// <remarks>
    /// The record's own `with` copies the dictionary by reference, which is right for a snapshot and
    /// wrong for a preset: a preset sharing a values dictionary with the live strip it was saved from
    /// would follow every knob somebody turned afterwards. A new link identity, because two chains
    /// must never claim the same link — that is what the compiler uses to decide it can keep a
    /// modifier instance and its filter history.
    /// </remarks>
    /// <returns>The copy.</returns>
    public ModifierSetting Copy() => new()
    {
        ModifierId = ModifierId,
        IsBypassed = IsBypassed,
        Values = new Dictionary<string, float>(Values)
    };
}
