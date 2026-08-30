using System.Buffers.Binary;

namespace Vam.Protocol;

/// <summary>
/// The packed binary layout of a meter frame. G3.
/// </summary>
/// <remarks>
/// <para>
/// <b>Fixed and small on purpose.</b> Sixteen channels and six buses at twenty-five frames a second
/// is a hundred and fifty messages a second before anything else happens, and a protobuf message per
/// meter would be most of the traffic on the link. Ten bytes per strip and four per bus is a frame
/// of a couple of hundred bytes.
/// </para>
/// <para>
/// Levels are decibels in hundredths, as signed sixteen-bit integers. That covers plus or minus
/// three hundred decibels at a resolution far finer than any meter draws, and it avoids sending
/// floats for numbers that are about to be rounded to a pixel anyway.
/// </para>
/// <para>
/// Part of the Apache-licensed protocol rather than the engine, because a third-party console has to
/// be able to decode this without inheriting copyleft.
/// </para>
/// </remarks>
public static class MeterFrameCodec
{
    /// <summary>Bytes each strip takes.</summary>
    public const int ChannelBytes = 10;

    /// <summary>Bytes each bus takes.</summary>
    public const int BusBytes = 4;

    /// <summary>What a level is multiplied by before it is rounded to an integer.</summary>
    public const double DecibelScale = 100.0;

    /// <summary>The level a meter shows when there is nothing there.</summary>
    public const double SilenceDb = -120.0;

    /// <summary>How large a frame is for a console of a given shape.</summary>
    /// <param name="channelCount">Strips.</param>
    /// <param name="busCount">Buses.</param>
    /// <returns>Bytes.</returns>
    public static int SizeOf(int channelCount, int busCount) =>
        (channelCount * ChannelBytes) + (busCount * BusBytes);

    /// <summary>Writes one strip's meters.</summary>
    /// <param name="frame">The frame being built.</param>
    /// <param name="index">Which strip.</param>
    /// <param name="meters">What to write.</param>
    public static void WriteChannel(Span<byte> frame, int index, ChannelMeter meters)
    {
        Span<byte> at = frame[(index * ChannelBytes)..];

        BinaryPrimitives.WriteInt16LittleEndian(at, ToFixed(meters.PeakDb));
        BinaryPrimitives.WriteInt16LittleEndian(at[2..], ToFixed(meters.RmsDb));
        BinaryPrimitives.WriteInt16LittleEndian(at[4..], ToFixed(meters.GainReductionDb));
        BinaryPrimitives.WriteUInt16LittleEndian(at[6..], ToUnit(meters.AutomixShare));

        at[8] = meters.Flags;
        at[9] = 0;
    }

    /// <summary>Reads one strip's meters.</summary>
    /// <param name="frame">The frame.</param>
    /// <param name="index">Which strip.</param>
    /// <returns>What it said.</returns>
    public static ChannelMeter ReadChannel(ReadOnlySpan<byte> frame, int index)
    {
        ReadOnlySpan<byte> at = frame[(index * ChannelBytes)..];

        return new ChannelMeter(
            FromFixed(BinaryPrimitives.ReadInt16LittleEndian(at)),
            FromFixed(BinaryPrimitives.ReadInt16LittleEndian(at[2..])),
            FromFixed(BinaryPrimitives.ReadInt16LittleEndian(at[4..])),
            FromUnit(BinaryPrimitives.ReadUInt16LittleEndian(at[6..])),
            at[8]);
    }

    /// <summary>Writes one bus's meters.</summary>
    /// <param name="frame">The frame being built.</param>
    /// <param name="channelCount">Strips before the buses.</param>
    /// <param name="index">Which bus.</param>
    /// <param name="peakDb">Its peak.</param>
    /// <param name="rmsDb">Its average.</param>
    public static void WriteBus(Span<byte> frame, int channelCount, int index, double peakDb, double rmsDb)
    {
        Span<byte> at = frame[((channelCount * ChannelBytes) + (index * BusBytes))..];

        BinaryPrimitives.WriteInt16LittleEndian(at, ToFixed(peakDb));
        BinaryPrimitives.WriteInt16LittleEndian(at[2..], ToFixed(rmsDb));
    }

    /// <summary>Reads one bus's meters.</summary>
    /// <param name="frame">The frame.</param>
    /// <param name="channelCount">Strips before the buses.</param>
    /// <param name="index">Which bus.</param>
    /// <returns>Its peak and average.</returns>
    public static (double PeakDb, double RmsDb) ReadBus(ReadOnlySpan<byte> frame, int channelCount, int index)
    {
        ReadOnlySpan<byte> at = frame[((channelCount * ChannelBytes) + (index * BusBytes))..];

        return (
            FromFixed(BinaryPrimitives.ReadInt16LittleEndian(at)),
            FromFixed(BinaryPrimitives.ReadInt16LittleEndian(at[2..])));
    }

    static short ToFixed(double decibels) =>
        (short)Math.Clamp(Math.Round(Math.Max(decibels, SilenceDb) * DecibelScale), short.MinValue, short.MaxValue);

    static double FromFixed(short value) => value / DecibelScale;

    static ushort ToUnit(double value) => (ushort)Math.Clamp(Math.Round(value * ushort.MaxValue), 0, ushort.MaxValue);

    static double FromUnit(ushort value) => value / (double)ushort.MaxValue;
}
