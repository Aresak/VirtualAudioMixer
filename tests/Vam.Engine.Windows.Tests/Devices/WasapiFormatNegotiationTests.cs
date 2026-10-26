using Microsoft.Extensions.Logging.Abstractions;
using NAudio.Wave;
using Vam.Engine.Devices.Abstractions;
using Vam.Engine.Windows.Devices.Wasapi;
using Vam.TestKit.Harness;
using Xunit;

namespace Vam.Engine.Windows.Tests.Devices;

/// <summary>
/// The decision of 2026-09-16 against the real backend: the engine gets the rate it asked for, and
/// exclusive mode is taken only where the device's own format already carries it.
/// </summary>
/// <remarks>
/// Almost all of it needs hardware. The policy itself is checked without any, against the fake, in
/// <c>Vam.Engine.Tests.Devices.ShareModeAndRateTests</c>; what only a real device can answer is
/// whether WASAPI grants what this asks it for - and in particular whether an exclusive open at a
/// device's own format succeeds at all, which has never once happened in this project.
/// </remarks>
public class WasapiFormatNegotiationTests(ITestOutputHelper output)
{
    const int MixRateHz = 48000;
    const int FloatBits = 32;
    const int SurroundChannels = 6;

    static readonly TimeSpan BufferDuration = TimeSpan.FromMilliseconds(20);

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AnExtensibleFloatFormatIsOneTheSampleReaderCanRead()
    {
        // The shared-mode open builds this format itself when it asks Windows to convert to
        // something wider than stereo, and WasapiSampleReader refuses any format it has no
        // conversion for. If NAudio ever built a PCM format here instead, every surround endpoint
        // would fail at the open with no other test noticing.
        WaveFormatExtensible format = new(MixRateHz, FloatBits, SurroundChannels);
        WasapiSampleReader reader = new(format, 480);

        Assert.True(reader.IsFloatFormat);
        Assert.Equal(SurroundChannels, reader.ChannelCount);
    }

    [Fact(
        Skip = "Needs real audio devices. Set VAM_HARDWARE=1 to run.",
        SkipType = typeof(HardwareTests),
        SkipUnless = nameof(HardwareTests.IsEnabled))]
    [Trait("Category", TestCategories.NeedsHardware)]
    public void EveryDeviceReportsItsOwnFormatAsWellAsTheOneItPresents()
    {
        using WasapiBackend backend = new(NullLogger<WasapiBackend>.Instance);

        IReadOnlyList<AudioDeviceInfo> devices =
            [.. backend.Enumerate(DeviceDirection.Capture), .. backend.Enumerate(DeviceDirection.Render)];

        Assert.NotEmpty(devices);

        // Printed, because this is the open question. The device inventory of the room has never
        // been written down, and "which of these does Windows convert" is the thing this whole
        // change exists to answer.
        foreach (AudioDeviceInfo device in devices)
        {
            output.WriteLine(
                $"{device.Direction,-7} {device.FriendlyName,-46} presents {device.NominalSampleRate,6} Hz "
                + $"{device.ChannelCount} ch, own format {device.NativeSampleRate,6} Hz "
                + $"{device.NativeChannelCount} ch, exclusive={device.SupportsExclusiveMode}");
        }

        foreach (AudioDeviceInfo device in devices)
        {
            // Zero is a legitimate answer - a driver that publishes no device format - but anything
            // else has to be a rate, because the open decides on it.
            Assert.True(
                device.NativeSampleRate is 0 || device.NativeSampleRate is >= 8000 and <= 384000,
                $"{device.FriendlyName} reports an impossible own rate of {device.NativeSampleRate} Hz.");
        }
    }

    [Fact(
        Skip = "Needs real audio devices. Set VAM_HARDWARE=1 to run.",
        SkipType = typeof(HardwareTests),
        SkipUnless = nameof(HardwareTests.IsEnabled))]
    [Trait("Category", TestCategories.NeedsHardware)]
    public void ACaptureDeviceIsGrantedTheRateTheEngineAskedFor()
    {
        using WasapiBackend backend = new(NullLogger<WasapiBackend>.Instance);

        foreach (AudioDeviceInfo device in backend.Enumerate(DeviceDirection.Capture))
        {
            using ICaptureStream stream = backend.OpenCapture(
                device.Id,
                new CaptureOptions(ShareMode.Shared, BufferDuration, device.ChannelCount, MixRateHz));

            output.WriteLine(
                $"{device.FriendlyName,-46} granted {stream.Format.SampleRate} Hz {stream.Format.ChannelCount} ch "
                + $"{stream.Format.ShareMode}, converting={stream.Format.IsSystemConverting}");

            // The contract the supervisor relies on. A device that cannot be given this rate throws
            // out of OpenCapture rather than reaching here with a different one.
            Assert.Equal(MixRateHz, stream.Format.SampleRate);
            Assert.Equal(device.ChannelCount, stream.Format.ChannelCount);
        }
    }

    [Fact(
        Skip = "Needs real audio devices. Set VAM_HARDWARE=1 to run.",
        SkipType = typeof(HardwareTests),
        SkipUnless = nameof(HardwareTests.IsEnabled))]
    [Trait("Category", TestCategories.NeedsHardware)]
    public void ExclusiveIsNeverGrantedAtARateTheEngineCannotUse()
    {
        using WasapiBackend backend = new(NullLogger<WasapiBackend>.Instance);

        foreach (AudioDeviceInfo device in backend.Enumerate(DeviceDirection.Capture))
        {
            using ICaptureStream stream = backend.OpenCapture(
                device.Id,
                new CaptureOptions(ShareMode.Exclusive, BufferDuration, device.ChannelCount, MixRateHz));

            output.WriteLine(
                $"{device.FriendlyName,-46} own {device.NativeSampleRate,6} Hz -> {stream.Format.ShareMode}");

            // The trap, asserted. Exclusive mode runs at the device's own format, and taking it on a
            // device whose own format is 16 kHz would hand the mix graph a ratio of three.
            Assert.Equal(MixRateHz, stream.Format.SampleRate);

            if (stream.Format.ShareMode == ShareMode.Exclusive)
            {
                Assert.Equal(MixRateHz, device.NativeSampleRate);
                Assert.False(stream.Format.IsSystemConverting);
            }
        }
    }

    [Fact(
        Skip = "Needs a capture device whose own format runs at the mix rate. Set VAM_HARDWARE=1 to run.",
        SkipType = typeof(HardwareTests),
        SkipUnless = nameof(HardwareTests.IsEnabled))]
    [Trait("Category", TestCategories.NeedsHardware)]
    public void ExclusiveOpensAtADevicesOwnFormat()
    {
        using WasapiBackend backend = new(NullLogger<WasapiBackend>.Instance);
        int opened = 0;

        foreach (AudioDeviceInfo device in backend.Enumerate(DeviceDirection.Capture))
        {
            if (device.NativeSampleRate != MixRateHz || device.NativeChannelCount is 0)
            {
                continue;
            }

            // Exactly what VamEngine asks for when the share mode is exclusive: the device's own
            // width, because Windows presents nearly every mono microphone as stereo and exclusive
            // mode cannot grant a width the hardware does not have.
            using ICaptureStream stream = backend.OpenCapture(
                device.Id,
                new CaptureOptions(ShareMode.Exclusive, BufferDuration, device.NativeChannelCount, MixRateHz));

            output.WriteLine(
                $"{device.FriendlyName,-46} own {device.NativeSampleRate} Hz {device.NativeChannelCount} ch "
                + $"-> {stream.Format.ShareMode} {stream.Format.SampleRate} Hz {stream.Format.ChannelCount} ch");

            Assert.Equal(ShareMode.Exclusive, stream.Format.ShareMode);
            Assert.Equal(MixRateHz, stream.Format.SampleRate);
            Assert.False(stream.Format.IsSystemConverting);
            opened++;
        }

        Assert.True(opened > 0, "No capture device on this machine runs at the mix rate of its own, so nothing was proved.");
    }
}
