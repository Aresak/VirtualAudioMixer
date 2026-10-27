using Vam.Engine.Devices.Abstractions;

namespace Vam.TestKit.Devices;

/// <summary>
/// An <see cref="IAudioBackend"/> with no hardware behind it.
/// </summary>
/// <remarks>
/// <para>
/// Not a stub. This is what makes the hardest part of the engine testable: devices here run at a
/// deliberately inexact rate, so a drift estimator can be checked against a figure that is known
/// exactly rather than one that is hoped for. Two devices drifting in opposite directions across a
/// simulated eight hours is a test that runs in CI; the same thing with real hardware is an
/// evening.
/// </para>
/// <para>
/// Streams are pumped by the caller rather than by a thread, so no test here depends on timing.
/// </para>
/// </remarks>
public sealed class NullAudioBackend : IAudioBackend
{
    readonly Dictionary<AudioDeviceId, NullDeviceOptions> options = [];
    readonly Dictionary<AudioDeviceId, AudioDeviceInfo> devices = [];
    readonly Dictionary<DeviceDirection, AudioDeviceId> defaults = [];
    readonly Lock opening = new();

    // A test may add or remove a device while an engine's control thread enumerates.
    readonly Lock table = new();

    // Copied on write: the engine opens streams on its control thread while a test reads these.
    NullCaptureStream[] captureStreams = [];
    NullRenderStream[] renderStreams = [];

    int nextDeviceNumber;

    /// <inheritdoc />
    public string Id => "null";

    /// <inheritdoc />
    /// <remarks>False: nothing here keeps time, so something else must.</remarks>
    public bool CanProvideTimebase => false;

    /// <summary>Capture streams opened so far, in the order they were opened.</summary>
    public IReadOnlyList<NullCaptureStream> CaptureStreams => Volatile.Read(ref captureStreams);

    /// <summary>Render streams opened so far, in the order they were opened.</summary>
    public IReadOnlyList<NullRenderStream> RenderStreams => Volatile.Read(ref renderStreams);

    /// <summary>
    /// Adds a device.
    /// </summary>
    /// <param name="direction">Capture or render.</param>
    /// <param name="deviceOptions">How it behaves, including how far its clock drifts.</param>
    /// <returns>The device, as <see cref="Enumerate"/> will report it.</returns>
    public AudioDeviceInfo AddDevice(DeviceDirection direction, NullDeviceOptions deviceOptions)
    {
        // Identity is generated rather than derived from the name, precisely so two devices can
        // share a friendly name - which is the case that breaks name-based identity in real rooms.
        AudioDeviceId id = new($"null:{direction}:{Interlocked.Increment(ref nextDeviceNumber) - 1}");

        return AddDevice(id, direction, deviceOptions);
    }

    /// <summary>
    /// Adds a device with an identity chosen by the caller.
    /// </summary>
    /// <remarks>
    /// For the case a generated identity cannot express: a device that was unplugged and plugged
    /// back in. It is the <i>same</i> device, so it comes back under the same identity, and every
    /// re-attachment test depends on being able to say that. Adding it under a fresh identity would
    /// be a different microphone, which is a different scenario entirely.
    /// </remarks>
    /// <param name="id">The identity to restore it under.</param>
    /// <param name="direction">Capture or render.</param>
    /// <param name="deviceOptions">How it behaves.</param>
    /// <returns>The device, as <see cref="Enumerate"/> will report it.</returns>
    public AudioDeviceInfo AddDevice(AudioDeviceId id, DeviceDirection direction, NullDeviceOptions deviceOptions)
    {
        AudioDeviceInfo info = new(
            id,
            deviceOptions.FriendlyName,
            direction,
            deviceOptions.ChannelCount,
            deviceOptions.NominalSampleRate,
            deviceOptions.SupportsExclusiveMode && !deviceOptions.IsVirtual,
            deviceOptions.IsVirtual,
            deviceOptions.ContainerId,
            deviceOptions.DeviceSampleRate,
            deviceOptions.DeviceChannelCount);

        lock (table)
        {
            devices[id] = info;
            options[id] = deviceOptions;
        }

        return info;
    }

    /// <summary>Removes a device, as unplugging it would.</summary>
    /// <param name="deviceId">Which device.</param>
    /// <returns>Whether it was there to remove.</returns>
    public bool RemoveDevice(AudioDeviceId deviceId)
    {
        foreach (NullCaptureStream stream in captureStreams)
        {
            if (stream.DeviceId == deviceId)
            {
                stream.SimulateRemoval();
            }
        }

        foreach (NullRenderStream stream in renderStreams)
        {
            if (stream.DeviceId == deviceId)
            {
                stream.SimulateRemoval();
            }
        }

        lock (table)
        {
            options.Remove(deviceId);
            return devices.Remove(deviceId);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<AudioDeviceInfo> Enumerate(DeviceDirection direction)
    {
        List<AudioDeviceInfo> present = [];

        lock (table)
        {
            foreach (AudioDeviceInfo info in devices.Values)
            {
                if (info.Direction == direction)
                {
                    present.Add(info);
                }
            }
        }

        return present;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The first one added in that direction, and settable when a test needs to say which. A fake
    /// with no notion of a default would let a test pass while the real backend picks differently.
    /// </remarks>
    public AudioDeviceInfo? DefaultDevice(DeviceDirection direction)
    {
        lock (table)
        {
            return DefaultDeviceLocked(direction);
        }
    }

    /// <summary>Says which device this backend calls the default.</summary>
    /// <param name="deviceId">Which one.</param>
    public void MakeDefault(AudioDeviceId deviceId)
    {
        lock (table)
        {
            if (devices.TryGetValue(deviceId, out AudioDeviceInfo? info))
            {
                defaults[info.Direction] = deviceId;
            }
        }
    }

    AudioDeviceInfo? DefaultDeviceLocked(DeviceDirection direction)
    {
        if (defaults.TryGetValue(direction, out AudioDeviceId chosen) && devices.TryGetValue(chosen, out AudioDeviceInfo? named))
        {
            return named;
        }

        foreach (AudioDeviceInfo info in devices.Values)
        {
            if (info.Direction == direction)
            {
                return info;
            }
        }

        return null;
    }

    /// <inheritdoc />
    public ICaptureStream OpenCapture(AudioDeviceId deviceId, CaptureOptions captureOptions)
    {
        NullDeviceOptions deviceOptions = Resolve(deviceId, DeviceDirection.Capture);

        AudioStreamFormat format = FormatFor(
            deviceOptions,
            new NullStreamRequest(
                captureOptions.ShareMode,
                captureOptions.BufferDuration,
                captureOptions.SampleRate,
                captureOptions.ChannelCount));

        NullCaptureStream stream = new(deviceId, deviceOptions, format);

        lock (opening)
        {
            Volatile.Write(ref captureStreams, [.. captureStreams, stream]);
        }

        return stream;
    }

    /// <inheritdoc />
    public IRenderStream OpenRender(AudioDeviceId deviceId, RenderOptions renderOptions)
    {
        NullDeviceOptions deviceOptions = Resolve(deviceId, DeviceDirection.Render);

        AudioStreamFormat format = FormatFor(
            deviceOptions,
            new NullStreamRequest(
                renderOptions.ShareMode,
                renderOptions.BufferDuration,
                renderOptions.SampleRate,
                renderOptions.ChannelCount));

        NullRenderStream stream = new(deviceId, deviceOptions, format);

        lock (opening)
        {
            Volatile.Write(ref renderStreams, [.. renderStreams, stream]);
        }

        return stream;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (NullCaptureStream stream in captureStreams)
        {
            stream.Dispose();
        }

        foreach (NullRenderStream stream in renderStreams)
        {
            stream.Dispose();
        }

        lock (opening)
        {
            captureStreams = [];
            renderStreams = [];
        }
    }

    /// <summary>
    /// Grants a stream the same way the real backend does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately the same policy rather than a convenient one, because it is the policy under
    /// test. Exclusive is granted only where the device's own format already carries what was asked
    /// for; anything else is shared, and shared converts - which is what an operating system does
    /// and what this stands in for.
    /// </para>
    /// <para>
    /// A device that cannot convert refuses instead. Nothing here does that today, and the setting
    /// exists because something will: an ASIO driver has no system mixer behind it to convert with,
    /// and the engine's answer to a rate it cannot have has to be tested somewhere.
    /// </para>
    /// </remarks>
    static AudioStreamFormat FormatFor(NullDeviceOptions deviceOptions, NullStreamRequest request)
    {
        int rate = request.SampleRate > 0 ? request.SampleRate : deviceOptions.NominalSampleRate;
        int channels = request.ChannelCount > 0 ? request.ChannelCount : deviceOptions.ChannelCount;

        if (!deviceOptions.CanConvertRate && rate != deviceOptions.DeviceSampleRate)
        {
            throw new UnsupportedAudioFormatException(
                $"{deviceOptions.FriendlyName} runs at {deviceOptions.DeviceSampleRate} Hz and nothing behind it "
                + $"converts, so it cannot deliver {rate} Hz.");
        }

        // Exclusive is the mode a device can refuse, and refusing it must be visible rather than
        // silent - a session that fell back to shared is a session with a different latency budget.
        // A virtual endpoint never grants it, as in the real backend.
        bool canTakeExclusive = deviceOptions.SupportsExclusiveMode
            && !deviceOptions.IsVirtual
            && rate == deviceOptions.DeviceSampleRate
            && channels == deviceOptions.DeviceChannelCount;

        ShareMode granted = request.ShareMode == ShareMode.Exclusive && canTakeExclusive
            ? ShareMode.Exclusive
            : ShareMode.Shared;

        int frames = (int)Math.Round(request.BufferDuration.TotalSeconds * rate);

        return new AudioStreamFormat(rate, channels, Math.Max(1, frames), granted, deviceOptions.DeviceSampleRate);
    }

    NullDeviceOptions Resolve(AudioDeviceId deviceId, DeviceDirection direction)
    {
        lock (table)
        {
            if (!devices.TryGetValue(deviceId, out AudioDeviceInfo? info) || info.Direction != direction)
            {
                throw new DeviceNotFoundException(deviceId);
            }

            return options[deviceId];
        }
    }
}
