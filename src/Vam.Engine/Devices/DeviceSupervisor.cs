using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Vam.Engine.Devices.Abstractions;
using Vam.Engine.Devices.Extensions;

namespace Vam.Engine.Devices;

/// <summary>
/// Keeps the devices a session wants open, and puts them back when they come and go.
/// </summary>
/// <remarks>
/// <para>
/// A microphone that re-enumerates in the middle of a council meeting is a normal event. Removal
/// takes down that device and nothing else; return re-opens it on the same strip with its buffers
/// and its drift estimate cleared, because the old ones describe a stream that has since stopped.
/// </para>
/// <para>
/// <b>Notifications never touch anything directly.</b> A WASAPI device notification arrives on a COM
/// thread, so <see cref="Post"/> only queues it; every decision is made in <see cref="Poll"/> on the
/// control thread. Acting on a COM callback would put device enumeration and stream disposal on a
/// thread that has no business doing either.
/// </para>
/// <para>
/// <b>Notifications are an optimisation, not the mechanism.</b> <see cref="Poll"/> also reconciles
/// against what the backend actually reports, on a slow timer, so a missed notification costs a few
/// seconds rather than a strip that is dead for the rest of the meeting.
/// </para>
/// <para>
/// Control thread only, and entirely outside the audio path. The audio thread learns about all of
/// this through <see cref="DeviceInputChannel.State"/> and an empty ring, both of which it already
/// reads.
/// </para>
/// <para>
/// <b>One thing here is not finished, and it will matter later.</b> Re-opening clears the channel's
/// ring in place. That is correct while nothing is pulling from it, which is true today because the
/// mix graph does not exist yet. Once EPIC-03's graph pulls every block regardless of whether a
/// device is present, clearing a ring underneath a live reader is a torn read waiting to happen, and
/// the design's answer is the one VAM-020 describes: open the new stream, prime its ring to the
/// servo's setpoint, and then swap the ring reference in with a single write. Priming before
/// swapping is also what makes a re-attach silent instead of a click. That swap is owed when the
/// graph arrives.
/// </para>
/// </remarks>
public sealed class DeviceSupervisor(
    IAudioBackend backend,
    DeviceInputChannelRegistry channels,
    ILoggerFactory loggerFactory) : IDisposable
{
    /// <summary>
    /// How often to check the backend regardless of notifications. Slow on purpose - it is a safety
    /// net, and enumerating WASAPI is not free.
    /// </summary>
    static readonly TimeSpan ReconcileInterval = TimeSpan.FromSeconds(5);

    readonly ILogger<DeviceSupervisor> logger = loggerFactory.CreateLogger<DeviceSupervisor>();
    readonly ConcurrentQueue<DeviceChange> pending = new();
    readonly Dictionary<AudioDeviceId, TrackedCaptureDevice> tracked = [];
    readonly List<DeviceInputChannel> vacated = [];

    TimeSpan sinceReconcile = ReconcileInterval;

    /// <summary>Raised for every arrival, departure and failed open. Control thread.</summary>
    public event EventHandler<DeviceChange>? Changed;

    /// <summary>What a vacant registry slot reports as its device. Never a real endpoint, and never <see cref="AudioDeviceId.None"/>.</summary>
    public static AudioDeviceId VacantSlot { get; } = new("vam:vacant-slot");

    /// <summary>The devices being kept open.</summary>
    public IReadOnlyCollection<AudioDeviceId> TrackedDevices => tracked.Keys;

    /// <summary>
    /// Takes responsibility for a device, and tries to open it now.
    /// </summary>
    /// <param name="deviceId">Which device.</param>
    /// <param name="options">How to build its channel.</param>
    /// <param name="captureOptions">What to ask the device for.</param>
    /// <returns>The channel, which exists whether or not the device is currently present.</returns>
    public DeviceInputChannel Track(
        AudioDeviceId deviceId,
        DeviceInputChannelOptions options,
        CaptureOptions captureOptions)
    {
        if (tracked.TryGetValue(deviceId, out TrackedCaptureDevice? existing))
        {
            return existing.Channel;
        }

        // The channel is created here and never replaced. A strip keeps its identity, its telemetry
        // and its place in the graph across a device going away and coming back - only the stream
        // underneath it is transient.
        DeviceInputChannel channel = new(deviceId, options, loggerFactory.CreateLogger<DeviceInputChannel>());
        TrackedCaptureDevice device = new(deviceId, channel, captureOptions);

        tracked[deviceId] = device;

        if (vacated.Count > 0)
        {
            bool isReplaced = channels.Replace(vacated[0], channel);

            Debug.Assert(isReplaced, "A vacant slot's channel was not in the registry.");
            vacated.RemoveAt(0);
        }
        else
        {
            channels.Add(channel);
        }

        TryOpen(device);

        return channel;
    }

    /// <summary>Stops looking after a device and closes it.</summary>
    /// <remarks>
    /// Its registry slot is left vacant until <see cref="Track"/> reuses it, rather than removed.
    /// Removing it would shift every later channel down one place under a plan compiled for the old
    /// places, and the clock would hand each strip its neighbour's microphone until the next one.
    /// </remarks>
    /// <param name="deviceId">Which device.</param>
    /// <returns>Whether it was being tracked.</returns>
    public bool Forget(AudioDeviceId deviceId)
    {
        if (!tracked.Remove(deviceId, out TrackedCaptureDevice? device))
        {
            return false;
        }

        Close(device, DeviceStreamState.Stopped);
        device.Channel.Vacate();
        vacated.Add(device.Channel);

        return true;
    }

    /// <summary>The device a registry slot holds, or <see cref="VacantSlot"/> when it holds none.</summary>
    /// <param name="channel">A registered channel.</param>
    /// <returns>Its device.</returns>
    public static AudioDeviceId DeviceInSlot(DeviceInputChannel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);

        return channel.IsVacant ? VacantSlot : channel.DeviceId;
    }

    /// <summary>
    /// Moves a tracked device to the endpoint ID Windows now knows it by, and tries to open it.
    /// </summary>
    /// <remarks>
    /// The channel is kept rather than replaced, so the strip keeps its place in the registry and
    /// nothing the clock pulls from is added or removed under it.
    /// </remarks>
    /// <param name="from">The ID it was tracked under.</param>
    /// <param name="to">The ID it is present under now.</param>
    /// <returns>Whether it was tracked under <paramref name="from"/> and <paramref name="to"/> was free.</returns>
    public bool Retarget(AudioDeviceId from, AudioDeviceId to)
    {
        if (tracked.ContainsKey(to) || !tracked.Remove(from, out TrackedCaptureDevice? device))
        {
            return false;
        }

        Close(device, DeviceStreamState.Absent);

        device.DeviceId = to;
        device.Channel.DeviceId = to;
        device.CancelRetry();
        tracked[to] = device;

        TryOpen(device);

        return true;
    }

    /// <summary>
    /// Stops tracking one device and tracks another in its registry slot, then tries to open it.
    /// </summary>
    /// <remarks>
    /// A new channel rather than <see cref="Retarget"/>'s kept one, because the new device may be a
    /// different width and a channel's width is fixed when it is built.
    /// </remarks>
    /// <param name="from">The tracked device to let go of.</param>
    /// <param name="to">The device to take its slot.</param>
    /// <param name="options">How to build the new channel.</param>
    /// <param name="captureOptions">What to ask the new device for.</param>
    /// <returns>Whether <paramref name="from"/> was tracked and <paramref name="to"/> was not.</returns>
    public bool Replace(
        AudioDeviceId from,
        AudioDeviceId to,
        DeviceInputChannelOptions options,
        CaptureOptions captureOptions)
    {
        if (tracked.ContainsKey(to) || !tracked.Remove(from, out TrackedCaptureDevice? previous))
        {
            return false;
        }

        Close(previous, DeviceStreamState.Stopped);
        previous.Channel.Vacate();

        DeviceInputChannel channel = new(to, options, loggerFactory.CreateLogger<DeviceInputChannel>());
        TrackedCaptureDevice device = new(to, channel, captureOptions);

        tracked[to] = device;

        bool isReplaced = channels.Replace(previous.Channel, channel);

        Debug.Assert(isReplaced, "A tracked device's channel was not in the registry.");

        TryOpen(device);

        return true;
    }

    /// <summary>
    /// Queues a device notification. Safe from any thread, and does nothing else.
    /// </summary>
    /// <param name="change">What the operating system reported.</param>
    public void Post(DeviceChange change) => pending.Enqueue(change);

    /// <summary>
    /// Acts on everything queued, advances retries, and reconciles against the backend.
    /// </summary>
    /// <param name="elapsed">Time since the previous call.</param>
    public void Poll(TimeSpan elapsed)
    {
        while (pending.TryDequeue(out DeviceChange change))
        {
            Handle(change);
        }

        AdvanceRetries(elapsed);
        ReconcileIfDue(elapsed);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (TrackedCaptureDevice device in tracked.Values)
        {
            Close(device, DeviceStreamState.Stopped);
        }

        tracked.Clear();
    }

    void Handle(DeviceChange change)
    {
        if (!tracked.TryGetValue(change.DeviceId, out TrackedCaptureDevice? device))
        {
            return;
        }

        if (change.Kind == DeviceChangeKind.Removed)
        {
            Close(device, DeviceStreamState.Absent);
            Announce(DeviceChangeKind.Removed, device);
            return;
        }

        // Idempotent by construction: two arrival notifications for the same device find it already
        // open the second time and do nothing. WASAPI sends duplicates routinely.
        if (!device.IsOpen)
        {
            TryOpen(device);
        }
    }

    void AdvanceRetries(TimeSpan elapsed)
    {
        foreach (TrackedCaptureDevice device in tracked.Values)
        {
            if (!device.IsOpen && device.FailedAttempts > 0 && device.IsRetryDue(elapsed))
            {
                TryOpen(device);
            }
        }
    }

    void ReconcileIfDue(TimeSpan elapsed)
    {
        sinceReconcile += elapsed;

        if (sinceReconcile < ReconcileInterval)
        {
            return;
        }

        sinceReconcile = TimeSpan.Zero;

        HashSet<AudioDeviceId> present = [];

        foreach (AudioDeviceInfo info in backend.Enumerate(DeviceDirection.Capture))
        {
            present.Add(info.Id);
        }

        Reconcile(present);
    }

    void Reconcile(HashSet<AudioDeviceId> present)
    {
        foreach (TrackedCaptureDevice device in tracked.Values)
        {
            bool isPresent = present.Contains(device.DeviceId);

            if (isPresent && !device.IsOpen)
            {
                // A notification was missed, or the device came back while we were backing off.
                device.CancelRetry();
                TryOpen(device);
            }
            else if (!isPresent && device.IsOpen)
            {
                Close(device, DeviceStreamState.Absent);
                Announce(DeviceChangeKind.Removed, device);
            }
        }
    }

    void TryOpen(TrackedCaptureDevice device)
    {
        if (device.IsOpen)
        {
            return;
        }

        try
        {
            // Cleared before the stream starts, not after. The ring holds audio from before the
            // device left, and the drift estimate describes a rate it may no longer run at;
            // carrying either across the gap would splice two unrelated moments together.
            device.Channel.Reset();

            ICaptureStream stream = backend.OpenCapture(device.DeviceId, device.CaptureOptions);

            stream.RequireFormat(device.Channel.NominalSampleRate, device.Channel.ChannelCount);
            device.Channel.DescribeStream(stream.Format);
            stream.Start(device.Channel.Write);

            device.Stream = stream;
            device.Channel.State = DeviceStreamState.Running;
            device.FriendlyName = NameOf(device.DeviceId, device.FriendlyName);
            device.Succeeded();

            Announce(DeviceChangeKind.Arrived, device);
        }
        catch (Exception error)
        {
            device.Channel.State = DeviceStreamState.Absent;
            device.Failed();

            logger.LogWarning(
                error,
                "Could not open {DeviceName}. Attempt {Attempt}; retrying.",
                device.FriendlyName,
                device.FailedAttempts);

            Announce(DeviceChangeKind.OpenFailed, device);
        }
    }

    void Close(TrackedCaptureDevice device, DeviceStreamState state)
    {
        // Disposed before anything else opens. The stream owns a thread and a set of COM objects,
        // and leaking one of each per unplug is exactly what the twenty-times plug cycle is for.
        device.Stream?.Dispose();
        device.Stream = null;
        device.Channel.State = state;
        device.Channel.DescribeStream(default);
    }

    string NameOf(AudioDeviceId deviceId, string fallback)
    {
        foreach (AudioDeviceInfo info in backend.Enumerate(DeviceDirection.Capture))
        {
            if (info.Id == deviceId)
            {
                return info.FriendlyName;
            }
        }

        return fallback;
    }

    void Announce(DeviceChangeKind kind, TrackedCaptureDevice device)
    {
        DeviceChange change = new(kind, device.DeviceId, device.FriendlyName, DateTimeOffset.UtcNow);

        if (kind != DeviceChangeKind.OpenFailed)
        {
            logger.LogInformation(
                "{DeviceName} {Kind} at {Timestamp:HH:mm:ss}.",
                change.FriendlyName,
                kind,
                change.Timestamp);
        }

        Changed?.Invoke(this, change);
    }
}
