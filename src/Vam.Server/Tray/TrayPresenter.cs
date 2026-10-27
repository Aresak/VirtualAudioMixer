using System.Globalization;
using Shiny.Mediator;
using Vam.Engine.Windows.Shell;
using Vam.Protocol.V1;
using Vam.Server.Mediator.Contracts;

namespace Vam.Server.Tray;

/// <summary>
/// Turns what the engine is doing into what the tray shows, and a menu item into a mediator request.
/// </summary>
/// <param name="text">The words.</param>
public sealed class TrayPresenter(TrayText text)
{
    /// <summary>The reason written to the log when the tray stops the engine.</summary>
    public const string ShutdownReason = "Stopped from the tray icon.";

    /// <summary>What the tray shows for this status.</summary>
    /// <param name="status">The engine's state.</param>
    /// <returns>The icon, tooltip and menu.</returns>
    public TrayView Present(TrayStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);

        TrayGlyph glyph = status.FaultedStrips > 0
            ? TrayGlyph.Fault
            : status.IsRecording ? TrayGlyph.Recording : TrayGlyph.Running;

        TrayMenuItem[] menu =
        [
            new((int)TrayCommand.OpenConsole, text["tray.openConsole"]),
            status.IsRecording
                ? new((int)TrayCommand.ToggleRecording, text["tray.stopRecording"], text["tray.stopRecordingWhy"])
                : new((int)TrayCommand.ToggleRecording, text["tray.startRecording"]),
            TrayMenuItem.Separator,
            new((int)TrayCommand.StopEngine, text["tray.stopEngine"], status.IsRecording ? text["tray.stopEngineWhy"] : null)
        ];

        return new TrayView(glyph, Tooltip(status), text["tray.caption"], menu, (int)TrayCommand.OpenConsole);
    }

    /// <summary>The mediator request a menu item stands for.</summary>
    /// <param name="command">The item.</param>
    /// <param name="status">The state the menu was shown in.</param>
    /// <returns>The request, or null for an item that is not one.</returns>
    public static IRequest<CommandReply>? RequestFor(TrayCommand command, TrayStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);

        return command switch
        {
            TrayCommand.ToggleRecording => new SetRecordingRequest(!status.IsRecording, string.Empty),
            TrayCommand.StopEngine => new ShutdownRequest(ShutdownReason),
            _ => null
        };
    }

    /// <summary>The hover text.</summary>
    /// <param name="status">The engine's state.</param>
    /// <returns>A few lines.</returns>
    public string Tooltip(TrayStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);

        List<string> lines =
        [
            status.RecordingFor is { } elapsed
                ? text.Format("tray.recording", Elapsed(elapsed))
                : text["tray.running"]
        ];

        // Second, because Windows cuts a long tooltip short and this is the line that must survive.
        if (status.FaultedStrips > 0)
        {
            lines.Add(text.Format("tray.fault", status.FaultedStrips));
        }

        lines.Add(text.Format("tray.address", status.Address));
        lines.Add(text.Format("tray.dropouts", status.Dropouts));

        return string.Join('\n', lines);
    }

    static string Elapsed(TimeSpan elapsed) =>
        string.Create(CultureInfo.InvariantCulture, $"{(int)elapsed.TotalHours}:{elapsed.Minutes:00}:{elapsed.Seconds:00}");
}
