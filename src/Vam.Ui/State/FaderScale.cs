namespace Vam.Ui.State;

/// <summary>
/// The taper of a fader: where a decibel value sits along the travel.
/// </summary>
/// <remarks>
/// <para>
/// A fader is not linear in decibels and never has been. The useful resolution is around unity,
/// where an operator makes small corrections during a meeting, and almost none of it is needed
/// between forty and sixty decibels down, where everything is inaudible anyway. A linear control
/// spends half its travel on the difference between silent and slightly less silent.
/// </para>
/// <para>
/// The breakpoints below are the ones a physical console uses, and unity sits at the same place the
/// mockup draws its unity line. That is not a coincidence: the line is drawn where the number is.
/// </para>
/// <para>
/// Not in the audio path. The engine is sent decibels and converts them once, in the compiler.
/// </para>
/// </remarks>
public static class FaderScale
{
    /// <summary>Where unity sits along the travel, from the bottom.</summary>
    public const double UnityPosition = 0.72;

    /// <summary>The most a fader will add.</summary>
    public const double MaximumDb = 10.0;

    /// <summary>What the bottom of the travel means. Off, in every practical sense.</summary>
    public const double MinimumDb = -100.0;

    // Position, decibels. Straight lines between them, which is how a real fader's scale is printed.
    static readonly (double Position, double Decibels)[] Points =
    [
        (0.00, MinimumDb),
        (0.15, -50.0),
        (0.40, -20.0),
        (UnityPosition, 0.0),
        (1.00, MaximumDb)
    ];

    /// <summary>Turns a position along the travel into a level.</summary>
    /// <param name="position">Zero at the bottom, one at the top.</param>
    /// <returns>Decibels.</returns>
    public static double ToDecibels(double position)
    {
        double clamped = Math.Clamp(position, 0.0, 1.0);

        for (int index = 1; index < Points.Length; index++)
        {
            (double Position, double Decibels) upper = Points[index];

            if (clamped > upper.Position)
            {
                continue;
            }

            (double Position, double Decibels) lower = Points[index - 1];
            double span = upper.Position - lower.Position;
            double fraction = span <= 0 ? 0 : (clamped - lower.Position) / span;

            return lower.Decibels + (fraction * (upper.Decibels - lower.Decibels));
        }

        return MaximumDb;
    }

    /// <summary>Turns a level into a position along the travel.</summary>
    /// <param name="decibels">The level.</param>
    /// <returns>Zero at the bottom, one at the top.</returns>
    public static double ToPosition(double decibels)
    {
        double clamped = Math.Clamp(decibels, MinimumDb, MaximumDb);

        for (int index = 1; index < Points.Length; index++)
        {
            (double Position, double Decibels) upper = Points[index];

            if (clamped > upper.Decibels)
            {
                continue;
            }

            (double Position, double Decibels) lower = Points[index - 1];
            double span = upper.Decibels - lower.Decibels;
            double fraction = span <= 0 ? 0 : (clamped - lower.Decibels) / span;

            return lower.Position + (fraction * (upper.Position - lower.Position));
        }

        return 1.0;
    }

    /// <summary>How a level is written on a strip.</summary>
    /// <param name="decibels">The level.</param>
    /// <returns>The text, with the sign an operator expects to see on a fader.</returns>
    public static string Format(double decibels)
    {
        // A fader at the bottom reads off rather than minus a hundred. The number is true and the
        // word is what somebody glancing at sixteen strips actually needs.
        if (decibels <= MinimumDb)
        {
            return "−∞";
        }

        return decibels.ToString(
            decibels > 0 ? "+0.0" : "0.0",
            System.Globalization.CultureInfo.InvariantCulture);
    }
}
