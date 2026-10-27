namespace Vam.Engine.Tests.Recording;

/// <summary>
/// A FLAC decoder for the tests, written from the specification rather than from the encoder, so the
/// two do not share a misreading. Checks both CRCs on every frame and reads every subframe type
/// except LPC, which the encoder never writes.
/// </summary>
sealed class FlacTestDecoder
{
    readonly byte[] bytes;

    int bit;
    long frameNumber;

    FlacTestDecoder(byte[] bytes) => this.bytes = bytes;

    /// <summary>Reads one number in FLAC's extended UTF-8 coding from the start of some bytes.</summary>
    public static long ReadFrameNumber(byte[] bytes) => new FlacTestDecoder(bytes).ReadUtf8();

    /// <summary>Decodes a whole file into interleaved integers.</summary>
    public static (FlacStreamInfo Info, int[] Samples) Decode(string path)
    {
        FlacTestDecoder decoder = new(File.ReadAllBytes(path));

        return decoder.DecodeAll();
    }

    (FlacStreamInfo Info, int[] Samples) DecodeAll()
    {
        if (Read(32) != 0x664C6143)
        {
            throw new InvalidDataException("No fLaC marker.");
        }

        FlacStreamInfo? info = null;
        bool isLast = false;

        while (!isLast)
        {
            isLast = Read(1) == 1;
            long type = Read(7);
            int length = (int)Read(24);
            int end = (bit / 8) + length;

            if (type == 0)
            {
                info = ReadFlacStreamInfo();
            }

            bit = end * 8;
        }

        if (info is null)
        {
            throw new InvalidDataException("No STREAMINFO.");
        }

        List<int> samples = [];

        while (bit / 8 < bytes.Length)
        {
            ReadFrame(info, samples);
        }

        return (info, samples.ToArray());
    }

    FlacStreamInfo ReadFlacStreamInfo()
    {
        int minBlock = (int)Read(16);
        int maxBlock = (int)Read(16);

        Read(24);
        Read(24);

        int rate = (int)Read(20);
        int channels = (int)Read(3) + 1;
        int bits = (int)Read(5) + 1;
        long total = Read(36);
        byte[] md5 = new byte[16];

        for (int index = 0; index < 16; index++)
        {
            md5[index] = (byte)Read(8);
        }

        return new FlacStreamInfo(minBlock, maxBlock, rate, channels, bits, total, md5);
    }

    void ReadFrame(FlacStreamInfo info, List<int> output)
    {
        int start = bit / 8;

        if (Read(15) != 0x7FFC)
        {
            throw new InvalidDataException($"No frame sync at byte {start}.");
        }

        Read(1);

        int blockCode = (int)Read(4);
        int rateCode = (int)Read(4);
        int assignment = (int)Read(4);
        int sizeCode = (int)Read(3);

        Read(1);

        long number = ReadUtf8();

        if (number != frameNumber)
        {
            throw new InvalidDataException($"Frame {frameNumber} says it is frame {number}.");
        }

        frameNumber++;

        int blockSize = blockCode switch
        {
            1 => 192,
            >= 2 and <= 5 => 576 << (blockCode - 2),
            6 => (int)Read(8) + 1,
            7 => (int)Read(16) + 1,
            >= 8 => 256 << (blockCode - 8),
            _ => throw new InvalidDataException("Reserved block size.")
        };

        _ = rateCode switch
        {
            12 => Read(8),
            13 or 14 => Read(16),
            _ => 0
        };

        int sampleBits = sizeCode switch
        {
            0 => info.BitsPerSample,
            1 => 8,
            2 => 12,
            4 => 16,
            5 => 20,
            6 => 24,
            7 => 32,
            _ => throw new InvalidDataException("Reserved sample size.")
        };

        byte headerCrc = (byte)Read(8);

        if (Crc8(bytes.AsSpan(start, (bit / 8) - start - 1)) != headerCrc)
        {
            throw new InvalidDataException($"Header CRC mismatch at byte {start}.");
        }

        int channels = assignment < 8 ? assignment + 1 : 2;
        long[][] decoded = new long[channels][];

        for (int channel = 0; channel < channels; channel++)
        {
            // The side channel of a stereo decorrelation carries one extra bit.
            bool isSide = (assignment == 8 && channel == 1) || (assignment == 9 && channel == 0) || (assignment == 10 && channel == 1);

            decoded[channel] = ReadSubframe(blockSize, sampleBits + (isSide ? 1 : 0));
        }

        Undecorrelate(assignment, decoded);

        bit = (bit + 7) / 8 * 8;

        int footer = bit / 8;
        ushort frameCrc = (ushort)Read(16);

        if (Crc16(bytes.AsSpan(start, footer - start)) != frameCrc)
        {
            throw new InvalidDataException($"Frame CRC mismatch at byte {start}.");
        }

        for (int index = 0; index < blockSize; index++)
        {
            for (int channel = 0; channel < channels; channel++)
            {
                output.Add((int)decoded[channel][index]);
            }
        }
    }

    long[] ReadSubframe(int blockSize, int sampleBits)
    {
        Read(1);

        int type = (int)Read(6);
        int wasted = 0;

        if (Read(1) == 1)
        {
            wasted = 1;

            while (Read(1) == 0)
            {
                wasted++;
            }
        }

        int bits = sampleBits - wasted;
        long[] samples = new long[blockSize];

        if (type == 0)
        {
            Array.Fill(samples, ReadSigned(bits));
        }
        else if (type == 1)
        {
            for (int index = 0; index < blockSize; index++)
            {
                samples[index] = ReadSigned(bits);
            }
        }
        else if (type is >= 8 and <= 12)
        {
            ReadFixed(samples, type - 8, bits);
        }
        else
        {
            throw new InvalidDataException($"Subframe type {type} is not one this decoder reads.");
        }

        for (int index = 0; wasted > 0 && index < blockSize; index++)
        {
            samples[index] <<= wasted;
        }

        return samples;
    }

    void ReadFixed(long[] samples, int order, int bits)
    {
        for (int index = 0; index < order; index++)
        {
            samples[index] = ReadSigned(bits);
        }

        ReadResidual(samples, order);

        for (int index = order; index < samples.Length; index++)
        {
            long[] s = samples;
            int n = index;

            s[n] += order switch
            {
                0 => 0,
                1 => s[n - 1],
                2 => (2 * s[n - 1]) - s[n - 2],
                3 => (3 * s[n - 1]) - (3 * s[n - 2]) + s[n - 3],
                _ => (4 * s[n - 1]) - (6 * s[n - 2]) + (4 * s[n - 3]) - s[n - 4]
            };
        }
    }

    void ReadResidual(long[] target, int order)
    {
        int method = (int)Read(2);
        int parameterBits = method switch
        {
            0 => 4,
            1 => 5,
            _ => throw new InvalidDataException("Reserved residual coding method.")
        };
        int escape = (1 << parameterBits) - 1;
        int partitionOrder = (int)Read(4);
        int partitions = 1 << partitionOrder;
        int size = target.Length >> partitionOrder;
        int at = order;

        for (int partition = 0; partition < partitions; partition++)
        {
            int count = partition == 0 ? size - order : size;
            int parameter = (int)Read(parameterBits);

            if (parameter == escape)
            {
                int raw = (int)Read(5);

                for (int index = 0; index < count; index++)
                {
                    target[at++] = raw == 0 ? 0 : ReadSigned(raw);
                }

                continue;
            }

            for (int index = 0; index < count; index++)
            {
                long quotient = 0;

                while (Read(1) == 0)
                {
                    quotient++;
                }

                long folded = (quotient << parameter) | (parameter > 0 ? Read(parameter) : 0);

                target[at++] = (folded >> 1) ^ -(folded & 1);
            }
        }
    }

    static void Undecorrelate(int assignment, long[][] channels)
    {
        long[] first = channels[0];
        long[] second = channels.Length > 1 ? channels[1] : first;

        for (int index = 0; assignment >= 8 && index < first.Length; index++)
        {
            switch (assignment)
            {
                case 8:
                    second[index] = first[index] - second[index];
                    break;
                case 9:
                    first[index] = first[index] + second[index];
                    break;
                default:
                    long mid = (first[index] << 1) | (second[index] & 1);
                    first[index] = (mid + second[index]) >> 1;
                    second[index] = (mid - second[index]) >> 1;
                    break;
            }
        }
    }

    long ReadUtf8()
    {
        long lead = Read(8);
        int extra = 0;

        while (extra < 8 && (lead & (0x80 >> extra)) != 0)
        {
            extra++;
        }

        if (extra == 0)
        {
            return lead;
        }

        long value = lead & (0x7F >> extra);

        for (int index = 1; index < extra; index++)
        {
            long next = Read(8);

            if ((next & 0xC0) != 0x80)
            {
                throw new InvalidDataException("Malformed frame number.");
            }

            value = (value << 6) | (next & 0x3F);
        }

        return value;
    }

    long ReadSigned(int count)
    {
        long value = Read(count);

        return (value << (64 - count)) >> (64 - count);
    }

    long Read(int count)
    {
        long value = 0;

        for (int index = 0; index < count; index++)
        {
            int current = bytes[bit >> 3] >> (7 - (bit & 7)) & 1;

            value = (value << 1) | (long)current;
            bit++;
        }

        return value;
    }

    static byte Crc8(ReadOnlySpan<byte> data)
    {
        int crc = 0;

        foreach (byte value in data)
        {
            crc ^= value;

            for (int index = 0; index < 8; index++)
            {
                crc = (crc & 0x80) != 0 ? ((crc << 1) ^ 0x07) & 0xFF : (crc << 1) & 0xFF;
            }
        }

        return (byte)crc;
    }

    static ushort Crc16(ReadOnlySpan<byte> data)
    {
        int crc = 0;

        foreach (byte value in data)
        {
            crc ^= value << 8;

            for (int index = 0; index < 8; index++)
            {
                crc = (crc & 0x8000) != 0 ? ((crc << 1) ^ 0x8005) & 0xFFFF : (crc << 1) & 0xFFFF;
            }
        }

        return (ushort)crc;
    }
}
