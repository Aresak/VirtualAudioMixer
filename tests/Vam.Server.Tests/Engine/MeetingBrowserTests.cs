using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Vam.Engine.Devices;
using Vam.Engine.Devices.Abstractions;
using Vam.Engine.Windows.Devices.ProcessLoopback;
using Vam.Server.Engine;
using Vam.TestKit.Devices;
using Vam.TestKit.Harness;
using Xunit;

namespace Vam.Server.Tests.Engine;

/// <summary>#96. What starting the meeting browser runs, and where it is configured.</summary>
public class MeetingBrowserTests
{
    const string Chrome = @"C:\Program Files\Google\Chrome\Application\chrome.exe";

    static readonly AudioDeviceId Meeting = new("process:chrome.exe");

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void TheBrowserGetsAProfileOfItsOwnAndTheMeetingsAddress()
    {
        ProcessStartInfo start = MeetingBrowser.StartInfo(
            new MeetingBrowserOptions { Executable = Chrome, Url = "https://meet.example/council" });

        Assert.Equal(Chrome, start.FileName);
        Assert.StartsWith("--user-data-dir=", start.Arguments, StringComparison.Ordinal);
        Assert.EndsWith(" \"https://meet.example/council\"", start.Arguments, StringComparison.Ordinal);
        Assert.False(start.UseShellExecute);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void WithoutAnAddressOnlyTheArgumentsArePassed()
    {
        ProcessStartInfo start = MeetingBrowser.StartInfo(
            new MeetingBrowserOptions { Executable = Chrome, Arguments = "--new-window" });

        Assert.Equal("--new-window", start.Arguments);
    }

    [Theory]
    [Trait("Category", TestCategories.Unit)]
    [InlineData(Chrome, "chrome.exe")]
    [InlineData(@"C:\Browsers\chrome", "chrome.exe")]
    [InlineData(@"C:\Browsers\MSEDGE.EXE", "MSEDGE.EXE")]
    public void TheBrowserIsKnownByTheNameAProcessSnapshotReports(string executable, string expected)
    {
        Assert.Equal(expected, MeetingBrowser.ExecutableName(new MeetingBrowserOptions { Executable = executable }));
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AStartedBrowserIsRegisteredByItsPidAndFileName()
    {
        // #131. Two separate Chrome trees, so only the registration can say which one is the meeting.
        ScriptedProcesses processes = new();
        processes.Start(300, 1, "chrome.exe");
        processes.Start(400, 1, "chrome.exe");

        ProcessLoopbackSources sources = new(processes, NullLogger.Instance);
        MeetingBrowser browser = new(
            new MeetingBrowserOptions { Executable = Chrome },
            () => sources,
            _ => 400,
            NullLogger<MeetingBrowser>.Instance);

        Assert.Null(browser.Start());

        // Registered under the full path instead of chrome.exe, or not at all, and this lease is
        // ambiguous and throws.
        using ProcessCaptureLease lease = sources.Lease(Meeting, Parse(Meeting));

        Assert.Equal(400, lease.Process.ProcessId);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void WhenWindowsStartsNothingNothingIsRegisteredAndTheTrayIsToldWhy()
    {
        ScriptedProcesses processes = new();
        processes.Start(300, 1, "chrome.exe");
        processes.Start(400, 1, "chrome.exe");

        ProcessLoopbackSources sources = new(processes, NullLogger.Instance);
        MeetingBrowser browser = new(
            new MeetingBrowserOptions { Executable = Chrome },
            () => sources,
            _ => null,
            NullLogger<MeetingBrowser>.Instance);

        Assert.Contains("did not start", browser.Start(), StringComparison.Ordinal);
        Assert.Throws<DeviceNotFoundException>(() => sources.Lease(Meeting, Parse(Meeting)));
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void WhenStartingThrowsTheTrayIsGivenWindowsOwnSentence()
    {
        ProcessLoopbackSources sources = new(new ScriptedProcesses(), NullLogger.Instance);
        MeetingBrowser browser = new(
            new MeetingBrowserOptions { Executable = Chrome },
            () => sources,
            _ => throw new Win32Exception(2, "The system cannot find the file specified."),
            NullLogger<MeetingBrowser>.Instance);

        Assert.Equal("The system cannot find the file specified.", browser.Start());
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void TheAmbiguityWarningNamesTheSettingTheEngineReads()
    {
        // #132. The device layer cannot read the engine's configuration, so it spells the key out;
        // this is what keeps the two from drifting apart.
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [ProcessLoopbackSources.MeetingBrowserSetting] = Chrome })
            .Build();

        MeetingBrowserOptions? options = configuration.GetSection(MeetingBrowserOptions.Section).Get<MeetingBrowserOptions>();

        Assert.Equal(Chrome, options?.Executable);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void WithoutProcessSourcesNothingIsStartedAndTheTrayIsToldWhy()
    {
        string workspace = Path.Combine(Path.GetTempPath(), $"vam-{Guid.NewGuid():N}");

        using NullAudioBackend devices = new();
        using VamEngine engine = new(
            new EngineOptions
            {
                ConsolePath = Path.Combine(workspace, "console.json"),
                PresetPath = Path.Combine(workspace, "presets.json"),
                RecordingDirectory = Path.Combine(workspace, "recordings")
            },
            NullLoggerFactory.Instance,
            devices);

        // A path that cannot start, so reaching Process.Start would give a different sentence.
        MeetingBrowser browser = new(
            new MeetingBrowserOptions { Executable = Path.Combine(workspace, "chrome.exe") },
            engine,
            NullLogger<MeetingBrowser>.Instance);

        Assert.Contains("no process sources", browser.Start(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ItIsReadFromConfigurationAndKeepsItsOwnProfileUnlessToldOtherwise()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{MeetingBrowserOptions.Section}:Executable"] = Chrome,
                [$"{MeetingBrowserOptions.Section}:Url"] = "https://meet.example/council"
            })
            .Build();

        MeetingBrowserOptions? options = configuration.GetSection(MeetingBrowserOptions.Section).Get<MeetingBrowserOptions>();

        Assert.NotNull(options);
        Assert.True(options.IsConfigured);
        Assert.Equal("https://meet.example/council", options.Url);
        Assert.Contains("--user-data-dir=", options.Arguments, StringComparison.Ordinal);
        Assert.False(new MeetingBrowserOptions().IsConfigured);
    }

    static ProcessLoopbackId Parse(AudioDeviceId deviceId)
    {
        Assert.True(ProcessLoopbackId.TryParse(deviceId, out ProcessLoopbackId target));

        return target;
    }
}
