namespace Vam.Server.Tray;

/// <summary>What the tray reads from the engine, once a second.</summary>
public sealed record TrayStatus
{
    /// <summary>Where the engine listens.</summary>
    public required string Address { get; init; }

    /// <summary>Dropouts since the engine started.</summary>
    public long Dropouts { get; init; }

    /// <summary>Strips muted because their device failed or went.</summary>
    public int FaultedStrips { get; init; }

    /// <summary>How long the recording has run, or null when nothing is recording.</summary>
    public TimeSpan? RecordingFor { get; init; }

    /// <summary>Whether a recording is open.</summary>
    public bool IsRecording => RecordingFor is not null;
}
