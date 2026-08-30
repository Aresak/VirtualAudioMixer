using Shiny.Mediator;
using Vam.Engine.Devices.Abstractions;
using Vam.Engine.Graph;
using Vam.Protocol.V1;
using Vam.Server.Engine;
using Vam.Server.Mediator.Contracts;

namespace Vam.Server.Mediator.Handlers;

/// <summary>
/// Everything that changes a strip.
/// </summary>
/// <remarks>
/// <para>
/// One class implementing many handler interfaces rather than eleven classes with a line in each.
/// The unit of work an epic estimates is a contract; the unit of code that owns a thing is the
/// thing, and what all of these own is <see cref="ChannelConfig"/>. Splitting them would produce
/// eleven files that each say <c>Rewrite(graph, index, …)</c> and nothing else.
/// </para>
/// <para>
/// <b>Nothing here touches the audio path.</b> Every one of these either queues a command that the
/// control thread drains, or rewrites the configuration and recompiles. The audio thread reads a
/// snapshot and has never heard of the mediator.
/// </para>
/// </remarks>
public sealed class ChannelCommandHandler(VamEngine engine) :
    IRequestHandler<SetFaderRequest, CommandReply>,
    IRequestHandler<SetTrimRequest, CommandReply>,
    IRequestHandler<SetPanRequest, CommandReply>,
    IRequestHandler<SetChannelFlagRequest, CommandReply>,
    IRequestHandler<SetAutomixWeightRequest, CommandReply>,
    IRequestHandler<SetChannelNameRequest, CommandReply>,
    IRequestHandler<SetChannelColourRequest, CommandReply>,
    IRequestHandler<SetChannelDeviceRequest, CommandReply>,
    IRequestHandler<AddChannelRequest, CommandReply>,
    IRequestHandler<RemoveChannelRequest, CommandReply>,
    IRequestHandler<MoveChannelRequest, CommandReply>
{
    /// <inheritdoc />
    public Task<CommandReply> Handle(SetFaderRequest request, IMediatorContext context, CancellationToken cancellationToken) =>
        SubmitAsync(graph => graph.Submit(GraphCommand.SetFader(request.ChannelIndex, request.Decibels)));

    /// <inheritdoc />
    public Task<CommandReply> Handle(SetTrimRequest request, IMediatorContext context, CancellationToken cancellationToken) =>
        SubmitAsync(graph => graph.Submit(GraphCommand.SetTrim(request.ChannelIndex, request.Decibels)));

    /// <inheritdoc />
    public Task<CommandReply> Handle(SetPanRequest request, IMediatorContext context, CancellationToken cancellationToken) =>
        RewriteAsync(request.ChannelIndex, channel => channel with { Pan = Math.Clamp(request.Pan, -1.0, 1.0) });

    /// <inheritdoc />
    public Task<CommandReply> Handle(SetChannelFlagRequest request, IMediatorContext context, CancellationToken cancellationToken)
    {
        // Whether a strip takes part in gain sharing is not one of the audio thread's flag bits — it
        // decides what the compiler builds, not what the mix does — so it is set by rewriting the
        // strip. It arrives here because to an operator it is the same kind of thing as mute.
        if (string.Equals(request.Flag, "ParticipatesInAutomix", StringComparison.OrdinalIgnoreCase))
        {
            return RewriteAsync(request.ChannelIndex, channel => channel with { ParticipatesInAutomix = request.Enabled });
        }

        if (!Enum.TryParse(request.Flag, ignoreCase: true, out ChannelFlags flag))
        {
            return Replies.DoneAsync(Replies.Refused($"There is no flag called '{request.Flag}'."));
        }

        return SubmitAsync(graph => graph.Submit(GraphCommand.SetFlag(request.ChannelIndex, flag, request.Enabled)));
    }

    /// <inheritdoc />
    public Task<CommandReply> Handle(SetAutomixWeightRequest request, IMediatorContext context, CancellationToken cancellationToken) =>
        RewriteAsync(request.ChannelIndex, channel => channel with { AutomixWeight = (float)request.Weight });

    /// <inheritdoc />
    public Task<CommandReply> Handle(SetChannelNameRequest request, IMediatorContext context, CancellationToken cancellationToken) =>
        RewriteAsync(request.ChannelIndex, channel => channel with { Name = request.Name });

    /// <inheritdoc />
    public Task<CommandReply> Handle(SetChannelColourRequest request, IMediatorContext context, CancellationToken cancellationToken) =>
        RewriteAsync(request.ChannelIndex, channel => channel with { Colour = request.Colour });

    /// <inheritdoc />
    public Task<CommandReply> Handle(SetChannelDeviceRequest request, IMediatorContext context, CancellationToken cancellationToken) =>
        engine.RetargetChannel(request.ChannelIndex, new AudioDeviceId(request.DeviceId))
            ? Replies.DoneAsync(Replies.Accepted())
            : Replies.DoneAsync(Replies.Refused($"There is no strip {request.ChannelIndex}."));

    /// <inheritdoc />
    public Task<CommandReply> Handle(AddChannelRequest request, IMediatorContext context, CancellationToken cancellationToken) =>
        engine.AddChannel(new ChannelConfig
        {
            DeviceId = new AudioDeviceId(request.DeviceId),
            Name = request.Name,
            ChannelCount = Math.Max(request.ChannelCount, 1),
            ParticipatesInAutomix = request.ParticipatesInAutomix
        }) >= 0
            ? Replies.DoneAsync(Replies.Accepted())
            : Replies.DoneAsync(Replies.Refused("The engine is not running."));

    /// <inheritdoc />
    public Task<CommandReply> Handle(RemoveChannelRequest request, IMediatorContext context, CancellationToken cancellationToken) =>
        engine.Graph is { } graph && graph.RemoveChannel(request.ChannelIndex)
            ? Replies.DoneAsync(Replies.Accepted())
            : Replies.DoneAsync(Replies.Refused($"There is no strip {request.ChannelIndex}."));

    /// <inheritdoc />
    public Task<CommandReply> Handle(MoveChannelRequest request, IMediatorContext context, CancellationToken cancellationToken) =>
        engine.Graph is { } graph && graph.MoveChannel(request.FromIndex, request.ToIndex)
            ? Replies.DoneAsync(Replies.Accepted())
            : Replies.DoneAsync(Replies.Refused("A strip cannot be moved to a place that is not there."));

    /// <summary>
    /// Rewrites one strip's configuration and recompiles.
    /// </summary>
    /// <remarks>
    /// <see cref="ChannelConfig"/> is a record with init-only properties, so a change is a new one
    /// put back in place. That is what makes a half-applied strip impossible: either the whole thing
    /// went in or none of it did.
    /// </remarks>
    Task<CommandReply> RewriteAsync(int index, Func<ChannelConfig, ChannelConfig> change)
    {
        if (engine.Graph is not { } graph || index < 0 || index >= graph.Config.Channels.Count)
        {
            return Replies.DoneAsync(Replies.Refused($"There is no strip {index}."));
        }

        graph.Config.Channels[index] = change(graph.Config.Channels[index]);
        graph.Recompile();

        return Replies.DoneAsync(Replies.Accepted());
    }

    Task<CommandReply> SubmitAsync(Action<GraphController> queue)
    {
        if (engine.Graph is not { } graph)
        {
            return Replies.DoneAsync(Replies.Refused("The engine is not running."));
        }

        queue(graph);
        graph.Pump();

        return Replies.DoneAsync(Replies.Accepted());
    }
}
