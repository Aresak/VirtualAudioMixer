using Vam.Core;

namespace Vam.Engine.Devices.Abstractions;

/// <summary>
/// Thrown when a device is asked for by identity and is not present.
/// </summary>
/// <remarks>
/// Opening a device that has gone is a programming error at this level - callers are expected to
/// resolve identities first and handle absence as the ordinary event it is. Once the device
/// registry exists, an identity that is remembered but missing resolves to
/// <see cref="DeviceStreamState.Absent"/> with its last-known name, and no exception is raised.
/// </remarks>
public sealed class DeviceNotFoundException : VamException
{
    /// <summary>Names the missing device.</summary>
    /// <param name="deviceId">The identity that could not be resolved.</param>
    public DeviceNotFoundException(AudioDeviceId deviceId)
        : base($"No audio device with identity '{deviceId}' is present.")
    {
        DeviceId = deviceId;
    }

    /// <summary>The identity that could not be resolved.</summary>
    public AudioDeviceId DeviceId { get; }
}
