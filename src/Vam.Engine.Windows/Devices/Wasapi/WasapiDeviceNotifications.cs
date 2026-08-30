using NAudio.CoreAudioApi;
using Vam.Engine.Devices;
using Vam.Engine.Devices.Abstractions;

namespace Vam.Engine.Windows.Devices.Wasapi;

/// <summary>
/// Turns WASAPI device notifications into something the supervisor can act on later.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every handler here does exactly one thing: enqueue.</b> These callbacks arrive on a COM thread
/// owned by the audio service, and doing real work on one — enumerating devices, disposing a stream,
/// starting a thread — is how a hotplug turns into a deadlock or a crash inside somebody else's
/// callback. The supervisor drains the queue on the control thread and makes every decision there.
/// </para>
/// <para>
/// The friendly name is not filled in, because a device that has just been removed is no longer
/// there to ask. The supervisor remembers what each device was called from when it was open, which
/// is the only moment the name is knowable.
/// </para>
/// </remarks>
public sealed class WasapiDeviceNotifications : IDisposable
{
    readonly MMDeviceEnumerator enumerator = new();
    readonly MMDeviceNotificationClient client;
    readonly DeviceSupervisor supervisor;

    /// <summary>Subscribes to the operating system's device notifications.</summary>
    /// <param name="supervisor">Where the notifications are queued.</param>
    public WasapiDeviceNotifications(DeviceSupervisor supervisor)
    {
        ArgumentNullException.ThrowIfNull(supervisor);

        this.supervisor = supervisor;

        // No synchronisation context: there is nothing to marshal to, and asking for one would only
        // move the work onto a thread that is equally wrong for it.
        client = enumerator.CreateNotificationClient(useSynchronizationContext: false);

        client.DeviceAdded += OnDeviceAdded;
        client.DeviceRemoved += OnDeviceRemoved;
        client.DeviceStateChanged += OnDeviceStateChanged;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        client.DeviceAdded -= OnDeviceAdded;
        client.DeviceRemoved -= OnDeviceRemoved;
        client.DeviceStateChanged -= OnDeviceStateChanged;

        client.Dispose();
        enumerator.Dispose();
    }

    void OnDeviceAdded(object? sender, DeviceNotificationEventArgs arguments) =>
        Post(DeviceChangeKind.Arrived, arguments.DeviceId);

    void OnDeviceRemoved(object? sender, DeviceNotificationEventArgs arguments) =>
        Post(DeviceChangeKind.Removed, arguments.DeviceId);

    void OnDeviceStateChanged(object? sender, DeviceStateChangedEventArgs arguments)
    {
        // Unplugging a USB microphone usually arrives here rather than as a removal - the endpoint
        // still exists, it just stops being Active. Treating only DeviceRemoved as departure is why
        // a strip goes quiet without anybody being told.
        DeviceChangeKind kind = arguments.NewState == DeviceState.Active
            ? DeviceChangeKind.Arrived
            : DeviceChangeKind.Removed;

        Post(kind, arguments.DeviceId);
    }

    void Post(DeviceChangeKind kind, string deviceId) =>
        supervisor.Post(new DeviceChange(kind, new AudioDeviceId(deviceId), deviceId, DateTimeOffset.UtcNow));
}
