using Shiny.Mediator;
using Vam.Protocol.V1;

namespace Vam.Server.Mediator.Contracts;

/// <summary>Switches one input-to-bus send, and sets its level. D2 and D2a.</summary>
/// <remarks>Refused with a reason when mix-minus excludes it, because a button that does nothing teaches an operator the console is broken.</remarks>
/// <param name="ChannelIndex">Which strip.</param>
/// <param name="BusIndex">Which bus.</param>
/// <param name="On">Whether the send is switched on.</param>
/// <param name="Decibels">The new level.</param>
public sealed record SetSendRequest(int ChannelIndex, int BusIndex, bool On, double Decibels) : IRequest<CommandReply>;
