using Microsoft.Extensions.Logging;

namespace Vam.Engine.Diagnostics;

/// <summary>
/// Turns what the audio thread noted into words. I2 and I4.
/// </summary>
/// <remarks>
/// <para>
/// The other half of the arrangement: the audio thread writes numbers and an index into a ring, and
/// this drains the ring on the control thread and resolves the index against a table of names. That
/// is why there is no logging call anywhere below the line, and why the log still reads like
/// somebody wrote it — "Strip 2 re-attached and unmuted after 340 ms" rather than a row of numbers.
/// </para>
/// <para>
/// <b>Repeats are counted, not written.</b> The first dropout of a kind on an endpoint gets its own
/// line; the ones after it are counted and written as one line per <see cref="Interval"/>. A device
/// that keeps dropping out would otherwise fill the console's log tail by itself. The log ring is
/// untouched, so the dropouts tab still lists every one.
/// </para>
/// </remarks>
/// <param name="log">The ring the audio threads write into.</param>
/// <param name="logger">Where the lines go.</param>
/// <param name="clock">What decides an interval has passed with nothing new arriving. The system clock if null.</param>
public sealed class DropoutPump(DropoutLog log, ILogger<DropoutPump> logger, TimeProvider? clock = null)
{
    /// <summary>Records taken per pump. Enough to keep up, bounded so one bad second cannot stall the loop.</summary>
    const int BatchSize = 256;

    /// <summary>How long repeats are counted before they are written, unless told otherwise.</summary>
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(10);

    readonly DropoutRecord[] batch = new DropoutRecord[BatchSize];
    readonly List<string> names = [];
    readonly Dictionary<(int Endpoint, DropoutKind Kind), DropoutRun> runs = [];
    readonly TimeProvider time = clock ?? TimeProvider.System;

    /// <summary>How long repeats of one kind on one endpoint are counted before one line says how many.</summary>
    public TimeSpan Interval { get; init; } = DefaultInterval;

    /// <summary>Records given a log line of their own so far.</summary>
    public long Reported { get; private set; }

    /// <summary>Records counted into a repeat line rather than logged separately.</summary>
    public long Folded { get; private set; }

    /// <summary>Repeat lines written so far.</summary>
    public long Summaries { get; private set; }

    /// <summary>
    /// Tells the pump what each endpoint is called, so the log can say so.
    /// </summary>
    /// <param name="endpointNames">Names, in the order the audio thread indexes them.</param>
    public void SetNames(IReadOnlyList<string> endpointNames)
    {
        ArgumentNullException.ThrowIfNull(endpointNames);

        names.Clear();
        names.AddRange(endpointNames);
    }

    /// <summary>
    /// Drains the log and writes what it found, and any count whose interval has passed. Control thread.
    /// </summary>
    /// <returns>Records taken this time.</returns>
    public int Pump()
    {
        int count = log.Drain(batch);

        for (int index = 0; index < count; index++)
        {
            Report(batch[index]);
        }

        Expire(time.GetUtcNow().UtcTicks);

        return count;
    }

    /// <summary>Writes out every count still being held. Call when a session ends.</summary>
    public void Flush()
    {
        foreach (KeyValuePair<(int Endpoint, DropoutKind Kind), DropoutRun> entry in runs)
        {
            Summarise(entry.Key, entry.Value);
        }

        runs.Clear();
    }

    static string Describe(DropoutKind kind) => kind switch
    {
        DropoutKind.CaptureOverrun => "produced audio faster than it could be taken away",
        DropoutKind.CaptureUnderrun => "was asked for audio that had not arrived",
        DropoutKind.RenderUnderrun => "asked for audio the mix had not finished",
        DropoutKind.RecordingDropped => "could not be written because the disk was behind",
        DropoutKind.CorrectionClamped => "needed more drift correction than is plausible",
        _ => "reported something unrecognised"
    };

    static DateTimeOffset LocalTime(long utcTicks) => new DateTimeOffset(utcTicks, TimeSpan.Zero).ToLocalTime();

    string NameOf(int endpointIndex) =>
        endpointIndex >= 0 && endpointIndex < names.Count ? names[endpointIndex] : $"endpoint {endpointIndex}";

    void Report(DropoutRecord record)
    {
        (int Endpoint, DropoutKind Kind) key = (record.EndpointIndex, record.Kind);

        if (runs.TryGetValue(key, out DropoutRun? run))
        {
            bool isElapsed = record.TimestampTicks >= run.StartTicks + Interval.Ticks;

            if (isElapsed && run.Count == 0)
            {
                runs.Remove(key);
            }
            else
            {
                if (isElapsed)
                {
                    Summarise(key, run);
                    run.Restart(record.TimestampTicks);
                }

                run.Add(record);
                Folded++;

                return;
            }
        }

        runs[key] = new DropoutRun(record.TimestampTicks);
        Reported++;

        logger.LogWarning(
            "{Endpoint} {What} at {Time:HH:mm:ss.fff}, costing {Frames} frames ({Detail}).",
            NameOf(record.EndpointIndex),
            Describe(record.Kind),
            LocalTime(record.TimestampTicks),
            record.Frames,
            record.Detail);
    }

    void Expire(long nowTicks)
    {
        // A run that went quiet for a whole interval is forgotten, so the next dropout gets a line of its own.
        foreach (KeyValuePair<(int Endpoint, DropoutKind Kind), DropoutRun> entry in runs)
        {
            if (nowTicks < entry.Value.StartTicks + Interval.Ticks)
            {
                continue;
            }

            if (entry.Value.Count == 0)
            {
                runs.Remove(entry.Key);
            }
            else
            {
                Summarise(entry.Key, entry.Value);
                entry.Value.Restart(nowTicks);
            }
        }
    }

    void Summarise((int Endpoint, DropoutKind Kind) key, DropoutRun run)
    {
        if (run.Count == 0)
        {
            return;
        }

        Summaries++;

        logger.LogWarning(
            "{Endpoint} {What} {Repeats} more times by {Time:HH:mm:ss.fff}, costing {Frames} frames.",
            NameOf(key.Endpoint),
            Describe(key.Kind),
            run.Count,
            LocalTime(run.LastTicks),
            run.Frames);
    }
}
