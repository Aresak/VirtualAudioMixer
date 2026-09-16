using System.Runtime.InteropServices;

namespace Vam.Engine.Devices;

/// <summary>
/// A 64-bit counter that occupies a cache line on its own.
/// </summary>
/// <remarks>
/// The padding is the point. Without it the producer's cursor and the consumer's cursor share a
/// cache line, so every write by one invalidates the line the other is reading - hundreds of times
/// a second per device, times one ring per device. The counters would still be correct; they would
/// just be slow in the one place that cannot afford it.
/// </remarks>
[StructLayout(LayoutKind.Explicit, Size = 128)]
struct PaddedCursor
{
    /// <summary>The counter. Monotonic; never wrapped, only masked when used as an index.</summary>
    [FieldOffset(64)]
    public long Value;
}
