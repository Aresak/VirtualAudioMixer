using Shiny.Mediator;
using Vam.Engine.Devices.Abstractions;
using Vam.Engine.Graph;
using Vam.Protocol.V1;
using Vam.Server.Engine;
using Vam.Server.Mediator.Contracts;
using EngineSendState = Vam.Engine.Graph.SendState;

namespace Vam.Server.Mediator.Handlers;

/// <summary>
/// Everything that changes a bus, and the sends that reach one.
/// </summary>
/// <remarks>
/// A monitor is a bus with a different role, so there is no separate monitor handler and there never
/// will be. The role decides exactly three behaviours — the default send tap, whether it obeys solo,
/// and whether it needs an output device — and that is the whole difference.
/// </remarks>
public sealed class BusCommandHandler(VamEngine engine) :
    IRequestHandler<SetBusGainRequest, CommandReply>,
    IRequestHandler<SetBusMutedRequest, CommandReply>,
    IRequestHandler<SetBusNameRequest, CommandReply>,
    IRequestHandler<SetBusColourRequest, CommandReply>,
    IRequestHandler<SetBusRoleRequest, CommandReply>,
    IRequestHandler<SetBusOutputDeviceRequest, CommandReply>,
    IRequestHandler<AddBusRequest, CommandReply>,
    IRequestHandler<RemoveBusRequest, CommandReply>,
    IRequestHandler<SetSendRequest, CommandReply>
{
    /// <inheritdoc />
    public Task<CommandReply> Handle(SetBusGainRequest request, IMediatorContext context, CancellationToken cancellationToken) =>
        SubmitAsync(graph => graph.Submit(GraphCommand.SetBusGain(request.BusIndex, request.Decibels)));

    /// <inheritdoc />
    public Task<CommandReply> Handle(SetBusMutedRequest request, IMediatorContext context, CancellationToken cancellationToken) =>
        SubmitAsync(graph => graph.Submit(GraphCommand.SetBusMuted(request.BusIndex, request.Muted)));

    /// <inheritdoc />
    public Task<CommandReply> Handle(SetBusNameRequest request, IMediatorContext context, CancellationToken cancellationToken) =>
        RewriteAsync(request.BusIndex, bus => bus with { Name = request.Name });

    /// <inheritdoc />
    public Task<CommandReply> Handle(SetBusColourRequest request, IMediatorContext context, CancellationToken cancellationToken) =>
        RewriteAsync(request.BusIndex, bus => bus with { Colour = request.Colour });

    /// <inheritdoc />
    public Task<CommandReply> Handle(SetBusRoleRequest request, IMediatorContext context, CancellationToken cancellationToken) =>
        Enum.TryParse(request.Role, ignoreCase: true, out BusRole role)
            ? RewriteAsync(request.BusIndex, bus => bus with { Role = role })
            : Replies.DoneAsync(Replies.Refused($"There is no bus role called '{request.Role}'."));

    /// <inheritdoc />
    public Task<CommandReply> Handle(SetBusOutputDeviceRequest request, IMediatorContext context, CancellationToken cancellationToken)
    {
        Task<CommandReply> reply = RewriteAsync(
            request.BusIndex,
            bus => bus with { OutputDeviceId = new AudioDeviceId(request.DeviceId) });

        // Both halves, always together. Changing the configuration without re-opening the device
        // thread leaves the bus playing to the endpoint it used to have, which is worse than
        // silence: it is audio arriving somewhere nobody is expecting it.
        engine.RebindBusOutputs();

        return reply;
    }

    /// <inheritdoc />
    public Task<CommandReply> Handle(AddBusRequest request, IMediatorContext context, CancellationToken cancellationToken)
    {
        if (engine.Graph is not { } graph)
        {
            return Replies.DoneAsync(Replies.Refused("The engine is not running."));
        }

        graph.AddBus(new BusConfig
        {
            Name = request.Name,
            Role = Enum.TryParse(request.Role, ignoreCase: true, out BusRole role) ? role : BusRole.Output,
            ChannelCount = Math.Max(request.ChannelCount, 1),
            OutputDeviceId = new AudioDeviceId(request.OutputDeviceId)
        });

        // A new bus naming an output device needs a thread opened behind it, or it mixes into a ring
        // nobody drains and is silent with no error anywhere to explain it.
        engine.RebindBusOutputs();

        return Replies.DoneAsync(Replies.Accepted());
    }

    /// <inheritdoc />
    public Task<CommandReply> Handle(RemoveBusRequest request, IMediatorContext context, CancellationToken cancellationToken)
    {
        if (engine.Graph is not { } graph || !graph.RemoveBus(request.BusIndex))
        {
            return Replies.DoneAsync(Replies.Refused($"There is no bus {request.BusIndex}."));
        }

        // Closed, not merely unbound. A render stream left open on a bus that no longer exists is a
        // device an operator cannot use for anything else until the engine restarts.
        engine.RebindBusOutputs();

        return Replies.DoneAsync(Replies.Accepted());
    }

    /// <inheritdoc />
    public Task<CommandReply> Handle(SetSendRequest request, IMediatorContext context, CancellationToken cancellationToken)
    {
        if (engine.Graph is not { } graph)
        {
            return Replies.DoneAsync(Replies.Refused("The engine is not running."));
        }

        GraphSnapshot before = graph.Publisher.Current;

        if (request.ChannelIndex < before.Sends.ChannelCount
            && request.BusIndex < before.Sends.BusCount
            && before.Sends.StateOf(request.ChannelIndex, request.BusIndex) == EngineSendState.ExcludedMixMinus)
        {
            // Said out loud rather than silently doing nothing. An operator clicking a send that does
            // not respond needs to know it is mix-minus and not a broken button.
            return Replies.DoneAsync(Replies.Refused(
                "That send is excluded by mix-minus: the bus feeds the device this microphone belongs to, "
                + "and sending it there would play somebody their own voice, late."));
        }

        graph.Submit(GraphCommand.SetSend(request.ChannelIndex, request.BusIndex, request.On, request.Decibels));
        graph.Pump();

        return Replies.DoneAsync(Replies.Accepted());
    }

    Task<CommandReply> RewriteAsync(int index, Func<BusConfig, BusConfig> change)
    {
        if (engine.Graph is not { } graph || index < 0 || index >= graph.Config.Buses.Count)
        {
            return Replies.DoneAsync(Replies.Refused($"There is no bus {index}."));
        }

        graph.Config.Buses[index] = change(graph.Config.Buses[index]);
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
