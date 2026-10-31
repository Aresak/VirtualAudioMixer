using System.ComponentModel;
using System.Diagnostics;
using Vam.Engine.Windows.Devices.ProcessLoopback;

namespace Vam.Server.Engine;

/// <summary>
/// Starts the meeting browser and tells the process sources its PID, so a strip on
/// <c>process:&lt;executable&gt;</c> captures that tree and no other of the same name (#96).
/// </summary>
public sealed class MeetingBrowser
{
    readonly MeetingBrowserOptions options;
    readonly Func<ProcessLoopbackSources?> sources;
    readonly Func<ProcessStartInfo, int?> start;
    readonly ILogger<MeetingBrowser> logger;

    /// <summary>Starts real processes and tells the engine's sources.</summary>
    /// <param name="options">Which browser, and with what.</param>
    /// <param name="engine">Whose process sources are told.</param>
    /// <param name="logger">Where a start is reported.</param>
    public MeetingBrowser(MeetingBrowserOptions options, VamEngine engine, ILogger<MeetingBrowser> logger)
        : this(options, () => engine.ProcessSources, StartProcess, logger)
    {
        ArgumentNullException.ThrowIfNull(engine);
    }

    /// <summary>Starts processes and tells sources the way it is told to. #131.</summary>
    /// <param name="options">Which browser, and with what.</param>
    /// <param name="sources">The process sources to tell, or null when there are none.</param>
    /// <param name="start">Starts a process and returns its PID, or null when nothing started.</param>
    /// <param name="logger">Where a start is reported.</param>
    /// <remarks>
    /// The registration of the PID is the one step everything in #96 depends on, and a test can only
    /// reach it if starting a process is something it can stand in for.
    /// </remarks>
    public MeetingBrowser(
        MeetingBrowserOptions options,
        Func<ProcessLoopbackSources?> sources,
        Func<ProcessStartInfo, int?> start,
        ILogger<MeetingBrowser> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(logger);

        this.options = options;
        this.sources = sources;
        this.start = start;
        this.logger = logger;
    }

    /// <summary>Whether there is a browser to start.</summary>
    public bool IsConfigured => options.IsConfigured;

    /// <summary>What starting the browser runs.</summary>
    /// <param name="options">Which browser, and with what.</param>
    /// <returns>The start info.</returns>
    public static ProcessStartInfo StartInfo(MeetingBrowserOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        string url = options.Url.Trim();
        string arguments = options.Arguments.Trim();

        if (url.Length > 0)
        {
            arguments = arguments.Length > 0 ? $"{arguments} \"{url}\"" : $"\"{url}\"";
        }

        return new ProcessStartInfo
        {
            FileName = options.Executable.Trim(),
            Arguments = arguments,
            UseShellExecute = false
        };
    }

    /// <summary>The name a process snapshot reports the browser under.</summary>
    /// <param name="options">Which browser.</param>
    /// <returns>Its file name, ending in <c>.exe</c>.</returns>
    public static string ExecutableName(MeetingBrowserOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        string name = Path.GetFileName(options.Executable.Trim());

        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name : name + ".exe";
    }

    /// <summary>Starts the browser.</summary>
    /// <returns>Null when it started; otherwise a sentence for the log.</returns>
    public string? Start()
    {
        if (!IsConfigured)
        {
            return $"No browser is configured under {MeetingBrowserOptions.Section}.";
        }

        // Without the sources the start would capture nothing, and the tray would say it had worked.
        if (sources() is not { } named)
        {
            return "The engine has no process sources to capture the browser with.";
        }

        try
        {
            if (start(StartInfo(options)) is not { } processId)
            {
                return "Windows did not start the browser and did not say why.";
            }

            string executableName = ExecutableName(options);

            named.Launched(processId, executableName);
            logger.LogInformation("Started the meeting browser {Executable} as PID {ProcessId}.", executableName, processId);

            return null;
        }
        catch (Exception failure) when (failure is Win32Exception or InvalidOperationException)
        {
            return failure.Message;
        }
    }

    static int? StartProcess(ProcessStartInfo info)
    {
        using Process? started = Process.Start(info);

        return started?.Id;
    }
}
