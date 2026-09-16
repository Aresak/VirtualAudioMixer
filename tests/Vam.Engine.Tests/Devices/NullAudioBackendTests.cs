using Vam.Engine.Devices.Abstractions;
using Vam.TestKit.Allocations;
using Vam.TestKit.Devices;
using Vam.TestKit.Harness;
using Xunit;

namespace Vam.Engine.Tests.Devices;

/// <summary>
/// VAM-009's acceptance criteria: the null backend drives a full capture-and-render cycle, a
/// capture callback allocates nothing, and no NAudio type is reachable from the abstraction.
/// </summary>
public class NullAudioBackendTests
{
    static readonly TimeSpan BufferDuration = TimeSpan.FromMilliseconds(10);

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ACaptureAndRenderCycleCarriesEverySampleThrough()
    {
        using NullAudioBackend backend = new();

        AudioDeviceInfo microphone = backend.AddDevice(
            DeviceDirection.Capture,
            new NullDeviceOptions("Jabra Speak 750", Signal: NullSignal.Ramp));

        AudioDeviceInfo headphones = backend.AddDevice(
            DeviceDirection.Render,
            new NullDeviceOptions("Realtek - headphone"));

        using NullCaptureStream capture =
            (NullCaptureStream)backend.OpenCapture(microphone.Id, new CaptureOptions(ShareMode.Exclusive, BufferDuration));

        using NullRenderStream render =
            (NullRenderStream)backend.OpenRender(headphones.Id, new RenderOptions(ShareMode.Shared, BufferDuration));

        // Stands in for the ring buffer: capture writes, render reads, neither waits for the other.
        CaptureSink sink = new(capture.Format.BufferFrames * capture.Format.ChannelCount);
        int readPosition = 0;

        capture.Start(sink.OnSamplesCaptured);
        render.Start((destination, frameCount) =>
        {
            ReadOnlySpan<float> available = sink.Written[readPosition..];
            int supplied = Math.Min(frameCount, available.Length);
            available[..supplied].CopyTo(destination);
            readPosition += supplied;
            return supplied;
        });

        capture.PumpBuffer();
        render.PumpBuffer();

        Assert.Equal(DeviceStreamState.Running, capture.State);
        Assert.Equal(capture.Format.BufferFrames, sink.LastFrameCount);
        Assert.Equal(0, sink.DroppedSamples);
        Assert.Equal(0, render.UnderrunCount);

        // The ramp counts one per frame, so the played buffer proves nothing was lost or reordered.
        ReadOnlySpan<float> played = render.LastBuffer;
        Assert.Equal(capture.Format.BufferFrames, played.Length);

        for (int frame = 0; frame < played.Length; frame++)
        {
            Assert.Equal(frame, played[frame]);
        }
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ACaptureCallbackAllocatesNothing()
    {
        using NullAudioBackend backend = new();

        AudioDeviceInfo microphone = backend.AddDevice(
            DeviceDirection.Capture,
            new NullDeviceOptions("Mayor 180", Signal: NullSignal.Tone));

        using NullCaptureStream capture =
            (NullCaptureStream)backend.OpenCapture(microphone.Id, new CaptureOptions(ShareMode.Exclusive, BufferDuration));

        CaptureSink sink = new(capture.Format.BufferFrames * capture.Format.ChannelCount);
        capture.Start(sink.OnSamplesCaptured);

        // The sink is reset each time or it fills up and starts counting drops instead of copying,
        // which would measure a different code path than the one that matters.
        AllocationAssert.None((capture, sink), static state =>
        {
            state.sink.Reset();
            state.capture.PumpBuffer();
        });
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void TheEngineDoesNotReferenceNAudio()
    {
        // NAudio belongs in Vam.Engine.Windows. The moment it is reachable from here, the backend
        // interface has stopped being a seam and the engine has stopped being testable off Windows.
        IEnumerable<string?> references = typeof(IAudioBackend).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name);

        Assert.DoesNotContain(
            references,
            name => name is not null && name.Contains("NAudio", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void OpeningAnAbsentDeviceNamesIt()
    {
        using NullAudioBackend backend = new();
        AudioDeviceId missing = new("null:Capture:404");

        DeviceNotFoundException failure = Assert.Throws<DeviceNotFoundException>(
            () => backend.OpenCapture(missing, new CaptureOptions(ShareMode.Shared, BufferDuration)));

        Assert.Equal(missing, failure.DeviceId);
        Assert.Contains("null:Capture:404", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void RefusedExclusiveModeFallsBackToSharedAndSaysSo()
    {
        using NullAudioBackend backend = new();

        AudioDeviceInfo device = backend.AddDevice(
            DeviceDirection.Capture,
            new NullDeviceOptions("Cheap USB dongle", SupportsExclusiveMode: false));

        using ICaptureStream capture =
            backend.OpenCapture(device.Id, new CaptureOptions(ShareMode.Exclusive, BufferDuration));

        // Falling back is allowed. Falling back silently is not - the granted mode is reported, so
        // a session cannot quietly acquire a different latency budget than it had in rehearsal.
        Assert.Equal(ShareMode.Shared, capture.Format.ShareMode);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void TwoDevicesSharingAFriendlyNameStillHaveDifferentIdentities()
    {
        using NullAudioBackend backend = new();

        // The room has two identical Jabras. Identity by name would collapse them into one.
        AudioDeviceInfo left = backend.AddDevice(DeviceDirection.Capture, new NullDeviceOptions("Jabra Speak 750"));
        AudioDeviceInfo right = backend.AddDevice(DeviceDirection.Capture, new NullDeviceOptions("Jabra Speak 750"));

        Assert.Equal(left.FriendlyName, right.FriendlyName);
        Assert.NotEqual(left.Id, right.Id);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ADeviceRunsAtItsDriftedRateRatherThanItsNominalOne()
    {
        using NullAudioBackend backend = new();

        AudioDeviceInfo device = backend.AddDevice(
            DeviceDirection.Capture,
            new NullDeviceOptions("Behringer UCA222", NominalSampleRate: 48000, DriftPpm: 50.0));

        using NullCaptureStream capture =
            (NullCaptureStream)backend.OpenCapture(device.Id, new CaptureOptions(ShareMode.Shared, BufferDuration));

        CaptureSink sink = new(capture.Format.BufferFrames * 200);
        capture.Start(sink.OnSamplesCaptured);

        capture.Pump(TimeSpan.FromSeconds(1));

        // 50 ppm fast over one second is 48_002_4 frames, and the fractional remainder carries
        // rather than being rounded away - which is what makes hour three reproducible in a test.
        Assert.Equal(48002, capture.FramesCaptured);
        Assert.Equal(48000, capture.Format.SampleRate);
        Assert.Equal(48002.4, capture.EffectiveSampleRate, 1);
    }
}
