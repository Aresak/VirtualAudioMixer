using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Vam.Engine.Devices;
using Vam.Engine.Devices.Abstractions;

namespace Vam.Engine.Windows.Devices.Wasapi;

/// <summary>
/// The Windows device backend: real endpoints, real clocks.
/// </summary>
/// <remarks>
/// Enumeration and opening run on the control thread and allocate freely. Everything that happens
/// per callback lives in <see cref="WasapiCaptureStream"/>, which does not.
/// </remarks>
public sealed class WasapiBackend(ILogger<WasapiBackend> logger) : IAudioBackend
{
    readonly MMDeviceEnumerator enumerator = new();

    /// <inheritdoc />
    public string Id => "wasapi";

    /// <inheritdoc />
    /// <remarks>True: a real device's callback is the only honest clock in the system.</remarks>
    public bool CanProvideTimebase => true;

    /// <inheritdoc />
    public IReadOnlyList<AudioDeviceInfo> Enumerate(DeviceDirection direction)
    {
        List<AudioDeviceInfo> present = [];

        foreach (MMDevice device in enumerator.EnumerateAudioEndPoints(Flow(direction), DeviceState.Active))
        {
            using (device)
            {
                AudioDeviceInfo? info = Describe(device, direction);

                if (info is not null)
                {
                    present.Add(info);
                }
            }
        }

        return present;
    }

    /// <inheritdoc />
    public AudioDeviceInfo? DefaultDevice(DeviceDirection direction)
    {
        try
        {
            // Multimedia rather than Console. The two differ on a machine where somebody has pointed
            // communications audio at a headset, and a mixer is closer to media than to a phone call.
            using MMDevice device = enumerator.GetDefaultAudioEndpoint(Flow(direction), Role.Multimedia);

            return Describe(device, direction);
        }
        catch (Exception failure) when (failure is COMException or ArgumentException)
        {
            // No default endpoint at all. A machine with the sound card disabled reaches here, and it
            // is a machine the engine still has to start on.
            return null;
        }
    }

    /// <inheritdoc />
    public ICaptureStream OpenCapture(AudioDeviceId deviceId, CaptureOptions options)
    {
        MMDevice device = Resolve(deviceId);
        AudioClient client = device.CreateAudioClient();

        try
        {
            ShareMode granted = Initialise(client, deviceId, options.ShareMode, options.BufferDuration);
            WaveFormat format = client.MixFormat;

            return new WasapiCaptureStream(
                deviceId,
                device,
                client,
                new AudioStreamFormat(format.SampleRate, format.Channels, client.BufferSize, granted),
                format,
                logger);
        }
        catch
        {
            client.Dispose();
            device.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public IRenderStream OpenRender(AudioDeviceId deviceId, RenderOptions options)
    {
        MMDevice device = Resolve(deviceId);
        AudioClient client = device.CreateAudioClient();

        try
        {
            ShareMode granted = Initialise(client, deviceId, options.ShareMode, options.BufferDuration);
            WaveFormat format = client.MixFormat;

            return new WasapiRenderStream(
                deviceId,
                device,
                client,
                new AudioStreamFormat(format.SampleRate, format.Channels, client.BufferSize, granted),
                format,
                logger);
        }
        catch
        {
            client.Dispose();
            device.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public void Dispose() => enumerator.Dispose();

    /// <summary>
    /// Opens the client, preferring the requested share mode and falling back rather than failing.
    /// </summary>
    /// <returns>The share mode actually granted.</returns>
    ShareMode Initialise(AudioClient client, AudioDeviceId deviceId, ShareMode requested, TimeSpan bufferDuration)
    {
        // A TimeSpan tick and a WASAPI REFERENCE_TIME are both a hundred nanoseconds, which is a
        // coincidence worth stating rather than a conversion worth writing.
        long duration = bufferDuration.Ticks;

        if (requested == ShareMode.Exclusive && TryInitialiseExclusive(client, deviceId, duration))
        {
            return ShareMode.Exclusive;
        }

        // Shared mode takes the engine's format and a periodicity of zero; asking for anything else
        // is what makes it fail.
        client.Initialize(
            AudioClientShareMode.Shared,
            AudioClientStreamFlags.EventCallback,
            duration,
            0,
            client.MixFormat,
            Guid.Empty);

        return ShareMode.Shared;
    }

    bool TryInitialiseExclusive(AudioClient client, AudioDeviceId deviceId, long duration)
    {
        WaveFormat format = client.MixFormat;

        try
        {
            if (!client.IsFormatSupported(AudioClientShareMode.Exclusive, format))
            {
                // Loud on purpose. A session that quietly fell back to shared has a different
                // latency budget than the one it was rehearsed with, and the operator finds out
                // during the meeting rather than before it.
                logger.LogWarning(
                    "{DeviceId} refused exclusive mode at {SampleRate} Hz {ChannelCount} ch. Falling back to shared: "
                    + "Windows will mix and may resample this device without telling us.",
                    deviceId,
                    format.SampleRate,
                    format.Channels);

                return false;
            }

            client.Initialize(
                AudioClientShareMode.Exclusive,
                AudioClientStreamFlags.EventCallback,
                duration,
                duration,
                format,
                Guid.Empty);

            return true;
        }
        catch (Exception error)
        {
            logger.LogWarning(
                error,
                "{DeviceId} failed to open in exclusive mode. Falling back to shared.",
                deviceId);

            return false;
        }
    }

    MMDevice Resolve(AudioDeviceId deviceId)
    {
        try
        {
            MMDevice device = enumerator.GetDevice(deviceId.Value);

            if (device.State != DeviceState.Active)
            {
                device.Dispose();
                throw new DeviceNotFoundException(deviceId);
            }

            return device;
        }
        catch (Exception error) when (error is not DeviceNotFoundException)
        {
            throw new DeviceNotFoundException(deviceId);
        }
    }

    AudioDeviceInfo? Describe(MMDevice device, DeviceDirection direction)
    {
        try
        {
            using AudioClient client = device.CreateAudioClient();
            WaveFormat format = client.MixFormat;

            return new AudioDeviceInfo(
                new AudioDeviceId(device.ID),
                device.FriendlyName,
                direction,
                format.Channels,
                format.SampleRate,

                // A virtual endpoint is never exclusive, whatever it claims. Another application has
                // to keep using it at the same time - that is the whole point of one - and taking it
                // exclusively would lock Teams or OBS out of the device VAM exists to share with them.
                SupportsExclusiveMode: !IsVirtual(device.FriendlyName)
                    && client.IsFormatSupported(AudioClientShareMode.Exclusive, format),
                IsVirtual: IsVirtual(device.FriendlyName),
                ContainerId: ContainerOf(device));
        }
        catch (Exception error)
        {
            // A device can be present and still refuse to describe itself - another application
            // holds it exclusively, or a driver is mid-restart. Leaving it out of the list is
            // right: it is not openable, and a strip bound to it would fail at the worst moment.
            logger.LogWarning(error, "Skipping {DeviceName}: it is present but would not describe itself.", device.FriendlyName);
            return null;
        }
    }

    /// <summary>
    /// Whether an endpoint comes from a virtual driver rather than from hardware.
    /// </summary>
    /// <remarks>
    /// By name, which is not identity and is not pretending to be. Nothing above the backend
    /// interface branches on this - it is used to derive mix-minus and to tell an operator what is
    /// available, and the moment engine code asks "is this virtual" the abstraction has leaked.
    /// </remarks>
    static bool IsVirtual(string friendlyName) => VirtualDriver.Recognise(friendlyName) is not null;

    /// <summary>
    /// Which physical device an endpoint belongs to.
    /// </summary>
    /// <remarks>
    /// Windows groups the endpoints of one piece of hardware under a container. It is how a
    /// speakerphone's microphone and its speaker can be recognised as the same object, which is what
    /// mix-minus needs and what no amount of comparing friendly names reliably gives.
    /// </remarks>
    static string ContainerOf(MMDevice device)
    {
        // Written out because NAudio does not name this one. It is PKEY_Device_ContainerId from
        // devpkey.h, and it is the documented way Windows says "these endpoints are one object".
        PropertyKey key = new()
        {
            formatId = new Guid("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c"),
            propertyId = 2
        };

        try
        {
            if (!device.Properties.Contains(key))
            {
                return string.Empty;
            }

            return device.Properties[key].Value?.ToString() ?? string.Empty;
        }
        catch (Exception failure) when (failure is COMException or InvalidOperationException or NotSupportedException)
        {
            // A driver that does not publish one. Pairing simply cannot be derived for it, which is
            // the same position as before asking.
            return string.Empty;
        }
    }

    static DataFlow Flow(DeviceDirection direction) => direction switch
    {
        DeviceDirection.Capture => DataFlow.Capture,
        DeviceDirection.Render => DataFlow.Render,
        _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "Unknown device direction.")
    };
}
