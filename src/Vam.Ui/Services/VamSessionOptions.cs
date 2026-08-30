namespace Vam.Ui.Services;

/// <summary>Where this console should look for an engine.</summary>
public sealed class VamSessionOptions
{
    /// <summary>The engine's address.</summary>
    /// <remarks>
    /// A setting rather than a constant even for the desktop client, because the case where an
    /// operator runs the console on a laptop and the engine on the machine wired to the microphones
    /// is the normal case, not an advanced one.
    /// </remarks>
    public string Address { get; set; } = "http://localhost:5211";

    /// <summary>What the protocol version has to be for the engine to accept this console.</summary>
    public int ProtocolVersion { get; set; } = 1;

    /// <summary>How long to wait before trying again after a drop, and the ceiling that backs off to.</summary>
    public TimeSpan ReconnectDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>The longest gap between reconnection attempts.</summary>
    public TimeSpan MaximumReconnectDelay { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>How many engine events to keep for the diagnostics view.</summary>
    public int EventHistory { get; set; } = 500;

    /// <summary>
    /// How often the console state is read back while nothing has changed it.
    /// </summary>
    /// <remarks>
    /// Numbers on the strips move without anybody touching anything: the measured rate, the drift in
    /// parts per million, how long the recording has been running. Left to refresh only when a
    /// command is sent, they would sit at whatever they read when the console connected, which is a
    /// console quietly lying about a device that has started drifting.
    ///
    /// Once a second, not at meter rate. The meters have their own stream and their own canvas
    /// precisely so that this one can be slow.
    /// </remarks>
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromSeconds(1);
}
