using Vam.Protocol.V1;
using Vam.TestKit.Harness;
using Xunit;

namespace Vam.Server.Tests;

/// <summary>The programs with audio a console can pick as a strip's source. #95.</summary>
public partial class ProtocolGateTests
{
    const int EngineProcessId = 4242;
    const int Explorer = 10;

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public async Task ABrowserPlayingFromAChildIsListedByItsExecutable()
    {
        processes.Start(100, Explorer, "msedge.exe");
        processes.Start(101, 100, "msedge.exe");
        processes.Start(200, Explorer, "notepad.exe");
        audioSessions.Open(101, isActive: true);

        AudioProcessList list = await client!.ListAudioProcessesAsync(new Empty(), cancellationToken: Token);

        AudioProcess browser = Assert.Single(list.Processes);
        Assert.Equal("process:msedge.exe", browser.Id);
        Assert.Equal("msedge", browser.Name);
        Assert.True(browser.IsPlaying);
        Assert.False(browser.IsInUse);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public async Task ProgramsPlayingNowComeBeforeQuietOnesAndEachIsListedOnce()
    {
        processes.Start(100, Explorer, "Spotify.exe");
        processes.Start(300, Explorer, "chrome.exe");
        processes.Start(301, 300, "chrome.exe");
        processes.Start(302, 300, "chrome.exe");
        audioSessions.Open(100, isActive: false);
        audioSessions.Open(301, isActive: false);
        audioSessions.Open(302, isActive: true);

        AudioProcessList list = await client!.ListAudioProcessesAsync(new Empty(), cancellationToken: Token);

        Assert.Equal(["process:chrome.exe", "process:Spotify.exe"], list.Processes.Select(process => process.Id));
        Assert.Equal([true, false], list.Processes.Select(process => process.IsPlaying));
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public async Task TheEngineTheSystemAndExitedProcessesAreNotOffered()
    {
        processes.Start(EngineProcessId, Explorer, "Vam.Server.exe");
        audioSessions.Open(0, isActive: true);
        audioSessions.Open(EngineProcessId, isActive: true);
        audioSessions.Open(999, isActive: true);

        AudioProcessList list = await client!.ListAudioProcessesAsync(new Empty(), cancellationToken: Token);

        Assert.Empty(list.Processes);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public async Task AProgramAStripAlreadyTakesIsMarkedInUse()
    {
        processes.Start(100, Explorer, "msedge.exe");
        audioSessions.Open(100, isActive: true);

        CommandReply added = await client!.ApplyAsync(
            new Command { AddChannel = new AddChannel { Name = "Browser", DeviceId = "process:msedge.exe", ChannelCount = 1 } },
            cancellationToken: Token);

        Assert.True(added.Accepted, added.Reason);

        AudioProcessList list = await client.ListAudioProcessesAsync(new Empty(), cancellationToken: Token);

        Assert.True(Assert.Single(list.Processes).IsInUse);
    }
}
