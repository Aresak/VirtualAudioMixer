using Vam.Engine.Devices.Abstractions;

namespace Vam.Engine.Devices;

/// <summary>One bus that wants to reach a device. D7.</summary>
/// <param name="BusIndex">Which bus.</param>
/// <param name="DeviceId">Which endpoint, or none.</param>
/// <param name="ChannelCount">How wide the bus is.</param>
public readonly record struct BusOutputRequest(int BusIndex, AudioDeviceId DeviceId, int ChannelCount);
