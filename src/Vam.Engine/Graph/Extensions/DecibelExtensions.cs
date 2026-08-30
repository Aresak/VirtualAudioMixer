namespace Vam.Engine.Graph.Extensions;

/// <summary>
/// Level conversion, for the control side.
/// </summary>
/// <remarks>
/// Its own namespace, imported where levels are being converted and nowhere else. None of this
/// belongs in the audio path: a decibel is what a person adjusts and a multiply is what the audio
/// thread wants, and the conversion happens once when a snapshot is built.
/// </remarks>
public static class DecibelExtensions
{
    /// <summary>Below this a fader is off rather than very quiet.</summary>
    /// <remarks>
    /// A fader dragged to its bottom should be silent, not −90 dB. Without a floor the graph would
    /// keep multiplying by a number that is inaudible but not zero, and denormals would then cost
    /// more than the audio is worth.
    /// </remarks>
    public const double SilenceThresholdDb = -100.0;

    /// <summary>Converts a level in decibels to a linear gain.</summary>
    /// <param name="decibels">The level.</param>
    /// <returns>The gain, or exactly zero at or below the silence floor.</returns>
    public static float ToLinearGain(this double decibels) =>
        decibels <= SilenceThresholdDb ? 0f : (float)Math.Pow(10.0, decibels / 20.0);

    /// <summary>Converts a linear gain to decibels.</summary>
    /// <param name="gain">The gain.</param>
    /// <returns>The level, or the silence floor for zero.</returns>
    public static double ToDecibels(this float gain) =>
        gain <= 0f ? SilenceThresholdDb : 20.0 * Math.Log10(gain);
}
