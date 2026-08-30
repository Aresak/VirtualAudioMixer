namespace Vam.Modifiers.Abstractions;

/// <summary>
/// One knob a modifier exposes.
/// </summary>
/// <remarks>
/// <para>
/// <b>Persistence is by <see cref="Id"/>, never by position.</b> The audio thread reads parameters
/// by ordinal because that is fast; a saved configuration reads them by identifier because that is
/// stable. Conflating the two means a modifier that reorders its parameters in version two silently
/// loads a saved threshold into its ratio.
/// </para>
/// </remarks>
/// <param name="Id">Stable identifier. Persisted, and never reused for a different meaning.</param>
/// <param name="Name">What the console calls it.</param>
/// <param name="Unit">What the number means, for display. Decibels, hertz, milliseconds, a ratio.</param>
/// <param name="Minimum">Lowest accepted value.</param>
/// <param name="Maximum">Highest accepted value.</param>
/// <param name="Default">Where it starts.</param>
/// <param name="Curve">How it is smoothed and how a control travels across it.</param>
public readonly record struct ParameterDescriptor(
    string Id,
    string Name,
    string Unit,
    float Minimum,
    float Maximum,
    float Default,
    ParameterCurve Curve)
{
    /// <summary>Brings a value inside the declared range.</summary>
    /// <param name="value">What was asked for.</param>
    /// <returns>What the modifier will actually see.</returns>
    public float Clamp(float value) => Math.Clamp(value, Minimum, Maximum);
}
