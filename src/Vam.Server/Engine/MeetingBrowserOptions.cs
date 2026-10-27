namespace Vam.Server.Engine;

/// <summary>
/// The browser the meeting runs in, which VAM starts so it knows which process tree to capture.
/// </summary>
/// <remarks>
/// Read from <c>Vam:MeetingBrowser</c>. Without an executable there is nothing to start, and the tray
/// does not offer it.
/// </remarks>
public sealed record MeetingBrowserOptions
{
    /// <summary>The configuration section.</summary>
    public const string Section = "Vam:MeetingBrowser";

    /// <summary>The browser's executable, a full path, such as Chrome's or Edge's.</summary>
    public string Executable { get; init; } = string.Empty;

    /// <summary>The meeting's address, opened when the browser starts. Empty opens the browser alone.</summary>
    public string Url { get; init; } = string.Empty;

    /// <summary>What the browser is started with, before the address.</summary>
    /// <remarks>
    /// A profile of its own, so a Chromium browser already open on the machine does not take the
    /// window over and leave VAM holding the PID of a process that has exited. Another browser
    /// family needs its own equivalent here.
    /// </remarks>
    public string Arguments { get; init; } =
        $"--user-data-dir=\"{Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VAM", "meeting-browser")}\" --no-first-run --new-window";

    /// <summary>Whether there is a browser to start.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Executable);
}
