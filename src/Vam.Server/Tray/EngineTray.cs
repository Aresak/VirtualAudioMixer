using System.Globalization;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Shiny.Mediator;
using Vam.Engine.Windows.Shell;
using Vam.Protocol.V1;
using Vam.Server.Engine;

namespace Vam.Server.Tray;

/// <summary>
/// The engine's icon in the notification area, there exactly while the engine runs.
/// </summary>
/// <remarks>
/// It reads what the engine publishes and sends commands only through the mediator, the same
/// requests a console sends. It never touches the graph, and nothing here is near the audio path.
/// </remarks>
public sealed class EngineTray(
    VamEngine engine,
    MeetingBrowser meetingBrowser,
    IMediator mediator,
    IServer server,
    ILogger<EngineTray> logger)
    : IHostedService, ITrayModel, IDisposable
{
    readonly TrayText text = new(CultureInfo.CurrentUICulture);
    readonly ConsoleLauncher launcher = new();

    TrayIcon? icon;

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!TrayIcon.CanShow)
        {
            logger.LogInformation("No interactive session, so no tray icon.");

            return Task.CompletedTask;
        }

        icon = new TrayIcon(this, LoadIcons(), logger);
        icon.Start();

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        Dispose();

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        icon?.Dispose();
        icon = null;
    }

    /// <inheritdoc />
    public TrayView Read()
    {
        TrayStatus status = new()
        {
            Address = Address(),
            CanStartMeetingBrowser = meetingBrowser.IsConfigured,
            Dropouts = engine.Dropouts.TotalRecorded,
            FaultedStrips = engine.Faults?.FaultedCount ?? 0,
            RecordingFor = engine.Recording is { IsRecording: true } recording
                ? DateTimeOffset.Now - recording.StartedAt
                : null
        };

        return new TrayPresenter(text).Present(status);
    }

    /// <inheritdoc />
    public void Choose(int id)
    {
        TrayCommand command = (TrayCommand)id;

        if (command == TrayCommand.OpenConsole)
        {
            if (launcher.Open() is { } problem)
            {
                logger.LogWarning("The console was not opened from the tray: {Problem}", problem);
                icon?.Tell(text["tray.notDone"], text["tray.noConsole"]);
            }

            return;
        }

        if (command == TrayCommand.StartMeetingBrowser)
        {
            if (meetingBrowser.Start() is { } problem)
            {
                logger.LogWarning("The meeting browser was not started from the tray: {Problem}", problem);
                icon?.Tell(text["tray.notDone"], text["tray.noMeetingBrowser"]);
            }

            return;
        }

        if (TrayPresenter.RequestFor(command) is { } request)
        {
            // Not awaited: the tray thread pumps messages and must not wait on the engine.
            _ = SendAsync(request);
        }
    }

    static TrayIcons LoadIcons() => new(Resource("running"), Resource("recording"), Resource("fault"));

    static byte[] Resource(string state)
    {
        using Stream stream = typeof(EngineTray).Assembly.GetManifestResourceStream($"Vam.Server.Tray.engine-{state}.ico")
            ?? throw new InvalidOperationException($"The {state} tray icon is not embedded.");
        using MemoryStream copy = new();

        stream.CopyTo(copy);

        return copy.ToArray();
    }

    string Address() =>
        server.Features.Get<IServerAddressesFeature>()?.Addresses.FirstOrDefault() ?? string.Empty;

    async Task SendAsync(IRequest<CommandReply> request)
    {
        try
        {
            (IMediatorContext _, CommandReply reply) = await mediator.Request(request).ConfigureAwait(false);

            if (!reply.Accepted)
            {
                // The engine's own sentence, as a console would show it.
                icon?.Tell(text["tray.notDone"], reply.Reason);
            }
        }
        catch (Exception failure)
        {
            logger.LogError(failure, "{Operation} from the tray failed.", request.GetType().Name);
        }
    }
}
