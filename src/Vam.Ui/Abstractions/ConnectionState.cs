namespace Vam.Ui.Abstractions;

/// <summary>Where the console stands with the engine it is looking at.</summary>
/// <remarks>
/// Shown in the status bar as a word and a colour rather than inferred from an empty mixer. G1 has
/// the engine outliving every console, so "no strips" and "not connected" are different facts and a
/// console that conflates them tells an operator the meeting has stopped when it has not.
/// </remarks>
public enum ConnectionState
{
    /// <summary>Nothing has been attempted yet.</summary>
    Idle,

    /// <summary>Dialling, or retrying after a drop.</summary>
    Connecting,

    /// <summary>Talking to an engine.</summary>
    Connected,

    /// <summary>The engine went away, and this console is trying to get back.</summary>
    Reconnecting,

    /// <summary>
    /// The engine refused the connection and said why in <see cref="IVamSession.StatusMessage"/>.
    /// </summary>
    Refused
}
