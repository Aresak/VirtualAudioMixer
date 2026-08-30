namespace Vam.Engine.Dsp;

/// <summary>
/// One biquad's coefficients, normalised so that a0 is one.
/// </summary>
/// <remarks>
/// <para>
/// Made by the factories in <see cref="BiquadDesign"/>, on the control thread. This is a plain
/// carrier so that the filter itself never computes a cosine.
/// </para>
/// <para>
/// <b>Q is clamped where these are designed, not here.</b> Linear interpolation between two sets of
/// coefficients is not unconditionally stable at high Q, and the engine ramps coefficients over a
/// few dozen frames when a parameter moves. Speech equalisation sits far inside the safe region, so
/// the clamp costs nothing anybody would ask for.
/// </para>
/// </remarks>
/// <param name="B0">Feed-forward, current sample.</param>
/// <param name="B1">Feed-forward, one back.</param>
/// <param name="B2">Feed-forward, two back.</param>
/// <param name="A1">Feedback, one back.</param>
/// <param name="A2">Feedback, two back.</param>
public readonly record struct BiquadCoefficients(float B0, float B1, float B2, float A1, float A2)
{
    /// <summary>A filter that does nothing.</summary>
    public static BiquadCoefficients Bypass => new(1f, 0f, 0f, 0f, 0f);
}
