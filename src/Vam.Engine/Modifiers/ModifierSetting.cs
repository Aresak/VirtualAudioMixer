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
}
