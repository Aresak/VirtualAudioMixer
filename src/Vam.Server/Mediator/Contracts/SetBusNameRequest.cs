using Shiny.Mediator;
using Vam.Protocol.V1;

namespace Vam.Server.Mediator.Contracts;

/// <summary>Renames a bus.</summary>
/// <param name="BusIndex">Which bus.</param>
/// <param name="Name">What to call it.</param>
public sealed record SetBusNameRequest(int BusIndex, string Name) : IRequest<CommandReply>;
