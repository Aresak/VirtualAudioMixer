namespace Vam.Engine.Devices;

/// <summary>
/// Where one device's block sits inside the clock's arena.
/// </summary>
/// <remarks>
/// An offset and a width rather than a separate array per device. One contiguous arena means a
/// block's whole working set is a handful of kilobytes that fits in L1 <i>because it is contiguous</i>,
/// which an array of arrays would give up for nothing.
/// </remarks>
/// <param name="Offset">First sample of this device's block within the arena.</param>
/// <param name="ChannelCount">Channels interleaved in it.</param>
public readonly record struct BlockSlice(int Offset, int ChannelCount);
