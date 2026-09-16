using NLog;
using NLog.Targets;

namespace Vam.Server.Logging;

/// <summary>
/// Keeps the most recent log lines in memory, for the diagnostics view to show. K7.
/// </summary>
/// <remarks>
/// <para>
/// <b>The same stream the file gets, not a parallel one written for the screen.</b> A console
/// showing a different log from the one on disk is a console that will disagree with the evidence at
/// exactly the moment somebody is trying to work out what happened.
/// </para>
/// <para>
/// Bounded, because a three-hour session produces more lines than anybody will scroll through and an
/// unbounded buffer would be a slow leak with a schedule.
/// </para>
/// </remarks>
[Target("VamLogTail")]
public sealed class LogTailTarget : TargetWithLayout
{
    /// <summary>How many lines are kept.</summary>
    public const int Capacity = 2000;

    static readonly Queue<LogTailEntry> Entries = new(Capacity);
    static readonly Lock Gate = new();

    /// <summary>The lines currently held, oldest first.</summary>
    /// <returns>A copy, so a reader is not walking a queue somebody is writing into.</returns>
    public static IReadOnlyList<LogTailEntry> Snapshot()
    {
        lock (Gate)
        {
            return [.. Entries];
        }
    }

    /// <summary>Forgets everything.</summary>
    public static void Clear()
    {
        lock (Gate)
        {
            Entries.Clear();
        }
    }

    /// <inheritdoc />
    protected override void Write(LogEventInfo logEvent)
    {
        LogTailEntry entry = new(
            logEvent.TimeStamp,
            logEvent.Level.Name,
            logEvent.LoggerName ?? string.Empty,
            RenderLogEvent(Layout, logEvent));

        lock (Gate)
        {
            if (Entries.Count == Capacity)
            {
                Entries.Dequeue();
            }

            Entries.Enqueue(entry);
        }
    }
}
