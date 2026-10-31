using Vam.Protocol.V1;
using Vam.TestKit.Harness;
using Vam.Ui.Abstractions;
using Vam.Ui.Services;
using Xunit;

namespace Vam.Ui.Tests.Services;

/// <summary>
/// Settings offers to stop the engine only when there is one to stop, and only once.
/// </summary>
/// <remarks>
/// The defect this guards: a stop button beside "Connect" with no engine running, pressed again and
/// again, each press sending <c>Shutdown</c> to nobody.
/// </remarks>
public class EngineConnectorTests
{
    [Theory]
    [InlineData(ConnectionState.Idle)]
    [InlineData(ConnectionState.Connecting)]
    [InlineData(ConnectionState.Reconnecting)]
    [InlineData(ConnectionState.Refused)]
    [Trait("Category", TestCategories.Unit)]
    public void NoEngineOffersNeitherStopNorRestart(ConnectionState connection)
    {
        EngineConnector connector = Connector(new FakeSession { Connection = connection });

        Assert.False(connector.OffersStop);
        Assert.False(connector.OffersRestart);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ALocalConnectedEngineOffersBoth()
    {
        EngineConnector connector = Connector(new FakeSession { Connection = ConnectionState.Connected });

        Assert.True(connector.OffersStop);
        Assert.True(connector.OffersRestart);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ARemoteEngineIsNeverOffered()
    {
        EngineConnector connector = Connector(
            new FakeSession { Connection = ConnectionState.Connected },
            "http://studio-pc:5211");

        Assert.False(connector.OffersStop);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AHostThatCannotStartOneOffersStopButNotRestart()
    {
        EngineConnector connector = Connector(
            new FakeSession { Connection = ConnectionState.Connected },
            canStartEngine: false);

        Assert.True(connector.OffersStop);
        Assert.False(connector.OffersRestart);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public async Task AnAcceptedStopStaysPendingUntilTheConnectionChanges()
    {
        FakeSession session = new() { Connection = ConnectionState.Connected };
        EngineConnector connector = Connector(session);

        Assert.True(await connector.StopEngineAsync("test", TestContext.Current.CancellationToken));
        Assert.True(connector.IsStopping);

        session.Raise();
        Assert.True(connector.IsStopping);

        session.Connection = ConnectionState.Reconnecting;
        session.Raise();

        Assert.False(connector.IsStopping);
        Assert.False(connector.OffersStop);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public async Task ASecondPressSendsNothing()
    {
        FakeSession session = new() { Connection = ConnectionState.Connected };
        EngineConnector connector = Connector(session);

        await connector.StopEngineAsync("test", TestContext.Current.CancellationToken);

        Assert.False(await connector.StopEngineAsync("test", TestContext.Current.CancellationToken));
        Assert.Equal(1, session.Shutdowns);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public async Task ARefusedStopCanBeTriedAgain()
    {
        FakeSession session = new() { Connection = ConnectionState.Connected, Accepts = false };
        EngineConnector connector = Connector(session);

        Assert.False(await connector.StopEngineAsync("test", TestContext.Current.CancellationToken));
        Assert.False(connector.IsStopping);
        Assert.Equal("settings.stopRefused", connector.Problem);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public async Task ARestartedEngineCanBeStoppedAgain()
    {
        FakeSession session = new() { Connection = ConnectionState.Connected };
        EngineConnector connector = Connector(session);

        await connector.StopEngineAsync("test", TestContext.Current.CancellationToken);

        session.Connection = ConnectionState.Reconnecting;
        session.Raise();
        session.Connection = ConnectionState.Connected;
        session.Raise();

        Assert.False(connector.IsStopping);
        Assert.True(connector.OffersStop);
    }

    [Theory]
    [InlineData(ConnectionState.Idle)]
    [InlineData(ConnectionState.Connecting)]
    [InlineData(ConnectionState.Reconnecting)]
    [InlineData(ConnectionState.Refused)]
    [Trait("Category", TestCategories.Unit)]
    public void ClosingWithNoEngineAsksNothing(ConnectionState connection)
    {
        EngineConnector connector = Connector(new FakeSession { Connection = connection });

        Assert.False(connector.AsksOnClose);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ClosingWhileConnectedToALocalEngineAsks()
    {
        EngineConnector connector = Connector(new FakeSession { Connection = ConnectionState.Connected });

        Assert.True(connector.AsksOnClose);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ClosingWhileConnectedToARemoteEngineAsksNothing()
    {
        EngineConnector connector = Connector(
            new FakeSession { Connection = ConnectionState.Connected },
            "http://studio-pc:5211");

        Assert.False(connector.AsksOnClose);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public async Task ClosingWhileAStopIsPendingAsksNothing()
    {
        EngineConnector connector = Connector(new FakeSession { Connection = ConnectionState.Connected });

        await connector.StopEngineAsync("test", TestContext.Current.CancellationToken);

        Assert.False(connector.AsksOnClose);
    }

    static EngineConnector Connector(
        FakeSession session,
        string address = VamSessionOptions.LocalAddress,
        bool canStartEngine = true)
    {
        VamSessionOptions options = new() { Address = address };
        FakePlatform platform = new() { CanStartEngine = canStartEngine };

        return new EngineConnector(session, new EngineProbe(options, platform), options, platform);
    }

    sealed class FakePlatform : IPlatformServices
    {
        public string ClientName => "test";

        public bool CanPickFolders => false;

        public bool CanOpenFolders => false;

        public bool CanStartEngine { get; init; }

        public string? RememberedEngine { get; set; }

        public bool StartsEngineAutomatically { get; set; }

        public ValueTask<string?> PickFolderAsync(string title, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<string?> OpenFolderAsync(string path, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public bool CanSaveFiles => false;

        public ValueTask<Stream?> CreateFileAsync(string suggestedName, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public string? ExportAddress(string engine, string directory) => null;

        public ValueTask<string?> StartEngineAsync(string address, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    sealed class FakeSession : IVamSession
    {
        public event Action? Changed;

        public ConnectionState Connection { get; set; }

        public bool Accepts { get; init; } = true;

        public int Shutdowns { get; private set; }

        public string StatusMessage => throw new NotSupportedException();

        public ConsoleState? Console => throw new NotSupportedException();

        public string ServerName => throw new NotSupportedException();

        public string EngineAddress => throw new NotSupportedException();

        public int SampleRate => throw new NotSupportedException();

        public int BlockFrames => throw new NotSupportedException();

        public IReadOnlySet<string> Features => throw new NotSupportedException();

        public IReadOnlyList<EngineEvent> Events => throw new NotSupportedException();

        public IReadOnlyList<DeviceInfo> Devices => throw new NotSupportedException();

        public IReadOnlyList<ModifierDescriptorState> Modifiers => throw new NotSupportedException();

        public IReadOnlyList<ChainPresetSummary> Presets => throw new NotSupportedException();

        public MeterFrameHandler? MeterFrame { get; set; }

        public void Raise() => Changed?.Invoke();

        public ValueTask<CommandReply> ApplyAsync(Command command, CancellationToken cancellationToken = default)
        {
            Shutdowns++;

            return ValueTask.FromResult(new CommandReply { Accepted = Accepts, Reason = Accepts ? string.Empty : "no" });
        }

        public ValueTask ConnectAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask ReconnectAsync(string address, CancellationToken cancellationToken = default) =>
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
