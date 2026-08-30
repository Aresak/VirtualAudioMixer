namespace Vam.Engine.Metering;

/// <summary>
/// Where the audio thread leaves what the meters will be made of.
/// </summary>
/// <remarks>
/// <para>
/// <b>The audio thread writes a peak and a sum of squares, and nothing else.</b> No decibels, no
/// ballistics, no smoothing — those are logarithms and time constants, and they belong on the thread
/// that has time for them. This is also why peak, RMS and VU can be a setting the operator changes
/// without the engine knowing anything about it.
/// </para>
/// <para>
/// One writer per cell and one reader, so plain reads and writes are enough. The reader zeroes a
/// cell after taking it; a sample or two landing in the gap is invisible at twenty-five frames a
/// second and is not worth an interlocked operation per block.
/// </para>
/// </remarks>
public sealed class MeterCells
{
    readonly float[] peaks;
    readonly double[] sumsOfSquares;
    readonly long[] frames;

    /// <summary>Sizes the cells for a console.</summary>
    /// <param name="count">Strips or buses, whichever this set covers.</param>
    public MeterCells(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        peaks = new float[count];
        sumsOfSquares = new double[count];
        frames = new long[count];
    }

    /// <summary>How many cells there are.</summary>
    public int Count => peaks.Length;

    /// <summary>
    /// Accumulates one block. Audio thread, inside the audio path.
    /// </summary>
    /// <param name="index">Which cell.</param>
    /// <param name="peak">The loudest sample in this block.</param>
    /// <param name="sumOfSquares">The sum of the squares of this block's samples.</param>
    /// <param name="frameCount">Frames in the block.</param>
    public void Accumulate(int index, float peak, double sumOfSquares, int frameCount)
    {
        if (peak > peaks[index])
        {
            peaks[index] = peak;
        }

        sumsOfSquares[index] += sumOfSquares;
        frames[index] += frameCount;
    }

    /// <summary>
    /// Takes one cell and clears it. Control thread, at meter rate.
    /// </summary>
    /// <param name="index">Which cell.</param>
    /// <returns>The peak and the mean square since the last time it was taken.</returns>
    public (float Peak, double MeanSquare) Take(int index)
    {
        float peak = peaks[index];
        double sum = sumsOfSquares[index];
        long count = frames[index];

        peaks[index] = 0f;
        sumsOfSquares[index] = 0.0;
        frames[index] = 0;

        return (peak, count > 0 ? sum / count : 0.0);
    }

    /// <summary>Clears every cell.</summary>
    public void Clear()
    {
        Array.Clear(peaks);
        Array.Clear(sumsOfSquares);
        Array.Clear(frames);
    }
}
