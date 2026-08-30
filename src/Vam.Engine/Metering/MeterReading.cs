namespace Vam.Engine.Metering;

/// <summary>
/// One strip's or one bus's meters, after the ballistics have been applied.
/// </summary>
/// <remarks>
/// Peak and average both. Peak answers whether anything clipped and average answers how loud it
/// sounded, and a console showing only one of them leaves the operator guessing about the other.
/// </remarks>
/// <param name="PeakDb">The loudest sample since the previous frame, with the peak hold applied.</param>
/// <param name="RmsDb">The average level since the previous frame, smoothed.</param>
/// <param name="GainReductionDb">What the chain and the automixer are taking away. Zero or negative.</param>
/// <param name="AutomixShare">How much of the automixer's gain this strip is holding.</param>
/// <param name="IsDucked">Whether the automixer is holding it at or near the depth floor.</param>
public readonly record struct MeterReading(
    double PeakDb,
    double RmsDb,
    double GainReductionDb,
    double AutomixShare,
    bool IsDucked);
