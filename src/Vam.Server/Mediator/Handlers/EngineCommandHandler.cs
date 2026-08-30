using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shiny.Mediator;
using Vam.Engine.Recording;
using Vam.Protocol.V1;
using Vam.Server.Engine;
using Vam.Server.Mediator.Contracts;
using Vam.Server.Services;

namespace Vam.Server.Mediator.Handlers;

/// <summary>
/// The operations that change what the engine is doing rather than what the graph holds.
/// </summary>
/// <remarks>
/// A file to create, a meter to clear, a preset library to write, a setting for the next start.
/// None of them is a parameter of the mix, which is why they are here and not on one of the other
/// handlers.
/// </remarks>
public sealed class EngineCommandHandler(
    VamEngine engine,
    IHostApplicationLifetime lifetime,
    ILogger<EngineCommandHandler> logger) :
    IRequestHandler<SetAutomixRequest, CommandReply>,
    IRequestHandler<ShutdownRequest, CommandReply>,
    IRequestHandler<SetRecordingRequest, CommandReply>,
    IRequestHandler<SetStartupOptionsRequest, CommandReply>,
    IRequestHandler<ClearClipRequest, CommandReply>,
    IRequestHandler<SaveChainPresetRequest, CommandReply>,
    IRequestHandler<ApplyChainPresetRequest, CommandReply>,
    IRequestHandler<DeleteChainPresetRequest, CommandReply>
{
    /// <summary>How long the reply gets to leave before the transport carrying it is torn down.</summary>
    static readonly TimeSpan ShutdownGrace = TimeSpan.FromMilliseconds(250);

    /// <inheritdoc />
    public Task<CommandReply> Handle(ShutdownRequest request, IMediatorContext context, CancellationToken cancellationToken)
    {
        logger.LogInformation("Shutting down. {Reason}", request.Reason);

        // Deferred by a moment. This reply travels out through the transport StopApplication tears
        // down, and graceful shutdown would usually drain it first - but "usually" is a poor
        // guarantee for the one command whose confirmation somebody is sitting there waiting on.
        _ = Task.Run(
            async () =>
            {
                await Task.Delay(ShutdownGrace).ConfigureAwait(false);

                lifetime.StopApplication();
            },
            CancellationToken.None);

        return Replies.DoneAsync(Replies.Accepted());
    }

    /// <inheritdoc />
    public Task<CommandReply> Handle(SetAutomixRequest request, IMediatorContext context, CancellationToken cancellationToken)
    {
        if (engine.Graph is not { } graph)
        {
            return Replies.DoneAsync(Replies.Refused("The engine is not running."));
        }

        graph.Config.IsAutomixBypassed = request.Bypassed;
        graph.Config.AutomixDepthDb = request.DepthDb;
        graph.Config.AutomixResponseMilliseconds = request.ResponseMs;

        graph.Recompile();

        return Replies.DoneAsync(Replies.Accepted());
    }

    /// <inheritdoc />
    public Task<CommandReply> Handle(SetRecordingRequest request, IMediatorContext context, CancellationToken cancellationToken)
    {
        if (!request.Recording)
        {
            return Replies.DoneAsync(engine.StopRecording()
                ? Replies.Accepted()
                : Replies.Refused("Nothing was recording."));
        }

        DiskVerdict verdict = engine.StartRecording(
            string.IsNullOrWhiteSpace(request.Directory) ? null : request.Directory);

        // The disk's answer in the disk's own words. "There is room for forty minutes" is something
        // an operator can act on before a meeting; "recording failed" is not.
        return Replies.DoneAsync(verdict.CanStart
            ? Replies.Accepted()
            : Replies.Refused(verdict.Description));
    }

    /// <inheritdoc />
    public Task<CommandReply> Handle(
        SetStartupOptionsRequest request,
        IMediatorContext context,
        CancellationToken cancellationToken
    )
    {
        engine.SetStartup(request.LoadLastConsole, request.RecordAutomatically);

        return Replies.DoneAsync(Replies.Accepted());
    }

    /// <inheritdoc />
    public Task<CommandReply> Handle(ClearClipRequest request, IMediatorContext context, CancellationToken cancellationToken)
    {
        // The one operation that touches the meters rather than the graph.
        engine.Meters?.ClearClip(request.ChannelIndex);

        return Replies.DoneAsync(Replies.Accepted());
    }

    /// <inheritdoc />
    public Task<CommandReply> Handle(
        SaveChainPresetRequest request,
        IMediatorContext context,
        CancellationToken cancellationToken
    ) =>
        engine.Graph is { } graph
            ? Replies.DoneAsync(PresetCommands.Save(graph, engine.Presets, new SaveChainPreset
            {
                Target = request.Target,
                Name = request.Name
            }))
            : Replies.DoneAsync(Replies.Refused("The engine is not running."));

    /// <inheritdoc />
    public Task<CommandReply> Handle(
        ApplyChainPresetRequest request,
        IMediatorContext context,
        CancellationToken cancellationToken
    ) =>
        engine.Graph is { } graph
            ? Replies.DoneAsync(PresetCommands.Apply(graph, engine.Presets, new ApplyChainPreset
            {
                Target = request.Target,
                Name = request.Name
            }))
            : Replies.DoneAsync(Replies.Refused("The engine is not running."));

    /// <inheritdoc />
    public Task<CommandReply> Handle(
        DeleteChainPresetRequest request,
        IMediatorContext context,
        CancellationToken cancellationToken
    ) =>
        Replies.DoneAsync(PresetCommands.Delete(engine.Presets, new DeleteChainPreset { Name = request.Name }));
}
