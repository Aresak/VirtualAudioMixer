namespace Vam.Engine.Dsp;

/// <summary>
/// An in-place radix-2 fast Fourier transform, for the one thing in this engine that needs one.
/// </summary>
/// <remarks>
/// <para>
/// Written rather than taken from a package for the same reason the SIMD helpers are: it is a
/// hundred lines of textbook arithmetic, and a package version is a liability in five years for
/// something that has not changed since 1965.
/// </para>
/// <para>
/// Everything is allocated when the transform is constructed — the twiddle factors, the bit-reversal
/// table and the working buffers. Inside the audio path, nothing here allocates.
/// </para>
/// </remarks>
public sealed class RealFft
{
    readonly int size;
    readonly float[] cosines;
    readonly float[] sines;
    readonly int[] reversed;

    /// <summary>Builds the tables for one transform size.</summary>
    /// <param name="size">Points. Must be a power of two.</param>
    public RealFft(int size)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 2);

        if ((size & (size - 1)) != 0)
        {
            throw new ArgumentException("The transform size must be a power of two.", nameof(size));
        }

        this.size = size;

        cosines = new float[size / 2];
        sines = new float[size / 2];
        reversed = new int[size];

        for (int index = 0; index < size / 2; index++)
        {
            double angle = -2.0 * Math.PI * index / size;

            cosines[index] = (float)Math.Cos(angle);
            sines[index] = (float)Math.Sin(angle);
        }

        int bits = System.Numerics.BitOperations.TrailingZeroCount(size);

        for (int index = 0; index < size; index++)
        {
            reversed[index] = Reverse(index, bits);
        }
    }

    /// <summary>Points in the transform.</summary>
    public int Size => size;

    /// <summary>Distinct frequency bins a real transform produces, including both ends.</summary>
    public int BinCount => (size / 2) + 1;

    /// <summary>
    /// Transforms in place. Forward when <paramref name="isInverse"/> is false.
    /// </summary>
    /// <remarks>
    /// The inverse is the forward transform with the sign of the imaginary part flipped on the way
    /// in and out, and a division by the size. Doing it that way means one set of twiddle factors
    /// rather than two.
    /// </remarks>
    /// <param name="real">Real parts, <see cref="Size"/> long.</param>
    /// <param name="imaginary">Imaginary parts, <see cref="Size"/> long.</param>
    /// <param name="isInverse">Whether to run it backwards.</param>
    public void Transform(Span<float> real, Span<float> imaginary, bool isInverse)
    {
        if (isInverse)
        {
            Conjugate(imaginary);
        }

        Reorder(real, imaginary);
        Butterflies(real, imaginary);

        if (!isInverse)
        {
            return;
        }

        Conjugate(imaginary);

        float scale = 1f / size;

        for (int index = 0; index < size; index++)
        {
            real[index] *= scale;
            imaginary[index] *= scale;
        }
    }

    static int Reverse(int value, int bits)
    {
        int result = 0;

        for (int bit = 0; bit < bits; bit++)
        {
            result = (result << 1) | ((value >> bit) & 1);
        }

        return result;
    }

    static void Conjugate(Span<float> imaginary)
    {
        for (int index = 0; index < imaginary.Length; index++)
        {
            imaginary[index] = -imaginary[index];
        }
    }

    void Reorder(Span<float> real, Span<float> imaginary)
    {
        for (int index = 0; index < size; index++)
        {
            int target = reversed[index];

            if (target <= index)
            {
                continue;
            }

            (real[index], real[target]) = (real[target], real[index]);
            (imaginary[index], imaginary[target]) = (imaginary[target], imaginary[index]);
        }
    }

    void Butterflies(Span<float> real, Span<float> imaginary)
    {
        // Cooley-Tukey, decimation in time. The symbols follow the published form; renaming them
        // would make the code harder to check against any textbook, not easier.
        for (int span = 1; span < size; span <<= 1)
        {
            int step = size / (span * 2);

            for (int start = 0; start < size; start += span * 2)
            {
                for (int offset = 0; offset < span; offset++)
                {
                    int twiddle = offset * step;
                    int upper = start + offset;
                    int lower = upper + span;

                    float cos = cosines[twiddle];
                    float sin = sines[twiddle];

                    float realPart = (real[lower] * cos) - (imaginary[lower] * sin);
                    float imaginaryPart = (real[lower] * sin) + (imaginary[lower] * cos);

                    real[lower] = real[upper] - realPart;
                    imaginary[lower] = imaginary[upper] - imaginaryPart;
                    real[upper] += realPart;
                    imaginary[upper] += imaginaryPart;
                }
            }
        }
    }
}
