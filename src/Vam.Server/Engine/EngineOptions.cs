namespace Vam.Server.Engine;

/// <summary>
/// How the engine is set up when the process starts.
/// </summary>
/// <remarks>
/// The block size and the rate are here rather than configurable per session because changing them
/// means recompiling every plan and reallocating every arena, and a session does not change its mind
/// about what a block is.
/// </remarks>
public sealed record EngineOptions
{
    /// <summary>
    /// Frames per block. A hundred and twenty is two and a half milliseconds.
    /// </summary>
    /// <remarks>
    /// Chosen because it divides the five, ten and twenty millisecond device periods exactly,
    /// divides RNNoise's four hundred and eighty sample frame exactly, and is a whole multiple of a
    /// SIMD vector on everything this runs on.
    /// </remarks>
    public int BlockFrames { get; init; } = 120;

    /// <summary>The rate the engine runs at.</summary>
    public int SampleRate { get; init; } = 48000;

    /// <summary>Where the console is saved and loaded from. H1 and H3.</summary>
    public string ConsolePath { get; init; } =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "VAM",
            "console.json");

    /// <summary>Where the chain presets are saved. B12.</summary>
    /// <remarks>
    /// Beside the console rather than inside it. A preset outlives the console it was made on, and
    /// an operator moving a setup to another machine wants their presets to come along without
    /// dragging that machine's device assignments with them.
    /// </remarks>
    public string PresetPath { get; init; } =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "VAM",
            "presets.json");

    /// <summary>Where recordings go.</summary>
    public string RecordingDirectory { get; init; } =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "VAM",
            "recordings");

    /// <summary>Whether to record from the moment the engine starts. E4.</summary>
    public bool RecordAutomatically { get; set; } = true;

    /// <summary>
    /// Whether the engine comes up in the console it went down in. H3.
    /// </summary>
    /// <remarks>
    /// On, and the only reason it can be turned off is a machine whose saved console names devices
    /// that have gone: starting from what is plugged in is then faster than editing a file. Nobody
    /// configures a mixer ten minutes before a session.
    /// </remarks>
    public bool LoadLastConsole { get; set; } = true;

    /// <summary>How long a session is assumed to run, for the disk projection.</summary>
    public TimeSpan ExpectedSessionDuration { get; init; } = TimeSpan.FromHours(4);

    /// <summary>How often the control loop runs.</summary>
    public TimeSpan ControlInterval { get; init; } = TimeSpan.FromMilliseconds(10);

    /// <summary>How often each device's drift correction is advanced.</summary>
    public TimeSpan CorrectionInterval { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>Whether to open real devices. False runs the engine with nothing plugged in.</summary>
    public bool UseRealDevices { get; init; } = true;
}
