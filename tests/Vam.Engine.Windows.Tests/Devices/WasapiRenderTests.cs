using Microsoft.Extensions.Logging.Abstractions;
using Vam.Engine.Devices.Abstractions;
using Vam.Engine.Windows.Devices.Wasapi;
using Vam.TestKit.Harness;
using Xunit;

namespace Vam.Engine.Windows.Tests.Devices;

/// <summary>
/// VAM-012 against real outputs. Needs hardware; makes a quiet noise while it runs.
/// </summary>
public class WasapiRenderTests(ITestOutputHelper output)
{
    static readonly TimeSpan BufferDuration = TimeSpan.FromMilliseconds(20);
    static readonly TimeSpan RenderWindow = TimeSpan.FromSeconds(3);

    [Fact(
        Skip = "Needs real audio devices. Set VAM_HARDWARE=1 to run.",
        SkipType = typeof(HardwareTests),
        SkipUnless = nameof(HardwareTests.IsEnabled))]
    [Trait("Category", TestCategories.NeedsHardware)]
    public void ADeviceRendersATonePulledFromTheGraphWithoutAllocating()
    {
        using WasapiBackend backend = new(NullLogger<WasapiBackend>.Instance);

        AudioDeviceInfo device = backend.Enumerate(DeviceDirection.Render)[0];

        using IRenderStream stream = backend.OpenRender(
            device.Id,
            new RenderOptions(ShareMode.Shared, BufferDuration));

        RenderProbe probe = new(stream.Format.SampleRate, stream.Format.ChannelCount);

        stream.Start(probe.OnBufferNeeded);
        Thread.Sleep(RenderWindow);

        output.WriteLine(
            $"{device.FriendlyName}: {stream.Format.SampleRate} Hz {stream.Format.ChannelCount} ch "
            + $"{stream.Format.ShareMode}, buffer {stream.Format.BufferFrames} frames");
        output.WriteLine(
            $"    {probe.FramesWritten} frames over {probe.CallbackCount} buffers, {stream.UnderrunCount} underruns, "
            + $"{probe.AllocatedBytesInSteadyState} bytes across {probe.MeasuredCallbacks} measured buffers");

        Assert.Equal(DeviceStreamState.Running, stream.State);

        long expected = (long)(stream.Format.SampleRate * RenderWindow.TotalSeconds);

        Assert.True(
            probe.FramesWritten > expected / 2,
            $"Only {probe.FramesWritten} frames were pulled against roughly {expected} expected.");

        // The delegate filled everything it was asked for every time, so nothing should have been
        // counted. An underrun here would mean the pull model is asking for more than it hands over.
        Assert.Equal(0, stream.UnderrunCount);

        Assert.True(probe.MeasuredCallbacks > 0, "Never got past the warm-up, so nothing was measured.");
        Assert.True(
            probe.AllocatedBytesInSteadyState == 0,
            $"The render thread allocated {probe.AllocatedBytesInSteadyState} bytes across "
            + $"{probe.MeasuredCallbacks} buffers.");
    }

    [Fact(
        Skip = "Needs real audio devices. Set VAM_HARDWARE=1 to run.",
        SkipType = typeof(HardwareTests),
        SkipUnless = nameof(HardwareTests.IsEnabled))]
    [Trait("Category", TestCategories.NeedsHardware)]
    public void AGraphThatCannotKeepUpProducesCountedSilenceRatherThanAStall()
    {
        using WasapiBackend backend = new(NullLogger<WasapiBackend>.Instance);

        AudioDeviceInfo device = backend.Enumerate(DeviceDirection.Render)[0];

        using IRenderStream stream = backend.OpenRender(
            device.Id,
            new RenderOptions(ShareMode.Shared, BufferDuration));

        // Half of every buffer, every time. A graph that has not finished mixing looks exactly like
        // this from here, and the device must keep asking rather than waiting for it to catch up.
        RenderProbe probe = new(stream.Format.SampleRate, stream.Format.ChannelCount, fillFramesOf: 50);

        stream.Start(probe.OnBufferNeeded);
        Thread.Sleep(RenderWindow);

        output.WriteLine($"{device.FriendlyName}: {stream.UnderrunCount} underruns over {probe.CallbackCount} buffers");

        // Still running is the assertion that matters. Blocking here to wait for audio that has not
        // been mixed yet would turn one missing block into a stopped device.
        Assert.Equal(DeviceStreamState.Running, stream.State);
        Assert.True(stream.UnderrunCount > 0, "A deliberate shortfall was not counted.");
        Assert.True(probe.CallbackCount > 10, "The device stopped asking, which is the stall this forbids.");
    }
}
