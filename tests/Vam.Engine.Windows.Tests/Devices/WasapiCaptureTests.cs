using Microsoft.Extensions.Logging.Abstractions;
using Vam.Engine.Devices.Abstractions;
using Vam.Engine.Windows.Devices.Wasapi;
using Vam.TestKit.Harness;
using Xunit;

namespace Vam.Engine.Windows.Tests.Devices;

/// <summary>
/// VAM-011 against real devices. Everything here needs hardware and none of it runs in CI.
/// </summary>
/// <remarks>
/// A hosted runner has no microphone, so these tests would either fail there or, worse, pass
/// vacuously by finding nothing to test. `VAM_HARDWARE=1` opts a machine in.
/// </remarks>
public class WasapiCaptureTests(ITestOutputHelper output)
{
    const int DevicesWanted = 2;

    static readonly TimeSpan BufferDuration = TimeSpan.FromMilliseconds(20);
    static readonly TimeSpan CaptureWindow = TimeSpan.FromSeconds(5);

    [Fact(
        Skip = "Needs real audio devices. Set VAM_HARDWARE=1 to run.",
        SkipType = typeof(HardwareTests),
        SkipUnless = nameof(HardwareTests.IsEnabled))]
    [Trait("Category", TestCategories.NeedsHardware)]
    public void EnumerationFindsDevicesAndDescribesThem()
    {
        using WasapiBackend backend = new(NullLogger<WasapiBackend>.Instance);

        IReadOnlyList<AudioDeviceInfo> capture = backend.Enumerate(DeviceDirection.Capture);
        IReadOnlyList<AudioDeviceInfo> render = backend.Enumerate(DeviceDirection.Render);

        Assert.NotEmpty(capture);
        Assert.NotEmpty(render);

        // Printed rather than merely asserted: the device inventory is an open question for this
        // project, and a run that goes green without saying what it found answers nothing.
        foreach (AudioDeviceInfo device in capture.Concat(render))
        {
            output.WriteLine(
                $"{device.Direction,-7} {device.FriendlyName,-46} {device.NominalSampleRate,6} Hz "
                + $"{device.ChannelCount} ch  exclusive={device.SupportsExclusiveMode}");
        }

        foreach (AudioDeviceInfo device in capture.Concat(render))
        {
            // The identity is what gets persisted, so an empty one would silently lose a strip's
            // binding across a restart.
            Assert.False(device.Id.IsNone);
            Assert.False(string.IsNullOrWhiteSpace(device.FriendlyName));
            Assert.InRange(device.NominalSampleRate, 8000, 384000);
            Assert.InRange(device.ChannelCount, 1, 64);
        }

        // The claim VAM-013 rests on, checked against whatever is actually plugged in rather than
        // against the two identical Jabras the design was written around.
        int distinctNames = capture.Select(device => device.FriendlyName).Distinct(StringComparer.Ordinal).Count();
        int distinctIds = capture.Select(device => device.Id).Distinct().Count();

        Assert.Equal(capture.Count, distinctIds);
        Assert.True(
            distinctNames <= capture.Count,
            "Friendly names cannot outnumber devices; something is wrong with enumeration.");
    }

    [Fact(
        Skip = "Needs real audio devices. Set VAM_HARDWARE=1 to run.",
        SkipType = typeof(HardwareTests),
        SkipUnless = nameof(HardwareTests.IsEnabled))]
    [Trait("Category", TestCategories.NeedsHardware)]
    public void TwoDevicesCaptureAtOnceWithoutAllocatingOnEitherCallback()
    {
        using WasapiBackend backend = new(NullLogger<WasapiBackend>.Instance);

        IReadOnlyList<AudioDeviceInfo> devices = backend.Enumerate(DeviceDirection.Capture);

        Assert.True(
            devices.Count >= DevicesWanted,
            $"This test needs {DevicesWanted} capture devices and the machine has {devices.Count}.");

        List<ICaptureStream> streams = [];
        List<CaptureProbe> probes = [];

        try
        {
            foreach (AudioDeviceInfo device in devices.Take(DevicesWanted))
            {
                // Shared rather than exclusive, deliberately: a test that locks the operator's
                // microphone out of every other application is a test nobody runs twice.
                ICaptureStream stream = backend.OpenCapture(
                    device.Id,
                    new CaptureOptions(ShareMode.Shared, BufferDuration));

                CaptureProbe probe = new();

                streams.Add(stream);
                probes.Add(probe);

                stream.Start(probe.OnSamplesCaptured);
            }

            Thread.Sleep(CaptureWindow);

            for (int index = 0; index < streams.Count; index++)
            {
                ICaptureStream stream = streams[index];
                CaptureProbe probe = probes[index];
                AudioDeviceInfo device = devices[index];

                output.WriteLine(
                    $"{device.FriendlyName,-46} {stream.Format.SampleRate} Hz {stream.Format.ChannelCount} ch "
                    + $"{stream.Format.ShareMode}, buffer {stream.Format.BufferFrames} frames");
                output.WriteLine(
                    $"    {probe.FramesCaptured} frames over {probe.CallbackCount} callbacks, peak {probe.PeakLevel:F4}, "
                    + $"{probe.AllocatedBytesInSteadyState} bytes allocated across {probe.MeasuredCallbacks} measured callbacks");

                Assert.Equal(DeviceStreamState.Running, stream.State);

                // Frames within an order of magnitude of what the rate implies. Exact would be
                // wrong to assert - the window is wall-clock and the device is free-running, which
                // is the entire premise of the project.
                long expected = (long)(stream.Format.SampleRate * CaptureWindow.TotalSeconds);

                Assert.True(
                    probe.FramesCaptured > expected / 2,
                    $"{device.FriendlyName} delivered {probe.FramesCaptured} frames against roughly {expected} expected.");

                Assert.Equal(stream.Format.ChannelCount, probe.ChannelCount);

                Assert.True(
                    probe.MeasuredCallbacks > 0,
                    $"{device.FriendlyName} never got past the warm-up, so nothing was measured.");

                // The criterion, and the reason this whole path avoids NAudio's event wrappers.
                Assert.True(
                    probe.AllocatedBytesInSteadyState == 0,
                    $"{device.FriendlyName} allocated {probe.AllocatedBytesInSteadyState} bytes across "
                    + $"{probe.MeasuredCallbacks} callbacks on the device thread.");
            }
        }
        finally
        {
            foreach (ICaptureStream stream in streams)
            {
                stream.Dispose();
            }
        }
    }

    [Fact(
        Skip = "Needs real audio devices. Set VAM_HARDWARE=1 to run.",
        SkipType = typeof(HardwareTests),
        SkipUnless = nameof(HardwareTests.IsEnabled))]
    [Trait("Category", TestCategories.NeedsHardware)]
    public void AskingForExclusiveEitherGetsItOrFallsBackWithoutThrowing()
    {
        using WasapiBackend backend = new(NullLogger<WasapiBackend>.Instance);

        AudioDeviceInfo device = backend.Enumerate(DeviceDirection.Capture)[0];

        using ICaptureStream stream = backend.OpenCapture(
            device.Id,
            new CaptureOptions(ShareMode.Exclusive, BufferDuration));

        // Whichever way it went, the stream reports what it really got. A silent downgrade is the
        // failure this criterion exists to prevent: a session that fell back to shared has a
        // different latency budget than the one it was rehearsed with.
        output.WriteLine($"{device.FriendlyName}: asked for exclusive, got {stream.Format.ShareMode}");

        Assert.True(stream.Format.ShareMode is ShareMode.Exclusive or ShareMode.Shared);
        Assert.Equal(DeviceStreamState.Stopped, stream.State);

        if (stream.Format.ShareMode == ShareMode.Shared)
        {
            Assert.False(
                device.SupportsExclusiveMode,
                $"{device.FriendlyName} was advertised as supporting exclusive mode and then refused it.");
        }
    }
}
