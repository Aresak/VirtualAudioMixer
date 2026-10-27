using System.ComponentModel;
using System.Diagnostics;

namespace Vam.Server.Engine;

/// <summary>
/// Starts the meeting browser and tells the process sources its PID, so a strip on
/// <c>process:&lt;executable&gt;</c> captures that tree and no other of the same name (#96).
/// </summary>
/// <param name="options">Which browser, and with what.</param>
/// <param name="engine">Whose process sources are told.</param>
/// <param name="logger">Where a start is reported.</param>
public sealed class MeetingBrowser(MeetingBrowserOptions options, VamEngine engine, ILogger<MeetingBrowser> logger)
{
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

    /// <summary>Starts the browser.</summary>
    /// <returns>Null when it started; otherwise a sentence for the log.</returns>
    public string? Start()
    {
        if (!IsConfigured)
        {
            return $"No browser is configured under {MeetingBrowserOptions.Section}.";
        }

        ProcessStartInfo start = StartInfo(options);

        try
        {
            using Process? started = Process.Start(start);

            if (started is null)
            {
                return "Windows did not start the browser and did not say why.";
            }

            string executableName = Path.GetFileName(start.FileName);

            engine.ProcessSources?.Launched(started.Id, executableName);
            logger.LogInformation("Started the meeting browser {Executable} as PID {ProcessId}.", executableName, started.Id);

            return null;
        }
        catch (Exception failure) when (failure is Win32Exception or InvalidOperationException)
        {
            return failure.Message;
        }
    }
}
