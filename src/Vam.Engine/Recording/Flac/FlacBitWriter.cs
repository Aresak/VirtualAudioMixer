namespace Vam.Engine.Recording.Flac;

/// <summary>Packs bits most significant first into a buffer sized once, the way FLAC lays them out.</summary>
public sealed class FlacBitWriter
{
    const int BitsPerByte = 8;
    const int MaxBitsPerWrite = 32;

    readonly byte[] buffer;

    ulong accumulator;
    int pendingBits;
    int length;

    /// <summary>Creates a writer that can hold a given number of bytes.</summary>
    /// <param name="capacityBytes">The most it will ever be asked to hold.</param>
    public FlacBitWriter(int capacityBytes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacityBytes, 1);

        buffer = new byte[capacityBytes];
    }

    /// <summary>Whole bytes written so far. Complete only once the writer is byte-aligned.</summary>
    public ReadOnlySpan<byte> Written => buffer.AsSpan(0, length);

    /// <summary>Empties the writer.</summary>
    public void Reset()
    {
        accumulator = 0;
        pendingBits = 0;
        length = 0;
    }

    /// <summary>Writes the low bits of a value.</summary>
    /// <param name="value">The value.</param>
    /// <param name="count">How many of its low bits, up to thirty-two.</param>
    public void WriteBits(uint value, int count)
    {
        accumulator = (accumulator << count) | (value & ((1UL << count) - 1));
        pendingBits += count;

        while (pendingBits >= BitsPerByte)
        {
            pendingBits -= BitsPerByte;
            buffer[length++] = (byte)(accumulator >> pendingBits);
        }
    }

    /// <summary>Writes a signed value in two's complement.</summary>
    /// <param name="value">The value.</param>
    /// <param name="count">Bits to write it in.</param>
    public void WriteSigned(int value, int count) => WriteBits((uint)value, count);

    /// <summary>Writes a value as Rice code with a given parameter, zig-zag folded first.</summary>
    /// <param name="residual">The signed value.</param>
    /// <param name="parameter">The Rice parameter.</param>
    public void WriteRice(int residual, int parameter)
    {
        uint folded = (uint)((residual << 1) ^ (residual >> 31));
        uint quotient = folded >> parameter;

        while (quotient >= MaxBitsPerWrite)
        {
            WriteBits(0, MaxBitsPerWrite);
            quotient -= MaxBitsPerWrite;
        }

        // The quotient's zeros and the stop bit together.
        WriteBits(1, (int)quotient + 1);

        if (parameter > 0)
        {
            WriteBits(folded, parameter);
        }
    }

    /// <summary>Writes a frame or sample number in FLAC's extended UTF-8 coding.</summary>
    /// <param name="value">Up to thirty-six bits.</param>
    public void WriteUtf8(long value)
    {
        const int MaxBytes = 7;
        const int BitsPerContinuation = 6;

        if (value < 0x80)
        {
            WriteBits((uint)value, BitsPerByte);

            return;
        }

        int count = 2;

        // A sequence of n bytes carries 5n + 1 bits.
        while (count < MaxBytes && value >= 1L << ((5 * count) + 1))
        {
            count++;
        }

        uint lead = (uint)(0xFF00 >> count) & 0xFF;

        WriteBits(lead | (uint)(value >> (BitsPerContinuation * (count - 1))), BitsPerByte);

        for (int index = count - 2; index >= 0; index--)
        {
            WriteBits(0x80 | (uint)((value >> (BitsPerContinuation * index)) & 0x3F), BitsPerByte);
        }
    }

    /// <summary>Pads with zeros to the next byte boundary.</summary>
    public void AlignToByte()
    {
        if (pendingBits > 0)
        {
            WriteBits(0, BitsPerByte - pendingBits);
        }
    }
}
