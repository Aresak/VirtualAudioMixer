using Vam.Engine.Devices.Abstractions;

namespace Vam.Engine.Devices;

/// <summary>
/// Remembers which device belongs to which strip, by stable identity.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing above this layer may address a device by index.</b> An index changes the moment
/// something else is unplugged, and a friendly name is not unique - this room has two identical
/// Jabras, which breaks both schemes at once. Identity comes from the backend's endpoint
/// identifier and nothing else.
/// </para>
/// <para>
/// Control thread only. It allocates freely, resolves against a cached view of what is present,
/// and never appears anywhere near the audio path.
/// </para>
/// </remarks>
public sealed class DeviceRegistry
{
    readonly Dictionary<AudioDeviceId, RememberedDevice> remembered = [];
    readonly Dictionary<AudioDeviceId, AudioDeviceInfo> present = [];

    /// <summary>Every device the configuration expects, present or not.</summary>
    public IReadOnlyCollection<RememberedDevice> Remembered => remembered.Values;

    /// <summary>Every device currently connected, as of the last <see cref="Refresh"/>.</summary>
    public IReadOnlyCollection<AudioDeviceInfo> Present => present.Values;

    /// <summary>
    /// Re-reads what is connected. Call after a device notification, and on a slow timer as a
    /// fallback, because a missed notification means a dead strip nobody notices.
    /// </summary>
    /// <param name="backend">The backend to enumerate.</param>
    public void Refresh(IAudioBackend backend)
    {
        ArgumentNullException.ThrowIfNull(backend);

        present.Clear();

        foreach (AudioDeviceInfo device in backend.Enumerate(DeviceDirection.Capture))
        {
            present[device.Id] = device;
        }

        foreach (AudioDeviceInfo device in backend.Enumerate(DeviceDirection.Render))
        {
            present[device.Id] = device;
        }

        // A device that is here now gets its name refreshed, so the name shown after it later
        // disappears is the most recent one rather than whatever it was called originally.
        foreach (AudioDeviceInfo device in present.Values)
        {
            if (remembered.TryGetValue(device.Id, out RememberedDevice? entry)
                && entry.LastKnownName != device.FriendlyName)
            {
                remembered[device.Id] = entry with { LastKnownName = device.FriendlyName };
            }
        }
    }

    /// <summary>Binds a device to a strip and remembers it across restarts.</summary>
    /// <param name="device">The device, which must be present to be remembered.</param>
    /// <param name="stripIndex">The strip it belongs to.</param>
    public void Remember(AudioDeviceInfo device, int stripIndex)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentOutOfRangeException.ThrowIfNegative(stripIndex);

        remembered[device.Id] = new RememberedDevice(
            device.Id,
            device.FriendlyName,
            device.Direction,
            stripIndex);
    }

    /// <summary>Stops expecting a device.</summary>
    /// <param name="deviceId">Which device.</param>
    /// <returns>Whether it was remembered in the first place.</returns>
    public bool Forget(AudioDeviceId deviceId) => remembered.Remove(deviceId);

    /// <summary>
    /// Looks a device up by identity.
    /// </summary>
    /// <param name="deviceId">Which device.</param>
    /// <returns>
    /// Present with the live device, absent with its last-known name, or unknown. Never throws:
    /// a device that has gone is an event, not a fault.
    /// </returns>
    public DeviceResolution Resolve(AudioDeviceId deviceId)
    {
        remembered.TryGetValue(deviceId, out RememberedDevice? entry);

        if (present.TryGetValue(deviceId, out AudioDeviceInfo? device))
        {
            return DeviceResolution.Present(device, entry);
        }

        return entry is not null
            ? DeviceResolution.Absent(entry)
            : DeviceResolution.Unknown();
    }

    /// <summary>Finds the device bound to a strip.</summary>
    /// <param name="stripIndex">Which strip.</param>
    /// <returns>Its resolution, or unknown when no device is bound to it.</returns>
    public DeviceResolution ResolveStrip(int stripIndex)
    {
        foreach (RememberedDevice entry in remembered.Values)
        {
            if (entry.StripIndex == stripIndex)
            {
                return Resolve(entry.Id);
            }
        }

        return DeviceResolution.Unknown();
    }

    /// <summary>Captures the mapping in a form configuration can persist.</summary>
    /// <returns>The snapshot. Ordered by strip so a written file diffs sensibly.</returns>
    public DeviceRegistrySnapshot ToSnapshot()
    {
        List<RememberedDevice> devices = [.. remembered.Values];
        devices.Sort(static (left, right) => left.StripIndex.CompareTo(right.StripIndex));

        return new DeviceRegistrySnapshot(devices);
    }

    /// <summary>Replaces the mapping with one read back from configuration.</summary>
    /// <param name="snapshot">What to restore. Does not itself make any device present.</param>
    public void Restore(DeviceRegistrySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        remembered.Clear();

        foreach (RememberedDevice device in snapshot.Devices)
        {
            remembered[device.Id] = device;
        }
    }
}
