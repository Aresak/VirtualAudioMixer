using Vam.Modifiers.Abstractions;

namespace Vam.Engine.Modifiers;

/// <summary>
/// One link of a built chain: the instance, and the identity that lets it survive a rebuild.
/// </summary>
/// <remarks>
/// The identity is what makes a reorder silent. Recompiling the plan with fresh instances would
/// restart every filter history and envelope in the chain, and a denoise restarting mid-sentence is
/// audible - so a link that is still in the chain after a reorder keeps the object it had.
/// </remarks>
/// <param name="LinkId">Stable identity of this link, assigned when it was added and persisted.</param>
/// <param name="Modifier">The instance, with its state.</param>
public readonly record struct ChainLink(string LinkId, Modifier Modifier);
