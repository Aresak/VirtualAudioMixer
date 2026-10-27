namespace Vam.Engine.Recording.Extensions;

/// <summary>The two checksums a FLAC frame carries.</summary>
public static class SpanExtensions
{
    // Polynomials from the FLAC specification: x^8 + x^2 + x + 1 and x^16 + x^15 + x^2 + 1.
    const int Crc8Polynomial = 0x07;
    const int Crc16Polynomial = 0x8005;

    static readonly byte[] Crc8Table = BuildCrc8Table();
    static readonly ushort[] Crc16Table = BuildCrc16Table();

    /// <summary>The CRC-8 a FLAC frame header ends with.</summary>
    /// <param name="bytes">The header up to the checksum.</param>
    /// <returns>The checksum.</returns>
    public static byte ToFlacCrc8(this ReadOnlySpan<byte> bytes)
    {
        byte crc = 0;

        foreach (byte value in bytes)
        {
            crc = Crc8Table[crc ^ value];
        }

        return crc;
    }

    /// <summary>The CRC-16 a FLAC frame ends with.</summary>
    /// <param name="bytes">The whole frame up to the checksum.</param>
    /// <returns>The checksum.</returns>
    public static ushort ToFlacCrc16(this ReadOnlySpan<byte> bytes)
    {
        ushort crc = 0;

        foreach (byte value in bytes)
        {
            crc = (ushort)((crc << 8) ^ Crc16Table[(crc >> 8) ^ value]);
        }

        return crc;
    }

    static byte[] BuildCrc8Table()
    {
        byte[] table = new byte[256];

        for (int index = 0; index < table.Length; index++)
        {
            int crc = index;

            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc & 0x80) != 0 ? (crc << 1) ^ Crc8Polynomial : crc << 1;
            }

            table[index] = (byte)crc;
        }

        return table;
    }

    static ushort[] BuildCrc16Table()
    {
        ushort[] table = new ushort[256];

        for (int index = 0; index < table.Length; index++)
        {
            int crc = index << 8;

            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc & 0x8000) != 0 ? (crc << 1) ^ Crc16Polynomial : crc << 1;
            }

            table[index] = (ushort)crc;
        }

        return table;
    }
}
