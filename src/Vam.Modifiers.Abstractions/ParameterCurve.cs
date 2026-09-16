namespace Vam.Modifiers.Abstractions;

/// <summary>How a parameter should be smoothed and how a control should travel across its range.</summary>
/// <remarks>
/// The host smooths, so the curve is declared here and applied there. A modifier that smoothed its
/// own parameters would be one more place for the same bug, and a third-party one would be a place
/// nobody could fix.
/// </remarks>
public enum ParameterCurve
{
    /// <summary>Smoothed as a plain number. Ratios, times, counts.</summary>
    Linear,

    /// <summary>
    /// Smoothed as a gain rather than as decibels. Interpolating decibels through silence means
    /// interpolating through minus infinity, which is not a number the arithmetic survives.
    /// </summary>
    Decibel,

    /// <summary>Smoothed in the log domain. Frequencies, where a fixed number of hertz means
    /// something completely different at eighty and at eight thousand.</summary>
    Logarithmic,

    /// <summary>
    /// Not smoothed at all. A filter order, a mode, anything where the values between two settings
    /// are not settings — sliding from a twelve to a twenty-four decibel slope through eighteen
    /// would be meaningless rather than gradual.
    /// </summary>
    Stepped
}
