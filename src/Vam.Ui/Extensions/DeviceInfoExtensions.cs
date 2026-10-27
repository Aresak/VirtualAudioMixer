using Vam.Protocol.V1;

namespace Vam.Ui.Extensions;

/// <summary>Reads device names out of the list the engine reported.</summary>
public static class DeviceInfoExtensions
{
    /// <summary>The name of the device with this ID, for a selector's hover text.</summary>
    /// <param name="devices">The devices to look in.</param>
    /// <param name="deviceId">The device's ID. Null or empty finds nothing.</param>
    /// <returns>The device's name, or an empty string when no device has that ID.</returns>
    public static string NameOf(this IEnumerable<DeviceInfo> devices, string? deviceId)
    {
        ArgumentNullException.ThrowIfNull(devices);

        return devices.FirstOrDefault(device => device.Id == deviceId)?.Name ?? string.Empty;
    }
}
