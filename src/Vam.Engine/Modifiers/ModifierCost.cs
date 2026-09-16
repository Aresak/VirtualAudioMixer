namespace Vam.Engine.Modifiers;

/// <summary>
/// What one modifier is costing, measured on the audio thread without disturbing it.
/// </summary>
/// <remarks>
/// <para>
/// A timestamp difference accumulated into a pre-allocated slot, and nothing more. There is exactly
/// one writer — the audio thread — so the increments need no synchronisation, and the whole
/// measurement is two calls to a counter and an add. Anything fancier belongs off-thread.
/// </para>
/// <para>
/// The average is exponential rather than a running total, because what a budget guard needs to
/// know is what this modifier is costing <i>now</i>, not what it averaged over an hour that included
/// the moment the session started.
/// </para>
/// </remarks>
public struct ModifierCost
{
    /// <summary>
    /// How much of the past the average remembers. A sixteenth per block at 2.5 ms is a time
    /// constant of about forty milliseconds — fast enough to catch a modifier that has started
    /// misbehaving, slow enough not to trip on one unlucky block.
    /// </summary>
    const double Weight = 1.0 / 16.0;

    double averageTicks;

    /// <summary>Blocks measured.</summary>
    public long BlockCount { get; private set; }

    /// <summary>The worst single block seen, in timer ticks.</summary>
    public long PeakTicks { get; private set; }

    /// <summary>The recent average, in timer ticks.</summary>
    public readonly double AverageTicks => averageTicks;

    /// <summary>Records one block's cost.</summary>
    /// <param name="ticks">Timer ticks the modifier took.</param>
    public void Record(long ticks)
    {
        averageTicks += (ticks - averageTicks) * Weight;
        BlockCount++;

        if (ticks > PeakTicks)
        {
            PeakTicks = ticks;
        }
    }

    /// <summary>The recent average as a fraction of the time a block has to spare.</summary>
    /// <param name="blockTicks">Timer ticks one block of audio lasts.</param>
    /// <returns>Zero for free, one for the whole budget, above one for late.</returns>
    public readonly double FractionOfBudget(long blockTicks) =>
        blockTicks <= 0 ? 0.0 : averageTicks / blockTicks;

    /// <summary>Forgets everything measured.</summary>
    public void Clear()
    {
        averageTicks = 0.0;
        BlockCount = 0;
        PeakTicks = 0;
    }
}
