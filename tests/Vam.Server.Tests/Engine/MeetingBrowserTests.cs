using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Vam.Server.Engine;
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
