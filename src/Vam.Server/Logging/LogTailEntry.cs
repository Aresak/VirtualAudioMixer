namespace Vam.Server.Logging;

/// <summary>One line of the log, as the diagnostics view shows it. K7.</summary>
/// <param name="Timestamp">When it happened.</param>
/// <param name="Level">How severe. The view filters on this.</param>
/// <param name="Source">Which part of the engine said it.</param>
/// <param name="Message">What it said.</param>
public readonly record struct LogTailEntry(DateTime Timestamp, string Level, string Source, string Message);
