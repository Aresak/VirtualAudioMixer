using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Vam.Server.Engine;
using Vam.TestKit.Devices;
using Vam.TestKit.Harness;
using Xunit;

namespace Vam.Server.Tests.Engine;

/// <summary>#96. What starting the meeting browser runs, and where it is configured.</summary>
public class MeetingBrowserTests
{
    const string Chrome = @"C:\Program Files\Google\Chrome\Application\chrome.exe";

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
}
