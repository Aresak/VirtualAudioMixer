using Vam.Engine.Devices.Abstractions;

namespace Vam.Engine.Devices;

/// <summary>
/// Where one strip's audio comes from: a device, and a run of channels within it.
/// </summary>
/// <remarks>
/// A run rather than a single index, because a stereo pair feeding one stereo strip and a single
/// channel feeding a mono strip are the same idea with a different length. Modelling them as two
/// concepts would mean every consumer branching on which it had.
/// </remarks>
/// <param name="DeviceId">Which device. Identity, never an index — see <see cref="DeviceRegistry"/>.</param>
/// <param name="FirstChannel">Zero-based index of the first channel taken from that device.</param>
/// <param name="ChannelCount">How many consecutive channels the strip takes. One for mono, two for a pair.</param>
/// <param name="StripIndex">The strip this feeds. Two sources may not claim the same one.</param>
public readonly record struct ChannelSource(
    AudioDeviceId DeviceId,
    int FirstChannel,
    int ChannelCount,
    int StripIndex)
{
    /// <summary>One channel past the last one this source reads.</summary>
    public int ChannelLimit => FirstChannel + ChannelCount;
}
