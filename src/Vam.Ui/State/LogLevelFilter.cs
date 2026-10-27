namespace Vam.Ui.State;

/// <summary>
/// Which lines of the engine's log the diagnostics view shows. K7's chips.
/// </summary>
public enum LogLevelFilter
{
    /// <summary>Every line, Debug included.</summary>
    All,

    /// <summary>Info lines only.</summary>
    Info,

    /// <summary>Warn lines only.</summary>
    Warning,

    /// <summary>Error and Fatal lines.</summary>
    Error
}
