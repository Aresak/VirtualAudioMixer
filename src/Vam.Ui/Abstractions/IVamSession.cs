using Vam.Protocol.V1;

namespace Vam.Ui.Abstractions;

/// <summary>
/// One console's connection to one engine.
/// </summary>
/// <remarks>
/// <para>
/// Everything the console knows comes through here, including when the engine is on the same
/// machine. G1: the engine is the process that owns the meeting and this is a view over it. A local
/// client that reached into the graph directly would leave the remote path as the one nobody
/// exercised until somebody needed it during a session.
/// </para>
/// <para>
/// The two streams are kept apart on purpose. Meters arrive through <see cref="MeterFrame"/> and go
/// straight to a canvas; console state arrives through <see cref="Changed"/> and is rare enough to
/// re-render. A client that cannot keep up drops meter frames and still moves faders.
/// </para>
/// </remarks>
public interface IVamSession
{
    /// <summary>Where this console stands.</summary>
    ConnectionState Connection { get; }

    /// <summary>What the engine said when it refused or dropped, in a sentence a person can read.</summary>
    string StatusMessage { get; }

    /// <summary>The console as the engine last described it, or null before the first fetch.</summary>
    ConsoleState? Console { get; }

    /// <summary>What the engine calls itself.</summary>
    string ServerName { get; }

    /// <summary>The rate the whole graph runs at.</summary>
    int SampleRate { get; }

    /// <summary>Frames in a block.</summary>
    int BlockFrames { get; }

    /// <summary>Engine events, newest first, capped.</summary>
    IReadOnlyList<EngineEvent> Events { get; }

    /// <summary>
    /// The endpoints on the engine's machine.
    /// </summary>
    /// <remarks>
    /// The engine's, not this console's. A console on a laptop cannot enumerate the sound cards of
    /// the machine wired to the microphones, and a client that offered its own device list would be
    /// offering an operator a choice that cannot be made.
    /// </remarks>
    IReadOnlyList<DeviceInfo> Devices { get; }

    /// <summary>
    /// Every modifier this engine has.
    /// </summary>
    /// <remarks>
    /// Asked rather than assumed. A build with a third-party modifier dropped into its folder has
    /// ones this console has never heard of, and a hard-coded list would make them unreachable.
    /// </remarks>
    IReadOnlyList<ModifierDescriptorState> Modifiers { get; }

    /// <summary>
    /// The chain presets this engine has. B12.
    /// </summary>
    /// <remarks>
    /// The engine's, not this console's. A preset saved at the operator's desk has to be there on
    /// the tablet, and one that lived in a client would vanish when somebody reinstalled a browser.
    /// </remarks>
    IReadOnlyList<ChainPresetSummary> Presets { get; }

    /// <summary>Raised when the console state or the connection changed and the UI should redraw.</summary>
    /// <remarks>Never raised for a meter frame.</remarks>
    event Action? Changed;

    /// <summary>
    /// Set by whatever is drawing meters. One handler, because there is one canvas surface.
    /// </summary>
    MeterFrameHandler? MeterFrame { get; set; }

    /// <summary>Connects, greets, fetches the console and starts both streams.</summary>
    /// <param name="cancellationToken">Gives up.</param>
    ValueTask ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>Sends one change.</summary>
    /// <param name="command">What to change.</param>
    /// <param name="cancellationToken">Gives up.</param>
    /// <returns>
    /// Whether it was taken, and why not if it was refused. A mix-minus send that cannot be switched
    /// on comes back here with a reason rather than silently doing nothing.
    /// </returns>
    ValueTask<CommandReply> ApplyAsync(Command command, CancellationToken cancellationToken = default);

    /// <summary>Asks the engine for the whole console again.</summary>
    /// <param name="cancellationToken">Gives up.</param>
    ValueTask RefreshAsync(CancellationToken cancellationToken = default);

    /// <summary>Reads the preset library back, after one was saved or deleted.</summary>
    /// <param name="cancellationToken">Gives up.</param>
    ValueTask RefreshPresetsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks for K1 to K7.
    /// </summary>
    /// <remarks>
    /// Polled by the diagnostics view while it is open and by nothing else. An operator running a
    /// meeting is not paying for a drift chart nobody is looking at.
    /// </remarks>
    /// <param name="cancellationToken">Gives up.</param>
    /// <returns>The report, or null when there is no engine to ask.</returns>
    ValueTask<DiagnosticsState?> GetDiagnosticsAsync(CancellationToken cancellationToken = default);
}
