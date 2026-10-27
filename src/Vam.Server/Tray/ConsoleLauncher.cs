using System.Diagnostics;
using Vam.Engine.Windows.Shell;

namespace Vam.Server.Tray;

/// <summary>
/// Opens the console from the tray: brings an open one to the front, or starts one.
/// </summary>
/// <remarks>
/// Found the way the console's own EngineLauncher finds the engine: beside this process in a
/// release, or the newest build in a source checkout.
/// </remarks>
public sealed class ConsoleLauncher
{
    const string Name = "Vam.Client";
    const string Executable = Name + ".exe";

    /// <summary>Brings a console forward, or starts one.</summary>
    /// <returns>Null when one is there; otherwise a sentence for the log.</returns>
    public string? Open()
    {
        if (BringForward())
        {
            return null;
        }

        if (Locate(AppContext.BaseDirectory) is not string path)
        {
            return $"{Executable} is not beside the engine.";
        }

        try
        {
            using Process? started = Process.Start(new ProcessStartInfo
            {
                FileName = path,
                WorkingDirectory = Path.GetDirectoryName(path),
                UseShellExecute = false
            });

            return started is null ? "Windows did not start the console and did not say why." : null;
        }
        catch (Exception failure) when (failure is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return failure.Message;
        }
    }

    /// <summary>Finds the console, or returns null.</summary>
    /// <param name="here">The directory to start from: the engine's own.</param>
    /// <returns>Its path.</returns>
    public static string? Locate(string here)
    {
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

            string client = Path.Combine(folder.FullName, "src", Name);

            // The newest, because Debug and Release both build here.
            return Directory.Exists(client)
                ? Directory
                    .EnumerateFiles(client, Executable, SearchOption.AllDirectories)
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault()
                : null;
        }

        return null;
    }

    static bool BringForward()
    {
        Process[] running = Process.GetProcessesByName(Name);

        try
        {
            foreach (Process console in running)
            {
                if (console.MainWindowHandle != 0)
                {
                    TrayIcon.BringToFront(console.MainWindowHandle);

                    return true;
                }
            }

            return false;
        }
        finally
        {
            foreach (Process console in running)
            {
                console.Dispose();
            }
        }
    }
}
