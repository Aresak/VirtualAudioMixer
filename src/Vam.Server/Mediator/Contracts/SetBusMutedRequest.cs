using Shiny.Mediator;
using Vam.Protocol.V1;

namespace Vam.Server.Mediator.Contracts;

/// <summary>Silences a bus.</summary>
/// <param name="BusIndex">Which bus.</param>
/// <param name="Muted">/// <param name="Muted">The muted.</param></param>
public sealed record SetBusMutedRequest(int BusIndex, bool Muted) : IRequest<CommandReply>;
