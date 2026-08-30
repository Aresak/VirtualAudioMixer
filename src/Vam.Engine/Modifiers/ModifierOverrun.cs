namespace Vam.Engine.Modifiers;

/// <summary>
/// A modifier that was switched out for taking more of a block than it may.
/// </summary>
/// <remarks>
/// Reported rather than silently applied. An operator who notices the denoise has stopped working
/// needs to be able to find out why, and "it was costing a third of every block" is an answer they
/// can act on — by removing it, or by running fewer channels.
/// </remarks>
/// <param name="ChannelIndex">Which strip.</param>
/// <param name="LinkIndex">Which link of its chain.</param>
/// <param name="ModifierName">What the modifier is called.</param>
/// <param name="FractionOfBlock">How much of a block it was averaging. One is the whole block.</param>
public readonly record struct ModifierOverrun(
    int ChannelIndex,
    int LinkIndex,
    string ModifierName,
    double FractionOfBlock);
