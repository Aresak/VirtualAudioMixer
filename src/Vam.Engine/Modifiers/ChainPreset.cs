namespace Vam.Engine.Modifiers;

/// <summary>
/// A whole chain, saved. B0d.
/// </summary>
/// <remarks>
/// <para>
/// <b>Order and membership, not just values.</b> Once a chain differs per channel, "Jabra shared"
/// and "Studio 180 degrees" stop being the same strip with different numbers and become genuinely
/// different objects — one of them has a denoise in it and the other does not. A preset that stored
/// only settings could not express that, and would silently apply a compressor's threshold to
/// whatever happened to be third in the chain.
/// </para>
/// </remarks>
/// <param name="Name">What the operator calls it.</param>
/// <param name="Links">The chain, head to tail.</param>
public sealed record ChainPreset(string Name, IReadOnlyList<ModifierSetting> Links)
{
    /// <summary>Copies the preset's links so a strip can diverge from it without changing it.</summary>
    /// <returns>A fresh list of fresh settings.</returns>
    public List<ModifierSetting> ToChain() =>
        [.. Links.Select(link => new ModifierSetting
        {
            ModifierId = link.ModifierId,
            IsBypassed = link.IsBypassed,
            Values = new Dictionary<string, float>(link.Values, StringComparer.Ordinal)
        })];
}
