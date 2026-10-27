using Vam.Engine.Devices.Abstractions;
using Vam.Engine.Windows.Devices.ProcessLoopback;
using Vam.TestKit.Devices;
using Vam.TestKit.Harness;
using Vam.TestKit.Logging;
using Xunit;

namespace Vam.Engine.Windows.Tests.Devices.ProcessLoopback;

/// <summary>
/// #104. The glue between a <c>process:</c> source and the device list: the source is listed beside
/// the real devices, and closing its capture gives the source back so a restarted browser is
/// captured again.
/// </summary>
public class ProcessLoopbackBackendTests
{
    const int OutputSampleRate = 44100;

    static readonly AudioDeviceId Meeting = new("process:msedge.exe");
    static readonly CaptureOptions Stereo = new(ShareMode.Shared, TimeSpan.FromMilliseconds(20), ChannelCount: 2);

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ANamedSourceIsListedBesideTheDevicesOnlyWhileItsProcessRuns()
    {
        using Fixture fixture = new();
        AudioDeviceInfo microphone = fixture.Endpoints.AddDevice(DeviceDirection.Capture, new NullDeviceOptions("Lectern"));
        fixture.Endpoints.AddDevice(
            DeviceDirection.Render,
            new NullDeviceOptions("Speakers", NominalSampleRate: OutputSampleRate));
        fixture.Processes.Start(200, 1, "msedge.exe");

        Assert.DoesNotContain(fixture.Backend.Enumerate(DeviceDirection.Capture), device => device.Id == Meeting);

        using ICaptureStream stream = fixture.Backend.OpenCapture(Meeting, Stereo);

        IReadOnlyList<AudioDeviceInfo> present = fixture.Backend.Enumerate(DeviceDirection.Capture);
        AudioDeviceInfo source = Assert.Single(present, device => device.Id == Meeting);
        Assert.Contains(present, device => device.Id == microphone.Id);
        Assert.Equal(OutputSampleRate, source.NominalSampleRate);
        Assert.DoesNotContain(fixture.Backend.Enumerate(DeviceDirection.Render), device => device.Id == Meeting);

        fixture.Processes.Exit(200);

        present = fixture.Backend.Enumerate(DeviceDirection.Capture);
        Assert.DoesNotContain(present, device => device.Id == Meeting);
        Assert.Contains(present, device => device.Id == microphone.Id);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ClosingTheCaptureLetsABrowserRestartedUnderANewProcessIdBeCapturedAgain()
    {
        using Fixture fixture = new();
        fixture.Processes.Start(200, 1, "msedge.exe");

        ICaptureStream first = fixture.Backend.OpenCapture(Meeting, Stereo);

        fixture.Processes.Exit(200);
        fixture.Processes.Start(300, 1, "msedge.exe");

        Assert.DoesNotContain(fixture.Backend.Enumerate(DeviceDirection.Capture), device => device.Id == Meeting);

        first.Dispose();

        Assert.True(fixture.Opener.Opened[0].IsDisposed);
        Assert.Contains(fixture.Backend.Enumerate(DeviceDirection.Capture), device => device.Id == Meeting);

        using ICaptureStream second = fixture.Backend.OpenCapture(Meeting, Stereo);

        Assert.Equal(300, fixture.Opener.Opened[^1].Process.ProcessId);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AnOpenThatFailsGivesTheSourceBack()
    {
        using Fixture fixture = new();
        fixture.Processes.Start(200, 1, "msedge.exe");
        fixture.Opener.IsFailing = true;

        Assert.Throws<TimeoutException>(() => fixture.Backend.OpenCapture(Meeting, Stereo));

        fixture.Opener.IsFailing = false;

        using ICaptureStream stream = fixture.Backend.OpenCapture(Meeting, Stereo);

        Assert.Equal(200, fixture.Opener.Opened[^1].Process.ProcessId);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ADeviceThatIsNotAProcessOpensOnTheEndpoints()
    {
        using Fixture fixture = new();
        AudioDeviceInfo microphone = fixture.Endpoints.AddDevice(DeviceDirection.Capture, new NullDeviceOptions("Lectern"));

        using ICaptureStream stream = fixture.Backend.OpenCapture(microphone.Id, Stereo);

        Assert.Single(fixture.Endpoints.CaptureStreams);
        Assert.Empty(fixture.Opener.Opened);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ASourceNoStripWantsIsForgottenAndTheEndpointsAreTold()
    {
        using Fixture fixture = new();
        fixture.Processes.Start(200, 1, "msedge.exe");

        fixture.Backend.OpenCapture(Meeting, Stereo).Dispose();

        Assert.Contains(fixture.Backend.Enumerate(DeviceDirection.Capture), device => device.Id == Meeting);

        fixture.Backend.Forget(Meeting);

        Assert.DoesNotContain(fixture.Backend.Enumerate(DeviceDirection.Capture), device => device.Id == Meeting);
        Assert.Contains(Meeting, fixture.Endpoints.ForgottenDevices);
    }

    /// <summary>The backend over a null one, a scripted process list and stand-in captures.</summary>
    sealed class Fixture : IDisposable
    {
        readonly RecordingLoggerFactory loggers = new();

        public Fixture()
        {
            Backend = new ProcessLoopbackBackend(
                Endpoints,
                Opener,
                new ProcessLoopbackSources(Processes, loggers.CreateLogger("sources")));
        }

        public NullAudioBackend Endpoints { get; } = new();

        public ScriptedProcessSnapshot Processes { get; } = new();

        public StandInProcessOpener Opener { get; } = new();

        public ProcessLoopbackBackend Backend { get; }

        public void Dispose()
        {
            Backend.Dispose();
            loggers.Dispose();
        }
    }
}
