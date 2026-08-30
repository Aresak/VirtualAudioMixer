using System.Diagnostics;

namespace Vam.Client;

/// <summary>
/// Starts the engine that ships beside this console.
/// </summary>
/// <remarks>
/// <para>
/// The console and the engine are two processes on purpose — G1 has the session outliving every
/// console — but somebody who installed one application should not have to know that, or be told to
/// start a second thing before the first one will work. This is what makes the two-process design
/// invisible to the person the software is for.
/// </para>
/// <para>
/// It does not keep a handle, register a job object, or stop the engine when the console closes.
/// That is the whole point of the split: closing the window must not end the meeting.
/// </para>
/// </remarks>
public sealed class EngineLauncher
{
    const string Executable = "Vam.Server.exe";

    /// <summary>Starts an engine, or says what stopped it.</summary>
    /// <param name="address">Where it should listen.</param>
    /// <returns>Null when the process started; otherwise a sentence.</returns>
    public string? Start(string address)
    {
        if (Locate() is not string path)
        {
            return $"{Executable} is not beside the console. A release ships both together, so this "
                + "installation is incomplete.";
        }

        ProcessStartInfo start = new()
        {
            FileName = path,
            WorkingDirectory = Path.GetDirectoryName(path),

            // No window. An engine is a service that happens not to be registered as one, and a
            // console window somebody can close by accident is a meeting somebody can end by
            // accident.
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Double underscore is how the configuration builder reads a colon out of the environment.
        // Passed rather than assumed, so the console and the engine cannot disagree about the port
        // even if the engine's own default changes.
        start.EnvironmentVariables["Vam__Listen"] = address;

        try
        {
            return Process.Start(start) is null
                ? "Windows did not start the engine and did not say why."
                : null;
        }
        catch (Exception failure) when (failure is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return failure.Message;
        }
    }

    /// <summary>Finds the engine, or returns null.</summary>
    /// <remarks>
    /// Beside the console is the answer that matters: it is where a release puts it, and it is the
    /// only case a person who downloaded an installer will ever be in. The walk up the tree is for a
    /// source checkout, where the two projects build into their own directories and nothing has been
    /// packaged yet — without it this feature could only be tested by publishing first.
    /// </remarks>
    static string? Locate()
    {
        string here = AppContext.BaseDirectory;
        string beside = Path.Combine(here, Executable);

        if (File.Exists(beside))
        {
            return beside;
        }

        for (DirectoryInfo? folder = new(here); folder is not null; folder = folder.Parent)
        {
            if (!File.Exists(Path.Combine(folder.FullName, "VirtualAudioMixer.sln")))
            {
                continue;
            }

            return Directory
                .EnumerateFiles(Path.Combine(folder.FullName, "src", "Vam.Server"), Executable, SearchOption.AllDirectories)
                .FirstOrDefault();
        }

        return null;
    }
}
