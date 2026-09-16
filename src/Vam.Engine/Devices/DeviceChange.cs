using Vam.Engine.Devices.Abstractions;

namespace Vam.Engine.Devices;

/// <summary>
/// One device coming or going, in a form the log, the diagnostics view and the supervisor all read.
/// </summary>
/// <remarks>
/// Carries the friendly name alongside the identity, because by the time a removal is being logged
/// the device is no longer there to ask what it was called. A log line reading "device
/// {0.0.1.00000000}.{221b0797…} was removed" is technically complete and useless to the person
/// holding the cable.
/// </remarks>
/// <param name="Kind">What happened.</param>
/// <param name="DeviceId">Which device, by stable identity.</param>
/// <param name="FriendlyName">What it was called, for the human reading the log.</param>
/// <param name="Timestamp">When it happened.</param>
public readonly record struct DeviceChange(
    DeviceChangeKind Kind,
    AudioDeviceId DeviceId,
    string FriendlyName,
    DateTimeOffset Timestamp);
