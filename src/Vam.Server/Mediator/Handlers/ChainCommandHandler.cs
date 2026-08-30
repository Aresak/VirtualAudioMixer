using Shiny.Mediator;
using Vam.Protocol.V1;
using Vam.Server.Engine;
using Vam.Server.Mediator.Contracts;
using Vam.Server.Services;

namespace Vam.Server.Mediator.Handlers;

/// <summary>
/// Composing a modifier chain, on a strip or on a bus. B0a, B0b and D6.
/// </summary>
/// <remarks>
/// The work itself stays in <see cref="ChainCommands"/>, which was written and tested before the
/// mediator arrived and has no reason to move. A handler that reimplemented it to look more like a
/// handler would be a rewrite of working code in exchange for nothing.
/// </remarks>
public sealed class ChainCommandHandler(VamEngine engine) :
    IRequestHandler<AddModifierRequest, CommandReply>,
    IRequestHandler<RemoveModifierRequest, CommandReply>,
    IRequestHandler<MoveModifierRequest, CommandReply>,
    IRequestHandler<SetModifierBypassRequest, CommandReply>,
    IRequestHandler<SetModifierParameterRequest, CommandReply>
{
    /// <inheritdoc />
    public Task<CommandReply> Handle(AddModifierRequest request, IMediatorContext context, CancellationToken cancellationToken) =>
        engine.Graph is { } graph
            ? Replies.DoneAsync(ChainCommands.Add(graph, engine.Modifiers, new AddModifier
            {
                Target = request.Target,
                ModifierId = request.ModifierId,
                AtIndex = request.AtIndex
            }))
            : Replies.DoneAsync(Replies.Refused("The engine is not running."));

    /// <inheritdoc />
    public Task<CommandReply> Handle(RemoveModifierRequest request, IMediatorContext context, CancellationToken cancellationToken) =>
        engine.Graph is { } graph
            ? Replies.DoneAsync(ChainCommands.Remove(graph, new RemoveModifier
            {
                Target = request.Target,
                LinkIndex = request.LinkIndex
            }))
            : Replies.DoneAsync(Replies.Refused("The engine is not running."));

    /// <inheritdoc />
    public Task<CommandReply> Handle(MoveModifierRequest request, IMediatorContext context, CancellationToken cancellationToken) =>
        engine.Graph is { } graph
            ? Replies.DoneAsync(ChainCommands.Move(graph, new MoveModifier
            {
                Target = request.Target,
                FromIndex = request.FromIndex,
                ToIndex = request.ToIndex
            }))
            : Replies.DoneAsync(Replies.Refused("The engine is not running."));

    /// <inheritdoc />
    public Task<CommandReply> Handle(SetModifierBypassRequest request, IMediatorContext context, CancellationToken cancellationToken) =>
        engine.Graph is { } graph
            ? Replies.DoneAsync(ChainCommands.SetBypass(graph, new SetModifierBypass
            {
                Target = request.Target,
                LinkIndex = request.LinkIndex,
                Bypassed = request.Bypassed
            }))
            : Replies.DoneAsync(Replies.Refused("The engine is not running."));

    /// <inheritdoc />
    public Task<CommandReply> Handle(SetModifierParameterRequest request, IMediatorContext context, CancellationToken cancellationToken) =>
        engine.Graph is { } graph
            ? Replies.DoneAsync(ChainCommands.SetParameter(graph, new SetModifierParameter
            {
                Target = request.Target,
                LinkIndex = request.LinkIndex,
                ParameterId = request.ParameterId,
                Value = request.Value
            }))
            : Replies.DoneAsync(Replies.Refused("The engine is not running."));
}
