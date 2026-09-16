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
/// <b>Repeats are folded.</b> A device that has started underrunning does it every block, and three
/// thousand identical lines a minute drives the one line that mattered off the top of the log.
/// </para>
/// </remarks>
public sealed class DropoutPump(DropoutLog log, ILogger<DropoutPump> logger)
{
    /// <summary>Records taken per pump. Enough to keep up, bounded so one bad second cannot stall the loop.</summary>
    const int BatchSize = 256;

    readonly DropoutRecord[] batch = new DropoutRecord[BatchSize];
    readonly List<string> names = [];

    DropoutKind lastKind;
    int lastEndpoint = -1;
    int repeats;

    /// <summary>Records turned into log lines so far.</summary>
    public long Reported { get; private set; }

    /// <summary>Records folded into a repeat count rather than logged separately.</summary>
    public long Folded { get; private set; }

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
    /// Drains the log and writes what it found. Control thread.
    /// </summary>
    /// <returns>Records taken this time.</returns>
    public int Pump()
    {
        int count = log.Drain(batch);

        for (int index = 0; index < count; index++)
        {
            Report(batch[index]);
        }

        return count;
    }

    /// <summary>Writes out anything being held as a repeat. Call when a session ends.</summary>
    public void Flush()
    {
        if (repeats > 0)
        {
            logger.LogWarning("...and {Repeats} more like the last one.", repeats);
            repeats = 0;
        }
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

    string NameOf(int endpointIndex) =>
        endpointIndex >= 0 && endpointIndex < names.Count ? names[endpointIndex] : $"endpoint {endpointIndex}";

    void Report(DropoutRecord record)
    {
        // A device that has started underrunning does it every block. Three thousand identical lines
        // a minute would push the one line that mattered off the top of the log.
        if (record.Kind == lastKind && record.EndpointIndex == lastEndpoint)
        {
            repeats++;
            Folded++;

            return;
        }

        Flush();

        lastKind = record.Kind;
        lastEndpoint = record.EndpointIndex;
        Reported++;

        logger.LogWarning(
            "{Endpoint} {What} at {Time:HH:mm:ss.fff}, costing {Frames} frames ({Detail}).",
            NameOf(record.EndpointIndex),
            Describe(record.Kind),
            new DateTimeOffset(record.TimestampTicks, TimeSpan.Zero).ToLocalTime(),
            record.Frames,
            record.Detail);
    }
}
