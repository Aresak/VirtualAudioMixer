namespace Vam.Ui.State;

/// <summary>
/// Which panel of the diagnostics view is showing.
/// </summary>
/// <remarks>
/// Tabs rather than one long scroll, because six panels on one page trains an operator to scroll
/// past the one they came for — and the whole rule for what lives on this view is that seeing it
/// every day would train you to ignore it.
/// </remarks>
public enum DiagnosticsTab
{
    /// <summary>K1 and K2. Which device is the one that is wrong.</summary>
    Clocks,

    /// <summary>K4, K5 and K6. Callback times, allocations, and what each modifier costs.</summary>
    Performance,

    /// <summary>K3. What went wrong and when, as a list rather than a count.</summary>
    Dropouts,

    /// <summary>K7. The engine's log, filtered.</summary>
    Log
}
