namespace Vam.Protocol;

/// <summary>
/// One strip's meters for one frame.
/// </summary>
/// <remarks>
/// Peak and average both, because they answer different questions: peak is whether anything clipped
/// and average is how loud it sounded, and a console that shows only one of them leaves an operator
/// guessing about the other.
/// </remarks>
/// <param name="PeakDb">The loudest sample since the last frame.</param>
/// <param name="RmsDb">The average level since the last frame.</param>
/// <param name="GainReductionDb">What the chain is taking away. Zero or negative.</param>
/// <param name="AutomixShare">How much of the automixer's gain this strip is holding, from zero to one.</param>
/// <param name="Flags">Muted, soloed, faulted, ducked. See <see cref="MeterFlags"/>.</param>
public readonly record struct ChannelMeter(
    double PeakDb,
    double RmsDb,
    double GainReductionDb,
    double AutomixShare,
    byte Flags);
