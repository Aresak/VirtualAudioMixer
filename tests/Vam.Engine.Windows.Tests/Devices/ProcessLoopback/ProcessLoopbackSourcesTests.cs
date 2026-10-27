using Vam.Engine.Devices.Abstractions;
using Vam.Engine.Windows.Devices.ProcessLoopback;
using Vam.TestKit.Harness;
using Vam.TestKit.Logging;
using Xunit;

namespace Vam.Engine.Windows.Tests.Devices.ProcessLoopback;

/// <summary>
/// #55. A named process source is present while its process runs and absent once it closes, which
/// is all the supervisor needs to treat a closed browser as an unplugged device.
/// </summary>
public class ProcessLoopbackSourcesTests
{
    const int SampleRate = 48000;

    static readonly AudioDeviceId Meeting = new("process:msedge.exe");

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void NothingIsListedUntilItIsNamed()
    {
        ScriptedProcessSnapshot processes = new();
        processes.Start(200, 1, "msedge.exe");

        using RecordingLoggerFactory loggers = new();
        ProcessLoopbackSources sources = new(processes, loggers.CreateLogger("sources"));

        Assert.True(sources.IsEmpty);
        Assert.Empty(sources.Describe(SampleRate));
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ANamedSourceIsPresentOnlyWhileItsProcessRuns()
    {
        ScriptedProcessSnapshot processes = new();
        using RecordingLoggerFactory loggers = new();
        ProcessLoopbackSources sources = new(processes, loggers.CreateLogger("sources"));

        // Named while the browser is closed: the open fails as an absent device's would.
        Assert.Throws<DeviceNotFoundException>(() => sources.Lease(Meeting, Parse(Meeting)));
        Assert.Empty(sources.Describe(SampleRate));

        processes.Start(200, 1, "msedge.exe");

        AudioDeviceInfo present = Assert.Single(sources.Describe(SampleRate));
        Assert.Equal(Meeting, present.Id);
        Assert.Equal(DeviceDirection.Capture, present.Direction);
        Assert.Equal(ProcessLoopbackSources.DefaultChannelCount, present.ChannelCount);
        Assert.False(present.SupportsExclusiveMode);

        processes.Exit(200);

        Assert.Empty(sources.Describe(SampleRate));
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ABrowserRestartedUnderTheCaptureIsAbsentUntilTheCaptureCloses()
    {
        ScriptedProcessSnapshot processes = new();
        processes.Start(200, 1, "msedge.exe");

        using RecordingLoggerFactory loggers = new();
        ProcessLoopbackSources sources = new(processes, loggers.CreateLogger("sources"));
        ProcessCaptureLease lease = sources.Lease(Meeting, Parse(Meeting));

        processes.Exit(200);
        processes.Start(300, 1, "msedge.exe");

        // Still listed would mean the supervisor never re-opens it, and the strip stays bound to a
        // process that is gone.
        Assert.Empty(sources.Describe(SampleRate));

        lease.Dispose();

        Assert.Single(sources.Describe(SampleRate));
        using ProcessCaptureLease reopened = sources.Lease(Meeting, Parse(Meeting));
        Assert.Equal(300, reopened.Process.ProcessId);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AnAmbiguousNameIsReportedAndNotOpened()
    {
        ScriptedProcessSnapshot processes = new();
        processes.Start(200, 1, "msedge.exe");
        processes.Start(300, 1, "msedge.exe");

        using RecordingLoggerFactory loggers = new();
        ProcessLoopbackSources sources = new(processes, loggers.CreateLogger("sources"));

        Assert.Throws<DeviceNotFoundException>(() => sources.Lease(Meeting, Parse(Meeting)));
        Assert.True(loggers.Mentions("own instance"));
        Assert.Empty(sources.Describe(SampleRate));
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void OneSourceIsCapturedOnce()
    {
        ScriptedProcessSnapshot processes = new();
        processes.Start(200, 1, "msedge.exe");

        using RecordingLoggerFactory loggers = new();
        ProcessLoopbackSources sources = new(processes, loggers.CreateLogger("sources"));
        using ProcessCaptureLease lease = sources.Lease(Meeting, Parse(Meeting));

        Assert.Throws<InvalidOperationException>(() => sources.Lease(Meeting, Parse(Meeting)));
    }

    static ProcessLoopbackId Parse(AudioDeviceId deviceId)
    {
        Assert.True(ProcessLoopbackId.TryParse(deviceId, out ProcessLoopbackId target));
        return target;
    }
}
