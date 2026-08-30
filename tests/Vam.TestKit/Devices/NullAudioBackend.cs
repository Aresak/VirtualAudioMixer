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
    readonly List<NullCaptureStream> captureStreams = [];
    readonly List<NullRenderStream> renderStreams = [];

    int nextDeviceNumber;

    /// <inheritdoc />
    public string Id => "null";

    /// <inheritdoc />
    /// <remarks>False: nothing here keeps time, so something else must.</remarks>
    public bool CanProvideTimebase => false;

    /// <summary>Capture streams opened so far, in the order they were opened.</summary>
    public IReadOnlyList<NullCaptureStream> CaptureStreams => captureStreams;

    /// <summary>Render streams opened so far, in the order they were opened.</summary>
    public IReadOnlyList<NullRenderStream> RenderStreams => renderStreams;

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
        AudioDeviceId id = new($"null:{direction}:{nextDeviceNumber++}");

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
            deviceOptions.SupportsExclusiveMode,
            deviceOptions.IsVirtual);

        devices[id] = info;
        options[id] = deviceOptions;

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

        options.Remove(deviceId);
        return devices.Remove(deviceId);
    }

    /// <inheritdoc />
    public IReadOnlyList<AudioDeviceInfo> Enumerate(DeviceDirection direction)
    {
        List<AudioDeviceInfo> present = [];

        foreach (AudioDeviceInfo info in devices.Values)
        {
            if (info.Direction == direction)
            {
                present.Add(info);
            }
        }

        return present;
    }

    /// <inheritdoc />
    public ICaptureStream OpenCapture(AudioDeviceId deviceId, CaptureOptions captureOptions)
    {
        NullDeviceOptions deviceOptions = Resolve(deviceId, DeviceDirection.Capture);

        AudioStreamFormat format = FormatFor(
            deviceOptions,
            captureOptions.ShareMode,
            captureOptions.BufferDuration,
            captureOptions.ChannelCount);

        NullCaptureStream stream = new(deviceId, deviceOptions, format);
        captureStreams.Add(stream);

        return stream;
    }

    /// <inheritdoc />
    public IRenderStream OpenRender(AudioDeviceId deviceId, RenderOptions renderOptions)
    {
        NullDeviceOptions deviceOptions = Resolve(deviceId, DeviceDirection.Render);

        AudioStreamFormat format = FormatFor(
            deviceOptions,
            renderOptions.ShareMode,
            renderOptions.BufferDuration,
            renderOptions.ChannelCount);

        NullRenderStream stream = new(deviceId, deviceOptions, format);
        renderStreams.Add(stream);

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

        captureStreams.Clear();
        renderStreams.Clear();
    }

    static AudioStreamFormat FormatFor(
        NullDeviceOptions deviceOptions,
        ShareMode requested,
        TimeSpan bufferDuration,
        int channelCount)
    {
        // Exclusive is the mode a device can refuse, and refusing it must be visible rather than
        // silent - a session that fell back to shared is a session with a different latency budget.
        ShareMode granted = requested == ShareMode.Exclusive && !deviceOptions.SupportsExclusiveMode
            ? ShareMode.Shared
            : requested;

        int channels = channelCount > 0 ? channelCount : deviceOptions.ChannelCount;
        int frames = (int)Math.Round(bufferDuration.TotalSeconds * deviceOptions.NominalSampleRate);

        return new AudioStreamFormat(
            deviceOptions.NominalSampleRate,
            channels,
            Math.Max(1, frames),
            granted);
    }

    NullDeviceOptions Resolve(AudioDeviceId deviceId, DeviceDirection direction)
    {
        if (!devices.TryGetValue(deviceId, out AudioDeviceInfo? info) || info.Direction != direction)
        {
            throw new DeviceNotFoundException(deviceId);
        }

        return options[deviceId];
    }
}
