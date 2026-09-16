using Microsoft.Extensions.Logging.Abstractions;
using Vam.Engine.Devices.Abstractions;
using Vam.Engine.Windows.Devices.Wasapi;
using Vam.TestKit.Devices;
using Vam.TestKit.Dsp;
using Vam.TestKit.Harness;
using Xunit;

namespace Vam.Engine.Windows.Tests.Devices;

/// <summary>
/// L1 of the hardware end-to-end design: a tone played through a loopback comes back at the pitch
/// it left.
/// </summary>
/// <remarks>
/// <para>
/// The one test that would have caught the stereo-as-mono defect on its own. That microphone was
/// read as mono at half rate, an octave down, and it was audible immediately - but no test could
/// disagree, because <c>NullAudioBackend</c> honours whatever channel count it is handed. A fake
/// can only confirm that the code does what the code says.
/// </para>
/// <para>
/// Pitch, not level. Level depends on gain staging and on what sits between the two endpoints;
/// pitch does not.
/// </para>
/// <para>
/// Two gates, because having devices and having them wired together are different facts. See
/// <see cref="LoopbackRig"/>.
/// </para>
/// </remarks>
public class LoopbackToneTests(ITestOutputHelper output)
{
    const double ToneHz = 1000.0;
    const double TolerancePercent = 2.0;

    /// <summary>
    /// Below this nothing arrived at all, and whatever pitch was measured is a property of the
    /// noise floor. Not a level assertion - a sanity check that there is a signal to measure.
    /// </summary>
    const float SilenceFloor = 0.0005f;

    /// <summary>Seconds of slack on the sink, so a device that runs fast does not overflow it.</summary>
    const double SinkHeadroomSeconds = 1.0;

    static readonly TimeSpan BufferDuration = TimeSpan.FromMilliseconds(20);
    static readonly TimeSpan ToneDuration = TimeSpan.FromSeconds(2);

    [Fact(
        Skip = "Needs real audio devices. Set VAM_HARDWARE=1 to run.",
        SkipType = typeof(HardwareTests),
        SkipUnless = nameof(HardwareTests.IsEnabled))]
    [Trait("Category", TestCategories.NeedsHardware)]
    public void AToneComesBackAtThePitchItLeft()
    {
        if (!LoopbackRig.IsConfigured)
        {
            Assert.Skip(LoopbackRig.MissingDescription);
        }

        using WasapiBackend backend = new(NullLogger<WasapiBackend>.Instance);

        AudioDeviceInfo? renderDevice = Find(backend, DeviceDirection.Render, LoopbackRig.RenderDeviceId);
        AudioDeviceInfo? captureDevice = Find(backend, DeviceDirection.Capture, LoopbackRig.CaptureDeviceId);

        if (renderDevice is null || captureDevice is null)
        {
            Assert.Skip(DescribeAbsent(renderDevice, captureDevice));
            return;
        }

        ToneMeasurement measurement = PlayAndCapture(backend, renderDevice, captureDevice);

        output.WriteLine(
            $"    {ToneHz:F0} Hz out, {measurement.FrequencyHz:F1} Hz back "
            + $"({(measurement.FrequencyHz - ToneHz) / ToneHz * 100.0:+0.00;-0.00} %), "
            + $"peak {measurement.Peak:F4} at frame {measurement.WindowStartFrame}");

        Assert.True(
            measurement.Peak >= SilenceFloor,
            $"Nothing came back: peak was {measurement.Peak:F6}. The loopback is named but is not carrying audio.");

        Assert.InRange(
            measurement.FrequencyHz,
            ToneHz * (1.0 - (TolerancePercent / 100.0)),
            ToneHz * (1.0 + (TolerancePercent / 100.0)));
    }

    static AudioDeviceInfo? Find(WasapiBackend backend, DeviceDirection direction, AudioDeviceId deviceId)
    {
        foreach (AudioDeviceInfo device in backend.Enumerate(direction))
        {
            if (device.Id == deviceId)
            {
                return device;
            }
        }

        return null;
    }

    /// <summary>
    /// Names the end that is not there. A cable that has been unplugged, or a virtual driver that
    /// has been uninstalled, is not a failing test.
    /// </summary>
    /// <param name="renderDevice">What was found at the render end, or null.</param>
    /// <param name="captureDevice">What was found at the capture end, or null.</param>
    /// <returns>A sentence for the skip.</returns>
    static string DescribeAbsent(AudioDeviceInfo? renderDevice, AudioDeviceInfo? captureDevice)
    {
        if (renderDevice is null && captureDevice is null)
        {
            return $"Neither loopback endpoint is present: {LoopbackRig.RenderVariable} names "
                + $"{LoopbackRig.RenderDeviceId} and {LoopbackRig.CaptureVariable} names "
                + $"{LoopbackRig.CaptureDeviceId}, and this machine enumerates neither.";
        }

        return renderDevice is null
            ? $"{LoopbackRig.RenderVariable} names {LoopbackRig.RenderDeviceId}, which is not present."
            : $"{LoopbackRig.CaptureVariable} names {LoopbackRig.CaptureDeviceId}, which is not present.";
    }

    static string Describe(AudioStreamFormat format) =>
        $"{format.SampleRate} Hz {format.ChannelCount} ch {format.ShareMode}, {format.BufferFrames} frames";

    ToneMeasurement PlayAndCapture(
        WasapiBackend backend,
        AudioDeviceInfo renderDevice,
        AudioDeviceInfo captureDevice)
    {
        using IRenderStream render = backend.OpenRender(
            renderDevice.Id,
            new RenderOptions(ShareMode.Shared, BufferDuration));

        using ICaptureStream capture = backend.OpenCapture(
            captureDevice.Id,
            new CaptureOptions(ShareMode.Shared, BufferDuration));

        output.WriteLine($"out {renderDevice.FriendlyName,-44} {Describe(render.Format)}");
        output.WriteLine($"in  {captureDevice.FriendlyName,-44} {Describe(capture.Format)}");

        CaptureSink sink = new(
            (int)(capture.Format.SampleRate * (ToneDuration.TotalSeconds + SinkHeadroomSeconds))
            * capture.Format.ChannelCount);

        RenderProbe probe = new(render.Format.SampleRate, render.Format.ChannelCount, ToneHz);

        // Capture first. A tone that has already finished playing cannot be measured.
        capture.Start(sink.OnSamplesCaptured);
        render.Start(probe.OnBufferNeeded);

        Thread.Sleep(ToneDuration);

        render.Stop();
        capture.Stop();

        // Names the device before the analyser complains about an empty buffer. An endpoint that
        // opens, reports itself running and never ticks is a real failure mode, not a broken test.
        Assert.True(
            sink.TotalFrames > 0,
            $"{captureDevice.FriendlyName} delivered nothing across {ToneDuration.TotalSeconds:F0} s.");

        return new ToneAnalyser(capture.Format.SampleRate, capture.Format.ChannelCount).Measure(sink.Written);
    }
}
