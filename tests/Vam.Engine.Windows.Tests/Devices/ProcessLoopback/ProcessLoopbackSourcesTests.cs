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
    public void AReusedProcessIdDoesNotBringTheStripBackOnAnotherProgram()
    {
        AudioDeviceId byProcessId = new("process:4242");
        ScriptedProcessSnapshot processes = new();
        processes.Start(4242, 1, "msedge.exe");

        using RecordingLoggerFactory loggers = new();
        ProcessLoopbackSources sources = new(processes, loggers.CreateLogger("sources"));
        sources.Lease(byProcessId, Parse(byProcessId)).Dispose();

        processes.Exit(4242);
        processes.Start(4242, 1, "notepad.exe");

        Assert.Empty(sources.Describe(SampleRate));
        Assert.Throws<DeviceNotFoundException>(() => sources.Lease(byProcessId, Parse(byProcessId)));
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
        Assert.True(loggers.Mentions("VAM's tray"));
        Assert.True(loggers.Mentions(ProcessLoopbackSources.MeetingBrowserSetting));
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

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ASourceItsOnlyStripReleasedIsNoLongerListed()
    {
        ScriptedProcessSnapshot processes = new();
        processes.Start(200, 1, "msedge.exe");

        using RecordingLoggerFactory loggers = new();
        ProcessLoopbackSources sources = new(processes, loggers.CreateLogger("sources"));
        sources.Lease(Meeting, Parse(Meeting)).Dispose();

        // #105. Closing the capture alone keeps it listed; the browser may only have restarted.
        Assert.Single(sources.Describe(SampleRate));

        sources.Forget(Meeting);
        int taken = processes.TakenCount;

        Assert.True(sources.IsEmpty);
        Assert.Empty(sources.Describe(SampleRate));
        Assert.Equal(taken, processes.TakenCount);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AMistypedSourceIsForgottenWithoutEverBeingCaptured()
    {
        AudioDeviceId mistyped = new("process:msedg.exe");
        ScriptedProcessSnapshot processes = new();
        processes.Start(200, 1, "msedge.exe");

        using RecordingLoggerFactory loggers = new();
        ProcessLoopbackSources sources = new(processes, loggers.CreateLogger("sources"));

        Assert.Throws<DeviceNotFoundException>(() => sources.Lease(mistyped, Parse(mistyped)));
        Assert.False(sources.IsEmpty);

        sources.Forget(mistyped);

        Assert.True(sources.IsEmpty);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ForgettingASourceStillCapturedKeepsItsClaimUntilTheCaptureCloses()
    {
        ScriptedProcessSnapshot processes = new();
        processes.Start(200, 1, "msedge.exe");

        using RecordingLoggerFactory loggers = new();
        ProcessLoopbackSources sources = new(processes, loggers.CreateLogger("sources"));
        ProcessCaptureLease lease = sources.Lease(Meeting, Parse(Meeting));

        sources.Forget(Meeting);

        Assert.Throws<InvalidOperationException>(() => sources.Lease(Meeting, Parse(Meeting)));

        lease.Dispose();

        using ProcessCaptureLease reopened = sources.Lease(Meeting, Parse(Meeting));
        Assert.Equal(200, reopened.Process.ProcessId);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AStartHandedToTheBrowserVamAlreadyStartedKeepsTheFirstTree()
    {
        // #96. A second start on the same profile passes the URL on to the first and exits.
        ScriptedProcessSnapshot processes = new();
        processes.Start(200, 1, "msedge.exe");
        processes.Start(500, 4, "msedge.exe");

        using RecordingLoggerFactory loggers = new();
        ProcessLoopbackSources sources = new(processes, loggers.CreateLogger("sources"));
        sources.Launched(500, "msedge.exe");

        processes.Start(600, 4, "msedge.exe");
        sources.Launched(600, "msedge.exe");
        processes.Exit(600);

        using ProcessCaptureLease lease = sources.Lease(Meeting, Parse(Meeting));
        Assert.Equal(500, lease.Process.ProcessId);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AStartedBrowserDoesNotChangeASourceNamedByProcessId()
    {
        AudioDeviceId byProcessId = new("process:200");
        ScriptedProcessSnapshot processes = new();
        processes.Start(200, 1, "msedge.exe");
        processes.Start(500, 4, "msedge.exe");

        using RecordingLoggerFactory loggers = new();
        ProcessLoopbackSources sources = new(processes, loggers.CreateLogger("sources"));
        sources.Launched(500, "msedge.exe");

        using ProcessCaptureLease lease = sources.Lease(byProcessId, Parse(byProcessId));
        Assert.Equal(200, lease.Process.ProcessId);
    }

    static ProcessLoopbackId Parse(AudioDeviceId deviceId)
    {
        Assert.True(ProcessLoopbackId.TryParse(deviceId, out ProcessLoopbackId target));
        return target;
    }
}
