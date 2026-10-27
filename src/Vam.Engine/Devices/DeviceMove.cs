using Vam.Engine.Devices.Abstractions;

namespace Vam.Engine.Devices;

/// <summary>
/// A tracked device found again under a new endpoint ID. #64.
/// </summary>
/// <param name="From">The ID it was tracked under.</param>
/// <param name="To">The ID it has now.</param>
/// <param name="FriendlyName">What it is called.</param>
public readonly record struct DeviceMove(AudioDeviceId From, AudioDeviceId To, string FriendlyName);
