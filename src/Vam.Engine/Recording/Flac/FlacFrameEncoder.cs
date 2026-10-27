using System.Numerics;
using Vam.Engine.Recording.Extensions;

namespace Vam.Engine.Recording.Flac;

/// <summary>
/// Encodes one FLAC frame of twenty-four bit audio: each channel as constant, verbatim or a fixed
/// polynomial predictor with Rice-coded residuals, whichever is smallest.
/// </summary>
/// <remarks>
/// <para>
/// Fixed predictors and no LPC, and no stereo decorrelation. That gives up some compression against
/// the reference encoder in exchange for an encoder short enough to read against the specification,
/// which matters more for a file that is the record of a meeting than the last few percent of disk.
/// </para>
/// <para>
/// Verbatim is the fallback for any channel the predictor makes larger, so a frame is never more than
/// a few bytes bigger than the samples uncompressed, and the buffer is sized for that once.
/// </para>
/// </remarks>
public sealed class FlacFrameEncoder
{
    /// <summary>Frames in every block but the last. 4096 is the reference encoder's default.</summary>
    public const int BlockFrames = 4096;

    /// <summary>Bits per sample.</summary>
    public const int BitsPerSample = 24;

    /// <summary>The most channels a FLAC stream can carry.</summary>
    public const int MaxChannels = 8;

    const int MaxFixedOrder = 4;
    const int MaxPartitionOrder = 8;

    // RICE2 has a five-bit parameter and 31 is its escape code.
    const int MaxRiceParameter = 30;
    const int RiceParameterBits = 5;
    const int PartitionOrderBits = 4;
    const int ResidualMethodBits = 2;
    const uint Rice2Method = 1;

    const int SubframeHeaderBits = 8;
    const uint ConstantType = 0;
    const uint VerbatimType = 1;
    const uint FixedTypeBase = 8;

    const uint FrameSync = 0xFFF8;
    const uint NominalBlockSizeCode = 12;
    const uint ExplicitBlockSizeCode = 7;
    const uint SampleSizeCode = 6;

    readonly FlacBitWriter bits;
    readonly int[] residual = new int[BlockFrames];
    readonly int[] parameters = new int[1 << MaxPartitionOrder];
    readonly ulong[] sums = new ulong[1 << MaxPartitionOrder];
    readonly int channelCount;
    readonly uint sampleRateCode;

    /// <summary>Prepares an encoder for one stream.</summary>
    /// <param name="sampleRate">The stream's rate.</param>
    /// <param name="channelCount">Its channels, one to eight.</param>
    public FlacFrameEncoder(int sampleRate, int channelCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(channelCount, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(channelCount, MaxChannels);

        this.channelCount = channelCount;

        sampleRateCode = SampleRateCodeFor(sampleRate);
        bits = new FlacBitWriter(MaxFrameBytes(channelCount));
    }

    /// <summary>The largest frame this encoder can produce for a channel count.</summary>
    /// <param name="channelCount">Channels.</param>
    /// <returns>Bytes.</returns>
    public static int MaxFrameBytes(int channelCount) => 32 + (channelCount * ((BlockFrames * BitsPerSample / 8) + 8));

    /// <summary>Encodes one frame.</summary>
    /// <param name="channels">One buffer per channel, at least <paramref name="frameCount"/> long.</param>
    /// <param name="frameCount">Frames in this block; <see cref="BlockFrames"/> for all but the last.</param>
    /// <param name="frameNumber">Which frame this is, from zero.</param>
    /// <returns>The frame's bytes, valid until the next call.</returns>
    public ReadOnlySpan<byte> Encode(int[][] channels, int frameCount, long frameNumber)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(frameCount, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(frameCount, BlockFrames);

        bits.Reset();

        WriteHeader(frameCount, frameNumber);

        for (int channel = 0; channel < channelCount; channel++)
        {
            WriteSubframe(channels[channel].AsSpan(0, frameCount));
        }

        bits.AlignToByte();
        bits.WriteBits(bits.Written.ToFlacCrc16(), 16);

        return bits.Written;
    }

    void WriteHeader(int frameCount, long frameNumber)
    {
        bool isNominal = frameCount == BlockFrames;

        bits.WriteBits(FrameSync, 16);
        bits.WriteBits(isNominal ? NominalBlockSizeCode : ExplicitBlockSizeCode, 4);
        bits.WriteBits(sampleRateCode, 4);

        // Independent channels: the assignment is the channel count less one.
        bits.WriteBits((uint)(channelCount - 1), 4);
        bits.WriteBits(SampleSizeCode, 3);
        bits.WriteBits(0, 1);
        bits.WriteUtf8(frameNumber);

        if (!isNominal)
        {
            bits.WriteBits((uint)(frameCount - 1), 16);
        }

        bits.WriteBits(bits.Written.ToFlacCrc8(), 8);
    }

    void WriteSubframe(ReadOnlySpan<int> samples)
    {
        if (IsConstant(samples))
        {
            bits.WriteBits(ConstantType << 1, SubframeHeaderBits);
            bits.WriteSigned(samples[0], BitsPerSample);

            return;
        }

        int order = BestOrder(samples);

        FillResidual(samples, order);

        int partitionOrder = BestPartitionOrder(samples.Length, order);
        long fixedBits = ((long)order * BitsPerSample) + ResidualMethodBits + PartitionOrderBits +
            ChooseParameters(samples.Length, order, partitionOrder);

        if (fixedBits >= (long)samples.Length * BitsPerSample)
        {
            WriteVerbatim(samples);

            return;
        }

        bits.WriteBits((FixedTypeBase | (uint)order) << 1, SubframeHeaderBits);

        for (int index = 0; index < order; index++)
        {
            bits.WriteSigned(samples[index], BitsPerSample);
        }

        WriteResidual(samples.Length, order, partitionOrder);
    }

    void WriteVerbatim(ReadOnlySpan<int> samples)
    {
        bits.WriteBits(VerbatimType << 1, SubframeHeaderBits);

        foreach (int sample in samples)
        {
            bits.WriteSigned(sample, BitsPerSample);
        }
    }

    void WriteResidual(int frameCount, int order, int partitionOrder)
    {
        bits.WriteBits(Rice2Method, ResidualMethodBits);
        bits.WriteBits((uint)partitionOrder, PartitionOrderBits);

        int size = frameCount >> partitionOrder;

        for (int partition = 0; partition < 1 << partitionOrder; partition++)
        {
            int parameter = parameters[partition];

            bits.WriteBits((uint)parameter, RiceParameterBits);

            for (int index = partition == 0 ? order : partition * size; index < (partition + 1) * size; index++)
            {
                bits.WriteRice(residual[index], parameter);
            }
        }
    }

    // The order whose residual is smallest in total, measured over the same samples for each.
    int BestOrder(ReadOnlySpan<int> samples)
    {
        int maxOrder = Math.Min(MaxFixedOrder, samples.Length - 1);
        int best = 0;
        long bestTotal = long.MaxValue;

        for (int order = 0; order <= maxOrder; order++)
        {
            long total = 0;

            for (int index = maxOrder; index < samples.Length; index++)
            {
                total += Math.Abs(ResidualAt(samples, order, index));
            }

            if (total < bestTotal)
            {
                bestTotal = total;
                best = order;
            }
        }

        return best;
    }

    void FillResidual(ReadOnlySpan<int> samples, int order)
    {
        for (int index = order; index < samples.Length; index++)
        {
            residual[index] = (int)ResidualAt(samples, order, index);
        }
    }

    // Estimated from per-partition sums at the finest order, merged pairwise for each coarser one.
    int BestPartitionOrder(int frameCount, int order)
    {
        int finest = 0;

        while (finest < MaxPartitionOrder &&
            frameCount % (1 << (finest + 1)) == 0 &&
            frameCount >> (finest + 1) > order)
        {
            finest++;
        }

        int size = frameCount >> finest;

        for (int partition = 0; partition < 1 << finest; partition++)
        {
            sums[partition] = SumFolded(partition == 0 ? order : partition * size, (partition + 1) * size);
        }

        int best = finest;
        long bestBits = long.MaxValue;

        for (int partitionOrder = finest; partitionOrder >= 0; partitionOrder--)
        {
            long estimate = EstimateBits(partitionOrder, frameCount >> partitionOrder, order);

            if (estimate <= bestBits)
            {
                bestBits = estimate;
                best = partitionOrder;
            }

            for (int partition = 0; partitionOrder > 0 && partition < 1 << (partitionOrder - 1); partition++)
            {
                sums[partition] = sums[2 * partition] + sums[(2 * partition) + 1];
            }
        }

        return best;
    }

    long EstimateBits(int partitionOrder, int size, int order)
    {
        long total = 0;

        for (int partition = 0; partition < 1 << partitionOrder; partition++)
        {
            int count = partition == 0 ? size - order : size;
            int parameter = ParameterFor(sums[partition], count);

            total += RiceParameterBits + ((long)count * (parameter + 1)) + (long)(sums[partition] >> parameter);
        }

        return total;
    }

    // Exact, because whether verbatim is smaller is decided on it and the buffer is sized on that.
    long ChooseParameters(int frameCount, int order, int partitionOrder)
    {
        int size = frameCount >> partitionOrder;
        long total = 0;

        for (int partition = 0; partition < 1 << partitionOrder; partition++)
        {
            int start = partition == 0 ? order : partition * size;
            int end = (partition + 1) * size;
            int parameter = ParameterFor(SumFolded(start, end), end - start);

            parameters[partition] = parameter;
            total += RiceParameterBits;

            for (int index = start; index < end; index++)
            {
                total += 1 + parameter + (Fold(residual[index]) >> parameter);
            }
        }

        return total;
    }

    ulong SumFolded(int start, int end)
    {
        ulong sum = 0;

        for (int index = start; index < end; index++)
        {
            sum += Fold(residual[index]);
        }

        return sum;
    }

    static int ParameterFor(ulong sum, int count)
    {
        ulong mean = count > 0 ? sum / (ulong)count : 0;

        return mean == 0 ? 0 : Math.Min(BitOperations.Log2(mean), MaxRiceParameter);
    }

    static uint Fold(int value) => (uint)((value << 1) ^ (value >> 31));

    static bool IsConstant(ReadOnlySpan<int> samples)
    {
        for (int index = 1; index < samples.Length; index++)
        {
            if (samples[index] != samples[0])
            {
                return false;
            }
        }

        return true;
    }

    // The fixed predictors of the FLAC specification, orders zero to four.
    static long ResidualAt(ReadOnlySpan<int> s, int order, int index) => order switch
    {
        0 => s[index],
        1 => s[index] - (long)s[index - 1],
        2 => s[index] - (2L * s[index - 1]) + s[index - 2],
        3 => s[index] - (3L * s[index - 1]) + (3L * s[index - 2]) - s[index - 3],
        _ => s[index] - (4L * s[index - 1]) + (6L * s[index - 2]) - (4L * s[index - 3]) + s[index - 4]
    };

    // Rates the frame header can name; anything else says "see STREAMINFO".
    static uint SampleRateCodeFor(int sampleRate) => sampleRate switch
    {
        44100 => 9,
        48000 => 10,
        96000 => 11,
        _ => 0
    };
}
