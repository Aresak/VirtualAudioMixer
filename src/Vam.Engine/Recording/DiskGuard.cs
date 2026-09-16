using Microsoft.Extensions.Logging;

namespace Vam.Engine.Recording;

/// <summary>
/// Decides whether there is room to record, and says so before the meeting rather than during it. E5.
/// </summary>
/// <remarks>
/// <para>
/// <b>The failure this exists to prevent is the silent one.</b> A disk filling up mid-session ends
/// with a recording that looks fine until somebody tries to use it, and by then the meeting is over.
/// Refusing to start is an inconvenience; running out at hour three is a lost record of a public
/// session.
/// </para>
/// <para>
/// The margin is deliberate. A projection that exactly fits leaves nothing for the operating system,
/// for a session that runs long, or for the fact that nobody knows in advance how long a council
/// meeting will take.
/// </para>
/// </remarks>
public sealed class DiskGuard(ILogger<DiskGuard> logger)
{
    /// <summary>How much more than the projection has to be free before recording may start.</summary>
    public const double RequiredMargin = 1.15;

    /// <summary>Below this, the operator is told loudly and repeatedly while recording continues.</summary>
    public const long WarningBytes = 20L * 1024 * 1024 * 1024;

    /// <summary>Bytes one second of one track takes at the engine's format.</summary>
    /// <param name="sampleRate">The rate.</param>
    /// <param name="channelCount">Channels in that track.</param>
    /// <returns>Bytes per second.</returns>
    public static long BytesPerSecond(int sampleRate, int channelCount) =>
        (long)sampleRate * channelCount * WaveWriter.BytesPerSample;

    /// <summary>
    /// Whether a session may start.
    /// </summary>
    /// <param name="path">Where the recording will go.</param>
    /// <param name="projectedBytes">How large the session is expected to be.</param>
    /// <returns>The verdict, with the numbers in it.</returns>
    public DiskVerdict CheckBeforeStart(string path, long projectedBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        long free;

        try
        {
            free = new DriveInfo(System.IO.Path.GetPathRoot(System.IO.Path.GetFullPath(path))!).AvailableFreeSpace;
        }
        catch (Exception error)
        {
            // A path whose drive cannot be interrogated is not a reason to refuse to record. It is a
            // reason to say so and let the operator decide, because the alternative is a meeting
            // that goes unrecorded over a network share nobody could measure.
            logger.LogWarning(error, "Could not read the free space at {Path}. Recording anyway.", path);

            return new DiskVerdict(true, 0, projectedBytes, "Free space could not be read; recording anyway.");
        }

        long required = (long)(projectedBytes * RequiredMargin);

        if (free >= required)
        {
            return new DiskVerdict(true, free, projectedBytes, "There is room.");
        }

        // The message says how much is needed, because "not enough space" leaves an operator with a
        // decision they cannot make.
        string message =
            $"Recording needs {Readable(required)} and {Readable(free)} is free at {path}. "
            + $"Free {Readable(required - free)} or record somewhere else.";

        logger.LogError("{Message}", message);

        return new DiskVerdict(false, free, projectedBytes, message);
    }

    /// <summary>
    /// Checks the space that is left while recording.
    /// </summary>
    /// <param name="path">Where the recording is going.</param>
    /// <returns>Whether the operator should be told.</returns>
    public bool IsRunningLow(string path)
    {
        try
        {
            long free = new DriveInfo(System.IO.Path.GetPathRoot(System.IO.Path.GetFullPath(path))!).AvailableFreeSpace;

            if (free >= WarningBytes)
            {
                return false;
            }

            logger.LogWarning(
                "Only {Free} is left at {Path}. The recording will stop when it runs out.",
                Readable(free),
                path);

            return true;
        }
        catch (Exception error)
        {
            logger.LogWarning(error, "Could not read the free space at {Path}.", path);
            return false;
        }
    }

    static string Readable(long bytes) => bytes >= 1_000_000_000L
        ? $"{bytes / 1_000_000_000.0:F1} GB"
        : $"{bytes / 1_000_000.0:F0} MB";
}
