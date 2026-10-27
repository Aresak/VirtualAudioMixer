using Vam.Protocol.V1;
using Vam.Ui.State;

namespace Vam.Ui.Extensions;

/// <summary>What the diagnostics view does to the engine's log before showing it.</summary>
public static class LogLineExtensions
{
    /// <summary>The lines that pass both the level chip and the text box, in their original order.</summary>
    /// <param name="lines">The log, oldest first, as the engine sends it.</param>
    /// <param name="level">The chip that is on.</param>
    /// <param name="text">The text box. Matched against the level as well as the message.</param>
    /// <returns>The lines to show.</returns>
    public static IReadOnlyList<LogLine> Filter(this IReadOnlyList<LogLine> lines, LogLevelFilter level, string text) =>
        level == LogLevelFilter.All && text.Length == 0
            ? lines
            : [.. lines.Where(line => line.IsAt(level) && line.Matches(text))];

    /// <summary>How many lines of <paramref name="current"/> arrived after the last of <paramref name="shown"/>.</summary>
    /// <param name="current">The log the engine sent last, oldest first.</param>
    /// <param name="shown">The log the reader is looking at, oldest first.</param>
    /// <returns>The count, or all of <paramref name="current"/> once the last shown line has left the tail.</returns>
    public static int CountNewerThan(this IReadOnlyList<LogLine> current, IReadOnlyList<LogLine> shown)
    {
        if (shown.Count == 0)
        {
            return current.Count;
        }

        LogLine last = shown[^1];

        for (int index = current.Count - 1; index >= 0; index--)
        {
            if (current[index].Equals(last))
            {
                return current.Count - 1 - index;
            }
        }

        return current.Count;
    }

    static bool IsAt(this LogLine line, LogLevelFilter level) => level switch
    {
        LogLevelFilter.Info => line.Level == "Info",
        LogLevelFilter.Warning => line.Level == "Warn",
        LogLevelFilter.Error => line.Level is "Error" or "Fatal",
        _ => true
    };

    static bool Matches(this LogLine line, string text) =>
        text.Length == 0
        || line.Message.Contains(text, StringComparison.OrdinalIgnoreCase)
        || line.Level.Contains(text, StringComparison.OrdinalIgnoreCase);
}
