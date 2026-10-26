namespace Vam.Protocol;

/// <summary>
/// The names an engine lists in <c>HelloReply.features</c>, one for each addition to protocol 1.
/// </summary>
/// <remarks>
/// Names rather than a minor version number, because pull requests here land in parallel and two of
/// them would both claim the next number. An engine from before the list sends none of these, so a
/// console asks before it reads a field or calls a method one of them names.
/// </remarks>
public static class EngineFeatures
{
    /// <summary><c>ConsoleState.paths</c> and <c>SetRecordingsPath</c>.</summary>
    public const string EnginePaths = "engine.paths";

    /// <summary>The share mode and the device and native rates on channels, buses and devices.</summary>
    public const string ShareMode = "device.share-mode";

    /// <summary>
    /// <c>RecordingState.captures</c>, the size and duration projection, the available formats and
    /// <c>SetCaptureOptions</c>.
    /// </summary>
    public const string RecordingCaptures = "recording.captures";

    /// <summary><c>ListPastSessions</c>.</summary>
    public const string PastSessions = "recording.past-sessions";

    /// <summary>Every name above, which is what an engine built from this source has.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        EnginePaths,
        ShareMode,
        RecordingCaptures,
        PastSessions
    ];
}
