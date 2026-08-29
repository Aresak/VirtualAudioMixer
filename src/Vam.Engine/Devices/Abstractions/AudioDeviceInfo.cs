namespace Vam.Engine.Devices.Abstractions;

/// <summary>
/// What a backend knows about a device before anything opens it.
/// </summary>
/// <param name="Id">Stable identity. This, not the name, is what gets persisted.</param>
/// <param name="FriendlyName">What the operating system calls it. For display only - it is not unique.</param>
/// <param name="Direction">Capture or render.</param>
/// <param name="ChannelCount">Channels the device offers.</param>
/// <param name="NominalSampleRate">The rate the device claims. What it actually runs at is measured later.</param>
/// <param name="SupportsExclusiveMode">
/// Whether the device will grant <see cref="ShareMode.Exclusive"/>. Shared mode is always available;
/// exclusive is the one a device can refuse, and refusing it changes the latency budget.
/// </param>
/// <param name="IsVirtual">
/// Whether this endpoint comes from a virtual audio driver rather than hardware. Used to derive
/// mix-minus exclusion when the graph is built; nothing in the audio path ever branches on it.
/// </param>
public sealed record AudioDeviceInfo(
    AudioDeviceId Id,
    string FriendlyName,
    DeviceDirection Direction,
    int ChannelCount,
    int NominalSampleRate,
    bool SupportsExclusiveMode = true,
    bool IsVirtual = false);
