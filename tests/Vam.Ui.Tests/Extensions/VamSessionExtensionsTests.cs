using Vam.Protocol;
using Vam.Protocol.V1;
using Vam.TestKit.Harness;
using Vam.Ui.Abstractions;
using Vam.Ui.Extensions;
using Xunit;

namespace Vam.Ui.Tests.Extensions;

/// <summary>
/// The meter-frame slot passes from one view to the next, and the old view's teardown runs after
/// the new view has taken it. Getting that wrong is a console whose meters stop on a view change.
/// And an engine older than the console is told apart from a current one, rather than crashing it.
/// </summary>
public class VamSessionExtensionsTests
{
    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void TheOwnerCanLetGo()
    {
        FakeSession session = new();
        MeterFrameHandler mixer = (_, _, _) => { };

        session.TakeMeterFrames(mixer);
        session.ReleaseMeterFrames(mixer);

        Assert.Null(session.MeterFrame);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ALateTeardownLeavesTheNewViewsHandler()
    {
        FakeSession session = new();
        MeterFrameHandler mixer = (_, _, _) => { };
        MeterFrameHandler automix = (_, _, _) => { };

        session.TakeMeterFrames(mixer);
        session.TakeMeterFrames(automix);
        session.ReleaseMeterFrames(mixer);

        Assert.Same(automix, session.MeterFrame);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AViewThatNeverTookTheSlotCannotClearIt()
    {
        FakeSession session = new();
        MeterFrameHandler automix = (_, _, _) => { };

        session.TakeMeterFrames(automix);
        session.ReleaseMeterFrames(null);

        Assert.Same(automix, session.MeterFrame);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AnEngineFromBeforeTheListSupportsNothingAndIsOlder()
    {
        FakeSession session = new() { Connection = ConnectionState.Connected };

        Assert.False(session.Supports(EngineFeatures.RecordingCaptures));
        Assert.True(session.IsOlderEngine());
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ACurrentEngineSupportsEverythingAndIsNotOlder()
    {
        FakeSession session = new()
        {
            Connection = ConnectionState.Connected,
            Features = EngineFeatures.All.ToHashSet()
        };

        Assert.True(session.Supports(EngineFeatures.PastSessions));
        Assert.False(session.IsOlderEngine());
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void NoEngineIsNotAnOlderOne()
    {
        // Before the first Hello the list is empty too, and the status bar must not blame an engine
        // it has not reached.
        FakeSession session = new() { Connection = ConnectionState.Connecting };

        Assert.False(session.IsOlderEngine());
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AnOlderEngineHasNoCaptureOptionsEvenWhenItsMessageIsThere()
    {
        // The crash this exists for: an engine from before the list sends a recording with no
        // captures, and the view read straight through it.
        FakeSession older = new()
        {
            Connection = ConnectionState.Connected,
            Console = new ConsoleState { Recording = new RecordingState() }
        };

        Assert.Null(older.RecordingCaptures());
        Assert.Null(older.EnginePaths());
        Assert.False(older.CanListPastSessions());
        Assert.False(older.CanManagePastSessions());
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ACurrentEngineThatLeftAMessageOutIsNotReadThrough()
    {
        FakeSession current = new()
        {
            Connection = ConnectionState.Connected,
            Features = EngineFeatures.All.ToHashSet(),
            Console = new ConsoleState { Recording = new RecordingState() }
        };

        Assert.Null(current.RecordingCaptures());
        Assert.Null(current.EnginePaths());
        Assert.True(current.CanListPastSessions());
        Assert.True(current.CanManagePastSessions());
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ACurrentEngineGivesItsCaptureOptions()
    {
        RecordingCapture captures = new() { Inputs = true, Format = "wav24" };
        FakeSession current = new()
        {
            Connection = ConnectionState.Connected,
            Features = EngineFeatures.All.ToHashSet(),
            Console = new ConsoleState { Recording = new RecordingState { Captures = captures } }
        };

        Assert.Same(captures, current.RecordingCaptures());
    }

    sealed class FakeSession : IVamSession
    {
        public event Action? Changed
        {
            add { }
            remove { }
        }

        public ConnectionState Connection { get; init; }

        public string StatusMessage => throw new NotSupportedException();

        public ConsoleState? Console { get; init; }

        public string ServerName => throw new NotSupportedException();

        public string EngineAddress => throw new NotSupportedException();

        public int SampleRate => throw new NotSupportedException();

        public int BlockFrames => throw new NotSupportedException();

        public IReadOnlySet<string> Features { get; init; } = new HashSet<string>();

        public IReadOnlyList<EngineEvent> Events => throw new NotSupportedException();

        public IReadOnlyList<DeviceInfo> Devices => throw new NotSupportedException();

        public IReadOnlyList<ModifierDescriptorState> Modifiers => throw new NotSupportedException();

        public IReadOnlyList<ChainPresetSummary> Presets => throw new NotSupportedException();

        public MeterFrameHandler? MeterFrame { get; set; }

        public ValueTask ConnectAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask ReconnectAsync(string address, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<CommandReply> ApplyAsync(Command command, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask RefreshAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask RefreshPresetsAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<DiagnosticsState?> GetDiagnosticsAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<PastSessionList?> GetPastSessionsAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<IReadOnlyList<AudioProcess>> GetAudioProcessesAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<CommandReply?> DeletePastSessionAsync(string directory, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<CommandReply?> RenamePastSessionAsync(string directory, string name, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<string?> ExportPastSessionAsync(string directory, Stream destination, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
