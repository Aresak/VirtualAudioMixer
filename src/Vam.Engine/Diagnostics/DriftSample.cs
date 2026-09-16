namespace Vam.Engine.Diagnostics;

/// <summary>One device's clock, at one moment. K2.</summary>
/// <param name="ChannelIndex">Which strip.</param>
/// <param name="Timestamp">When.</param>
/// <param name="DriftPpm">How far the device's rate is from nominal.</param>
/// <param name="FillPercentage">How full its ring was, from 0 to 100.</param>
/// <param name="CorrectionPpm">What the servo was doing about it.</param>
public readonly record struct DriftSample(
    int ChannelIndex,
    DateTimeOffset Timestamp,
    double DriftPpm,
    double FillPercentage,
    double CorrectionPpm);
