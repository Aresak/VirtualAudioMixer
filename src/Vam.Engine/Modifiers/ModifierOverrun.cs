namespace Vam.Engine.Modifiers;

/// <summary>
/// A modifier that was switched out for taking more of a block than it may.
/// </summary>
/// <remarks>
/// Reported rather than silently applied. An operator who notices the denoise has stopped working
/// needs to be able to find out why, and "it was costing a third of every block" is an answer they
/// can act on — by removing it, or by running fewer channels.
/// </remarks>
/// <param name="ChannelIndex">Which strip, or which bus when <paramref name="IsBus"/> is set.</param>
/// <param name="LinkIndex">Which link of its chain.</param>
/// <param name="ModifierName">What the modifier is called.</param>
/// <param name="FractionOfBlock">How much of a block it was averaging. One is the whole block.</param>
/// <param name="IsBus">
/// Whether the index names a bus rather than a strip. A separate flag rather than a negative index:
/// an index that means two things depending on its sign is an index somebody eventually reads
/// wrongly, and this one ends up in a log line an engineer reads months later.
/// </param>
public readonly record struct ModifierOverrun(
    int ChannelIndex,
    int LinkIndex,
    string ModifierName,
    double FractionOfBlock,
    bool IsBus = false);
