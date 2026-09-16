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
/// <para>
/// Enumeration and opening run on the control thread and allocate freely. Everything that happens
/// per callback lives in <see cref="WasapiCaptureStream"/>, which does not.
/// </para>
/// <para>
/// <b>The rate asked for is the rate granted, or the open throws.</b> Shared mode is asked for the
/// engine's own format and the system converts to it; exclusive mode is taken only where the
/// device's own format already runs at that rate, because exclusive at any other rate hands the
/// engine samples nothing converts. See the decision of 2026-09-16.
/// </para>
/// </remarks>
public sealed class WasapiBackend(ILogger<WasapiBackend> logger) : IAudioBackend
{
    /// <summary>
    /// What shared mode is opened with when the engine's format is not the device's mix format.
    /// </summary>
    /// <remarks>
    /// The two conversion flags are a pair - Windows requires the quality flag alongside the
    /// conversion one - and together they are the difference between the system resampling behind
    /// our back and the system resampling because it was told to, onto a rate we chose.
    /// </remarks>
    const AudioClientStreamFlags ConvertingFlags =
        AudioClientStreamFlags.EventCallback
        | AudioClientStreamFlags.AutoConvertPcm
        | AudioClientStreamFlags.SrcDefaultQuality;

    /// <summary>Bits per sample the engine speaks. Shared mode is float everywhere this will run.</summary>
    const int FloatBits = 32;

    /// <summary>Above this, WASAPI wants a channel mask, which means an extensible format.</summary>
    const int PlainFormatChannelLimit = 2;

    /// <summary>Bytes in a WAVEFORMATEX. A blob shorter than this cannot be one.</summary>
    const int MinimumFormatBytes = 18;

    /// <summary>Bytes in a WAVEFORMATEXTENSIBLE, which is the whole of what an extensible tag implies.</summary>
    const int ExtensibleFormatBytes = 40;

    /// <summary>Where WAVEFORMATEX keeps the size of whatever follows it.</summary>
    const int ExtraSizeOffset = 16;

    /// <summary>WAVE_FORMAT_EXTENSIBLE, as the tag at the front of the blob.</summary>
    const ushort ExtensibleTag = 0xFFFE;

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
        OpenedClient? opened = null;

        try
        {
            opened = Open(
                device,
                new StreamRequest(options.ShareMode, options.BufferDuration, options.SampleRate, options.ChannelCount));

            return new WasapiCaptureStream(deviceId, device, opened.Client, opened.Describe(), opened.Format, logger);
        }
        catch
        {
            opened?.Client.Dispose();
            device.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public IRenderStream OpenRender(AudioDeviceId deviceId, RenderOptions options)
    {
        MMDevice device = Resolve(deviceId);
        OpenedClient? opened = null;

        try
        {
            opened = Open(
                device,
                new StreamRequest(options.ShareMode, options.BufferDuration, options.SampleRate, options.ChannelCount));

            return new WasapiRenderStream(deviceId, device, opened.Client, opened.Describe(), opened.Format, logger);
        }
        catch
        {
            opened?.Client.Dispose();
            device.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public void Dispose() => enumerator.Dispose();

    /// <summary>
    /// Opens a client in the best mode this device can honestly be given.
    /// </summary>
    /// <param name="device">The endpoint.</param>
    /// <param name="request">What the engine needs.</param>
    /// <returns>An initialised client and what it was initialised with.</returns>
    OpenedClient Open(MMDevice device, StreamRequest request)
    {
        WaveFormat? native = NativeFormatOf(device);

        if (request.ShareMode == ShareMode.Exclusive
            && native is not null
            && CanTakeExclusive(device.FriendlyName, request, native)
            && TryOpenExclusive(device, request, native) is { } exclusive)
        {
            return exclusive;
        }

        return OpenShared(device, request, native?.SampleRate ?? 0);
    }

    /// <summary>
    /// Whether exclusive mode is worth asking this device for, and says why when it is not.
    /// </summary>
    /// <remarks>
    /// The whole of the 2026-09-16 decision sits in the second branch. Exclusive mode runs at the
    /// device's own format, and a device whose own format is not the engine's would be granted a
    /// rate nothing between here and the mix graph converts. Windows converting it in shared mode is
    /// the deliberate answer, not a fallback that failed.
    /// </remarks>
    /// <param name="deviceName">For the log line.</param>
    /// <param name="request">What the engine needs.</param>
    /// <param name="native">The device's own format.</param>
    /// <returns>Whether to try.</returns>
    bool CanTakeExclusive(string deviceName, StreamRequest request, WaveFormat native)
    {
        bool rateMatches = request.SampleRate is 0 || request.SampleRate == native.SampleRate;
        bool widthMatches = request.ChannelCount is 0 || request.ChannelCount == native.Channels;

        if (rateMatches && widthMatches)
        {
            return true;
        }

        logger.LogInformation(
            "{DeviceName} runs at {NativeRate} Hz {NativeChannels} ch of its own, and the engine needs "
            + "{WantedRate} Hz {WantedChannels} ch. Exclusive mode would deliver the device's format and nothing "
            + "converts it, so it is opened shared and Windows converts instead.",
            deviceName,
            native.SampleRate,
            native.Channels,
            request.SampleRate,
            request.ChannelCount);

        return false;
    }

    /// <summary>
    /// Asks for exclusive mode at the device's own format.
    /// </summary>
    /// <remarks>
    /// <c>Initialize</c> is the question rather than <c>IsFormatSupported</c>, because it is the one
    /// whose answer matters: a device can support a format and still refuse the buffer duration, and
    /// a refusal either way means the same thing here. The client is thrown away on refusal, since
    /// WASAPI does not promise anything about one whose initialisation failed.
    /// </remarks>
    /// <param name="device">The endpoint.</param>
    /// <param name="request">What the engine needs, for the buffer duration.</param>
    /// <param name="native">The device's own format, which is what exclusive mode runs at.</param>
    /// <returns>An initialised client, or null when the device refused.</returns>
    OpenedClient? TryOpenExclusive(MMDevice device, StreamRequest request, WaveFormat native)
    {
        // A TimeSpan tick and a WASAPI REFERENCE_TIME are both a hundred nanoseconds, which is a
        // coincidence worth stating rather than a conversion worth writing.
        long duration = request.BufferDuration.Ticks;
        AudioClient client = device.CreateAudioClient();

        try
        {
            client.Initialize(
                AudioClientShareMode.Exclusive,
                AudioClientStreamFlags.EventCallback,
                duration,
                duration,
                native,
                Guid.Empty);

            logger.LogInformation(
                "{DeviceName} is open exclusively at {SampleRate} Hz {ChannelCount} ch. Nothing else on this machine "
                + "can use it, and nothing resamples it.",
                device.FriendlyName,
                native.SampleRate,
                native.Channels);

            return new OpenedClient(client, native, ShareMode.Exclusive, native.SampleRate);
        }
        catch (Exception error)
        {
            client.Dispose();

            // Loud on purpose. A session that quietly fell back to shared has a different latency
            // budget than the one it was rehearsed with, and the operator finds out during the
            // meeting rather than before it.
            logger.LogWarning(
                error,
                "{DeviceName} refused exclusive mode at its own {SampleRate} Hz {ChannelCount} ch format. "
                + "Falling back to shared.",
                device.FriendlyName,
                native.SampleRate,
                native.Channels);

            return null;
        }
    }

    /// <summary>
    /// Opens shared, at the format the engine asked for.
    /// </summary>
    /// <param name="device">The endpoint.</param>
    /// <param name="request">What the engine needs.</param>
    /// <param name="deviceSampleRate">The device's own rate, carried through for telemetry.</param>
    /// <returns>An initialised client.</returns>
    OpenedClient OpenShared(MMDevice device, StreamRequest request, int deviceSampleRate)
    {
        AudioClient client = device.CreateAudioClient();

        try
        {
            WaveFormat mix = client.MixFormat;
            WaveFormat wanted = WantedFormat(mix, request);
            bool isConverting = wanted.SampleRate != mix.SampleRate || wanted.Channels != mix.Channels;

            // Shared mode takes a periodicity of zero; asking for anything else is what makes it fail.
            client.Initialize(
                AudioClientShareMode.Shared,
                isConverting ? ConvertingFlags : AudioClientStreamFlags.EventCallback,
                request.BufferDuration.Ticks,
                0,
                wanted,
                Guid.Empty);

            return new OpenedClient(client, wanted, ShareMode.Shared, deviceSampleRate);
        }
        catch
        {
            client.Dispose();
            throw;
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
            WaveFormat mix = client.MixFormat;
            WaveFormat? native = NativeFormatOf(device);

            return new AudioDeviceInfo(
                new AudioDeviceId(device.ID),
                device.FriendlyName,
                direction,
                mix.Channels,
                mix.SampleRate,

                // Asked at the device's own format, which is what exclusive mode actually runs at.
                // Asking it at the mix format is the defect this answered: every device refused,
                // every session fell back to shared, and the log said the hardware was at fault.
                //
                // A virtual endpoint is never exclusive, whatever it claims. Another application has
                // to keep using it at the same time - that is the whole point of one - and taking it
                // exclusively would lock Teams or OBS out of the device VAM exists to share with them.
                SupportsExclusiveMode: !IsVirtual(device.FriendlyName)
                    && native is not null
                    && client.IsFormatSupported(AudioClientShareMode.Exclusive, native),
                IsVirtual: IsVirtual(device.FriendlyName),
                ContainerId: ContainerOf(device),
                NativeSampleRate: native?.SampleRate ?? 0,
                NativeChannelCount: native?.Channels ?? 0);
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
    /// The format the engine wants from a shared stream.
    /// </summary>
    /// <remarks>
    /// Returns the mix format unchanged when nothing needs converting, so the common case opens
    /// exactly as it always did and carries none of the conversion flags.
    /// </remarks>
    /// <param name="mix">What shared mode presents by default.</param>
    /// <param name="request">What the engine needs.</param>
    /// <returns>The format to initialise with.</returns>
    static WaveFormat WantedFormat(WaveFormat mix, StreamRequest request)
    {
        int rate = request.SampleRate > 0 ? request.SampleRate : mix.SampleRate;
        int channels = request.ChannelCount > 0 ? request.ChannelCount : mix.Channels;

        if (rate == mix.SampleRate && channels == mix.Channels)
        {
            return mix;
        }

        // Float, because that is what the shared engine speaks and what WasapiSampleReader copies
        // without converting. Anything wider than stereo needs a channel mask, and that is what an
        // extensible format carries.
        return channels > PlainFormatChannelLimit
            ? new WaveFormatExtensible(rate, FloatBits, channels)
            : WaveFormat.CreateIeeeFloatWaveFormat(rate, channels);
    }

    /// <summary>
    /// The format the hardware itself runs at, which is not always the one it presents.
    /// </summary>
    /// <remarks>
    /// <c>PKEY_AudioEngine_DeviceFormat</c> is how Windows says what the device's own converter is
    /// doing, and it is the format an exclusive-mode stream would carry. The mix format is a
    /// different question - it is what the shared engine presents to applications, after its own
    /// conversion.
    /// </remarks>
    /// <param name="device">The endpoint.</param>
    /// <returns>The device's own format, or null when it does not publish one.</returns>
    static WaveFormat? NativeFormatOf(MMDevice device)
    {
        try
        {
            if (!device.Properties.Contains(PropertyKeys.PKEY_AudioEngine_DeviceFormat))
            {
                return null;
            }

            // A WAVEFORMATEX, or an extensible one, as a blob. Marshalled rather than parsed by
            // hand, because the same struct with a different cbSize is two different layouts.
            if (device.Properties[PropertyKeys.PKEY_AudioEngine_DeviceFormat].Value is not byte[] blob
                || blob.Length < RequiredBytes(blob))
            {
                return null;
            }

            unsafe
            {
                fixed (byte* start = blob)
                {
                    return WaveFormat.MarshalFromPtr((nint)start);
                }
            }
        }
        catch (Exception failure) when (failure is COMException or InvalidOperationException or NotSupportedException or ArgumentException)
        {
            // A driver that does not publish one, or publishes something unreadable. The caller's
            // answer to null is to open shared, which is where it would have ended up anyway.
            return null;
        }
    }

    /// <summary>
    /// How many bytes a device-format blob has to hold before it is safe to marshal.
    /// </summary>
    /// <remarks>
    /// Both questions, because a driver is free to publish a blob that disagrees with itself.
    /// <c>MarshalFromPtr</c> reads a whole WAVEFORMATEXTENSIBLE whenever the tag says extensible,
    /// whatever <c>cbSize</c> claims, so trusting <c>cbSize</c> alone would read off the end of the
    /// pinned array and into whatever is next on the heap.
    /// </remarks>
    /// <param name="blob">What the property store returned.</param>
    /// <returns>The length the blob must reach.</returns>
    static int RequiredBytes(byte[] blob)
    {
        if (blob.Length < MinimumFormatBytes)
        {
            return MinimumFormatBytes;
        }

        int declared = MinimumFormatBytes + BitConverter.ToUInt16(blob, ExtraSizeOffset);
        int implied = BitConverter.ToUInt16(blob, 0) == ExtensibleTag ? ExtensibleFormatBytes : MinimumFormatBytes;

        return Math.Max(declared, implied);
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
