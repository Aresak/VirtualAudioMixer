using Shiny.Mediator;
using Vam.Protocol.V1;

namespace Vam.Server.Mediator.Contracts;

/// <summary>Colours a bus. U5.</summary>
/// <param name="BusIndex">Which bus.</param>
/// <param name="Colour">A hex colour.</param>
public sealed record SetBusColourRequest(int BusIndex, string Colour) : IRequest<CommandReply>;
