using Vam.Engine.Devices.Abstractions;

namespace Vam.Engine.Devices;

/// <summary>
/// Finds the present device a remembered one became. #64.
/// </summary>
/// <remarks>
/// Windows gives a USB device a new endpoint ID when it moves to another port or its driver is
/// reinstalled. The name stays. An exact name, one candidate and nobody else using it is the only
/// case taken: two microphones of the same model are indistinguishable by name, and guessing
/// between them would put one person's voice on another person's strip.
/// </remarks>
public static class DeviceMatch
{
    /// <summary>Looks for exactly one present, unused device with this name.</summary>
    /// <param name="friendlyName">What the device was called when it was last seen.</param>
    /// <param name="present">What is plugged in now, in one direction.</param>
    /// <param name="isTaken">Whether a device is already bound to something else.</param>
    /// <param name="candidates">How many present, unused devices carry the name.</param>
    /// <returns>The device when there is exactly one, otherwise null.</returns>
    public static AudioDeviceInfo? Find(
        string friendlyName,
        IReadOnlyList<AudioDeviceInfo> present,
        Func<AudioDeviceId, bool> isTaken,
        out int candidates)
    {
        ArgumentNullException.ThrowIfNull(present);
        ArgumentNullException.ThrowIfNull(isTaken);

        candidates = 0;

        if (string.IsNullOrWhiteSpace(friendlyName))
        {
            return null;
        }

        AudioDeviceInfo? found = null;

        foreach (AudioDeviceInfo device in present)
        {
            if (device.FriendlyName != friendlyName || isTaken(device.Id))
            {
                continue;
            }

            candidates++;
            found = device;
        }

        return candidates == 1 ? found : null;
    }
}
