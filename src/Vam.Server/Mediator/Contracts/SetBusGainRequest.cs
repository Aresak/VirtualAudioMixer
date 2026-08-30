using Shiny.Mediator;
using Vam.Protocol.V1;

namespace Vam.Server.Mediator.Contracts;

/// <summary>Sets a bus's own level.</summary>
/// <param name="BusIndex">Which bus.</param>
/// <param name="Decibels">The new level.</param>
public sealed record SetBusGainRequest(int BusIndex, double Decibels) : IRequest<CommandReply>;
