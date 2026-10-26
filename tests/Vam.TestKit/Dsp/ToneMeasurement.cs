namespace Vam.TestKit.Dsp;

/// <summary>
/// What <see cref="ToneAnalyser"/> found in a captured buffer.
/// </summary>
/// <param name="FrequencyHz">The strongest frequency present, interpolated between bins.</param>
/// <param name="Peak">
/// The largest absolute sample in the window measured. Reported rather than asserted on: level
/// depends on gain staging and on whatever the cable does, and pitch does not.
/// </param>
/// <param name="WindowStartFrame">Where in the capture the measured window began.</param>
public readonly record struct ToneMeasurement(double FrequencyHz, float Peak, int WindowStartFrame);
