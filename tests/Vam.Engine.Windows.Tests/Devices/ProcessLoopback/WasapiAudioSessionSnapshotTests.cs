using System.Runtime.InteropServices;
using Vam.Engine.Windows.Devices.ProcessLoopback;
using Vam.TestKit.Harness;
using Vam.TestKit.Logging;
using Xunit;

namespace Vam.Engine.Windows.Tests.Devices.ProcessLoopback;

/// <summary>An output or a session Windows will not describe is skipped on its own, and said so. #133.</summary>
public class WasapiAudioSessionSnapshotTests
{
    // RPC_E_DISCONNECTED, what a session torn down while being read reports.
    const int Disconnected = unchecked((int)0x80010108);

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ASessionThatCannotBeReadDoesNotTakeTheRestOfItsOutputWithIt()
    {
        StandInOutput speakers = new("Speakers", new AudioSessionEntry(100, true), null, new AudioSessionEntry(200, true), null);
        using RecordingLoggerFactory loggers = new();

        IReadOnlyList<AudioSessionEntry> sessions = WasapiAudioSessionSnapshot.Collect([speakers], loggers.CreateLogger("sessions"));

        Assert.Equal([new AudioSessionEntry(100, true), new AudioSessionEntry(200, true)], sessions);

        // One line for the output, not one per broken session.
        string problem = Assert.Single(loggers.Problems).Message;

        Assert.Contains("Speakers", problem, StringComparison.Ordinal);
        Assert.Contains("80010108", problem, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AnOutputThatCannotBeReadIsSkippedAndTheOthersAreListed()
    {
        StandInOutput gone = new("Headset", new AudioSessionEntry(100, true)) { RefreshFails = true };
        StandInOutput speakers = new("Speakers", new AudioSessionEntry(200, false));
        using RecordingLoggerFactory loggers = new();

        IReadOnlyList<AudioSessionEntry> sessions = WasapiAudioSessionSnapshot.Collect([gone, speakers], loggers.CreateLogger("sessions"));

        Assert.Equal([new AudioSessionEntry(200, false)], sessions);
        Assert.Contains("Headset", Assert.Single(loggers.Problems).Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void NothingIsLoggedWhenEverythingCanBeRead()
    {
        StandInOutput speakers = new("Speakers", new AudioSessionEntry(100, true), new AudioSessionEntry(200, false));
        using RecordingLoggerFactory loggers = new();

        IReadOnlyList<AudioSessionEntry> sessions = WasapiAudioSessionSnapshot.Collect([speakers], loggers.CreateLogger("sessions"));

        Assert.Equal(2, sessions.Count);
        Assert.Empty(loggers.Problems);
    }

    /// <summary>An output whose sessions are scripted. A null session is one that throws when read.</summary>
    sealed class StandInOutput(string name, params AudioSessionEntry?[] sessions) : IAudioSessionOutput
    {
        public bool RefreshFails { get; init; }

        public string Name => name;

        public int Refresh() => RefreshFails ? throw new COMException("Gone.", Disconnected) : sessions.Length;

        public AudioSessionEntry? Read(int index) => sessions[index] ?? throw new COMException("Gone.", Disconnected);
    }
}
