namespace Vam.Server.Tray;

/// <summary>The tray menu's items, as the ids Windows hands back.</summary>
public enum TrayCommand
{
    /// <summary>Not an item.</summary>
    None = 0,

    /// <summary>Starts a console, or brings an open one to the front.</summary>
    OpenConsole = 1,

    /// <summary>Starts the recording, or stops the one running.</summary>
    ToggleRecording = 2,

    /// <summary>Ends the engine.</summary>
    StopEngine = 3
}
