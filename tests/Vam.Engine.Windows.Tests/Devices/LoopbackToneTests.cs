using Microsoft.Extensions.Logging.Abstractions;
using Vam.Engine.Devices;
using Vam.Engine.Devices.Abstractions;
using Vam.Engine.Windows.Devices.Wasapi;
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
/// Captured the way the engine captures: a <see cref="DeviceSupervisor"/> opens the device into a
/// <see cref="DeviceInputChannel"/>, which checks the granted format and writes into its ring, and
/// the tone is read back out with <see cref="DeviceInputChannel.Pull"/> at the ring's own width. The
/// width itself is the engine's rule restated rather than called - <c>VamEngine</c> keeps it private
/// - so a wrong rule there is not caught here; a ring and a stream that disagree is.
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

    /// <summary>The engine's rate, which the capture is opened at and the ring is drained at.</summary>
    const int MixRateHz = 48000;

    /// <summary>What the engine pulls per block.</summary>
    const int BlockFrames = 480;

    /// <summary>
    /// Seconds of slack in the ring, so a device that runs fast does not overflow it. The ring holds
    /// the whole tone and is drained afterwards, so nothing here races the device thread.
    /// </summary>
    const double RingHeadroomSeconds = 1.0;

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

    /// <summary>
    /// The endpoint an environment variable names, or null when it is not present.
    /// </summary>
    /// <remarks>
    /// Case-insensitively, unlike <see cref="AudioDeviceId"/>'s own ordinal comparison, and
    /// deliberately: WASAPI reports an identity with a lower-case GUID and Device Manager shows the
    /// same identity in upper case. Somebody copying one into the other gets told the device is not
    /// present and loses an hour to it. The engine still compares identities ordinally - there both
    /// sides came from the backend. Here one side was typed by a person.
    /// </remarks>
    /// <param name="backend">What to ask.</param>
    /// <param name="direction">Which way to look.</param>
    /// <param name="deviceId">The identity named.</param>
    /// <returns>The endpoint, or null.</returns>
    static AudioDeviceInfo? Find(WasapiBackend backend, DeviceDirection direction, AudioDeviceId deviceId)
    {
        foreach (AudioDeviceInfo device in backend.Enumerate(direction))
        {
            if (string.Equals(device.Id.Value, deviceId.Value, StringComparison.OrdinalIgnoreCase))
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
        // VamEngine.WidthOf in shared mode: the device's own count, never the strip's.
        int width = captureDevice.ChannelCount;

        using IRenderStream render = backend.OpenRender(
            renderDevice.Id,
            new RenderOptions(ShareMode.Shared, BufferDuration));

        using DeviceSupervisor supervisor = new(backend, new DeviceInputChannelRegistry(), NullLoggerFactory.Instance);

        // Track opens the stream, requires its format and starts it writing into the channel's ring.
        DeviceInputChannel channel = supervisor.Track(
            captureDevice.Id,
            new DeviceInputChannelOptions
            {
                NominalSampleRate = MixRateHz,
                ChannelCount = width,
                BlockFrames = BlockFrames,
                RingCapacityFrames = (int)(MixRateHz * (ToneDuration.TotalSeconds + RingHeadroomSeconds)),
                TargetFillFrames = MixRateHz / 40
            },
            new CaptureOptions(ShareMode.Shared, BufferDuration, width, MixRateHz));

        Assert.True(
            channel.State == DeviceStreamState.Running,
            $"The engine would not open {captureDevice.FriendlyName} at {MixRateHz} Hz {width} ch.");

        output.WriteLine($"out {renderDevice.FriendlyName,-44} {Describe(render.Format)}");
        output.WriteLine($"in  {captureDevice.FriendlyName,-44} {MixRateHz} Hz {width} ch into the ring");

        RenderProbe probe = new(render.Format.SampleRate, render.Format.ChannelCount, ToneHz);

        render.Start(probe.OnBufferNeeded);

        Thread.Sleep(ToneDuration);

        render.Stop();
        supervisor.Forget(captureDevice.Id);

        float[] captured = Drain(channel);

        // Names the device before the analyser complains about an empty buffer. An endpoint that
        // opens, reports itself running and never ticks is a real failure mode, not a broken test.
        Assert.True(
            captured.Length > 0,
            $"{captureDevice.FriendlyName} delivered nothing across {ToneDuration.TotalSeconds:F0} s.");

        return new ToneAnalyser(MixRateHz, channel.ChannelCount).Measure(captured);
    }

    /// <summary>Reads everything the ring holds, a block at a time, as the mix thread would.</summary>
    /// <param name="channel">The channel the device wrote into.</param>
    /// <returns>The captured audio, interleaved at the channel's width.</returns>
    static float[] Drain(DeviceInputChannel channel)
    {
        List<float> captured = [];
        float[] block = new float[BlockFrames * channel.ChannelCount];

        while (true)
        {
            int produced = channel.Pull(block);

            captured.AddRange(block.AsSpan(0, produced * channel.ChannelCount));

            if (produced < BlockFrames)
            {
                return [.. captured];
            }
        }
    }
}
