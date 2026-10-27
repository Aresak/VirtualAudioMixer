namespace Vam.Engine.Windows.Devices.ProcessLoopback;

/// <summary>Whether a named process was found among the running ones.</summary>
public enum ProcessResolutionOutcome
{
    /// <summary>Exactly one process fits.</summary>
    Found,

    /// <summary>Nothing fits. The process is not running.</summary>
    Missing,

    /// <summary>More than one process tree fits, and picking one would be a guess.</summary>
    Ambiguous
}
