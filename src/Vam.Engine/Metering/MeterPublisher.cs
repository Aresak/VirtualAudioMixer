using Vam.Engine.Automix;

namespace Vam.Engine.Metering;

/// <summary>
/// Turns what the audio thread left behind into what a console draws. Off thread, at meter rate.
/// </summary>
/// <remarks>
/// <para>
/// This is where the logarithms and the time constants live, and it is outside the audio path
/// entirely — it reads counters on a timer and allocates freely, which is exactly what
/// <c>docs/audio-path.md</c> says about it.
/// </para>
/// <para>
/// <b>Peak holds and average falls.</b> A peak that decayed as fast as the signal would be
/// unreadable at twenty-five frames a second; one that holds and then falls slowly is how every
/// meter anybody has used behaves, and an operator reads it without thinking about it.
/// </para>
/// </remarks>
public sealed class MeterPublisher
{
    /// <summary>Frames a second. Fast enough to feel live, slow enough not to be the traffic.</summary>
    public const int FramesPerSecond = 25;

    /// <summary>Decibels a held peak falls per second once it starts falling.</summary>
    const double PeakFallDbPerSecond = 20.0;

    /// <summary>How long a peak is held before it starts falling.</summary>
    const double PeakHoldSeconds = 1.0;

    /// <summary>The level a meter shows when there is nothing there.</summary>
    const double SilenceDb = -120.0;

    /// <summary>How much of the previous average survives into the next frame.</summary>
    const double AverageSmoothing = 0.5;

    readonly MeterCells channelCells;
    readonly MeterCells busCells;
    readonly MeterReading[] channels;
    readonly MeterReading[] buses;
    readonly double[] heldPeaks;
    readonly double[] holdRemaining;
    readonly double[] smoothedRms;

    /// <summary>Prepares a publisher for a console's meters.</summary>
    /// <param name="channelCells">Where the strips leave their numbers.</param>
    /// <param name="busCells">Where the buses leave theirs.</param>
    public MeterPublisher(MeterCells channelCells, MeterCells busCells)
    {
        ArgumentNullException.ThrowIfNull(channelCells);
        ArgumentNullException.ThrowIfNull(busCells);

        this.channelCells = channelCells;
        this.busCells = busCells;

        channels = new MeterReading[channelCells.Count];
        buses = new MeterReading[busCells.Count];
        heldPeaks = new double[channelCells.Count + busCells.Count];
        holdRemaining = new double[heldPeaks.Length];
        smoothedRms = new double[heldPeaks.Length];

        Array.Fill(heldPeaks, SilenceDb);
        Array.Fill(smoothedRms, SilenceDb);
    }

    /// <summary>The strips, as of the last frame.</summary>
    public ReadOnlySpan<MeterReading> Channels => channels;

    /// <summary>The buses, as of the last frame.</summary>
    public ReadOnlySpan<MeterReading> Buses => buses;

    /// <summary>
    /// Builds one frame's worth of readings.
    /// </summary>
    /// <param name="elapsed">Time since the previous frame.</param>
    /// <param name="automix">What the automixer is doing, for the share and the ducked flag.</param>
    /// <param name="depthDb">The automixer's floor, for deciding what counts as ducked.</param>
    public void Publish(TimeSpan elapsed, AutomixState? automix, double depthDb)
    {
        for (int index = 0; index < channels.Length; index++)
        {
            (double peakDb, double rmsDb) = Read(channelCells, index, index, elapsed);

            double share = automix is not null && index < automix.Shares.Length ? automix.Shares[index] : 0.0;
            double gainDb = automix is not null && index < automix.GainsDb.Length ? automix.GainsDb[index] : 0.0;

            // Ducked rather than merely quiet. An operator looking at a wall of meters needs to see
            // which microphones the automixer is holding down, and a grey meter says that where a
            // low one does not.
            bool isDucked = gainDb <= depthDb + 1.0;

            channels[index] = new MeterReading(peakDb, rmsDb, gainDb, share, isDucked);
        }

        for (int index = 0; index < buses.Length; index++)
        {
            (double peakDb, double rmsDb) = Read(busCells, index, channels.Length + index, elapsed);

            buses[index] = new MeterReading(peakDb, rmsDb, 0.0, 0.0, false);
        }
    }

    static double ToDecibels(double magnitude) =>
        magnitude <= 0.0 ? SilenceDb : Math.Max(20.0 * Math.Log10(magnitude), SilenceDb);

    (double PeakDb, double RmsDb) Read(MeterCells cells, int index, int holdIndex, TimeSpan elapsed)
    {
        (float peak, double meanSquare) = cells.Take(index);

        double peakDb = ToDecibels(peak);
        double rmsDb = ToDecibels(Math.Sqrt(meanSquare));

        if (peakDb >= heldPeaks[holdIndex])
        {
            heldPeaks[holdIndex] = peakDb;
            holdRemaining[holdIndex] = PeakHoldSeconds;
        }
        else
        {
            holdRemaining[holdIndex] -= elapsed.TotalSeconds;

            if (holdRemaining[holdIndex] <= 0.0)
            {
                heldPeaks[holdIndex] = Math.Max(
                    heldPeaks[holdIndex] - (PeakFallDbPerSecond * elapsed.TotalSeconds),
                    peakDb);
            }
        }

        smoothedRms[holdIndex] = (smoothedRms[holdIndex] * AverageSmoothing) + (rmsDb * (1.0 - AverageSmoothing));

        return (heldPeaks[holdIndex], Math.Max(smoothedRms[holdIndex], SilenceDb));
    }
}
