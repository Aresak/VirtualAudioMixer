using Shiny.Mediator;
using Vam.Protocol.V1;

namespace Vam.Server.Mediator.Contracts;

/// <summary>Removes a bus, its sends, and the device it was playing to. D1.</summary>
/// <param name="BusIndex">Which bus.</param>
public sealed record RemoveBusRequest(int BusIndex) : IRequest<CommandReply>;
