using Vam.Protocol.V1;
using Vam.TestKit.Harness;
using Vam.Ui.Abstractions;
using Vam.Ui.Extensions;
using Xunit;

namespace Vam.Ui.Tests.Extensions;

/// <summary>
/// The meter-frame slot passes from one view to the next, and the old view's teardown runs after
/// the new view has taken it. Getting that wrong is a console whose meters stop on a view change.
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

    sealed class FakeSession : IVamSession
    {
        public event Action? Changed
        {
            add { }
            remove { }
        }

        public ConnectionState Connection => throw new NotSupportedException();

        public string StatusMessage => throw new NotSupportedException();

        public ConsoleState? Console => throw new NotSupportedException();

        public string ServerName => throw new NotSupportedException();

        public int SampleRate => throw new NotSupportedException();

        public int BlockFrames => throw new NotSupportedException();

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
    }
}
