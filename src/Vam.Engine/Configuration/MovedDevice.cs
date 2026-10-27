using Vam.Engine.Devices.Abstractions;

namespace Vam.Engine.Configuration;

/// <summary>A remembered device found again under a new endpoint ID.</summary>
/// <param name="From">The ID the console had.</param>
/// <param name="To">The ID it is present under now.</param>
/// <param name="Direction">Capture or render.</param>
/// <param name="FriendlyName">The name it was matched by.</param>
public readonly record struct MovedDevice(
    AudioDeviceId From,
    AudioDeviceId To,
    DeviceDirection Direction,
    string FriendlyName);
