using Shiny.Mediator;
using Vam.Protocol.V1;

namespace Vam.Server.Mediator.Contracts;

/// <summary>Changes what a bus is for. D5.</summary>
/// <remarks>The role decides exactly three behaviours - its default send tap, whether it obeys solo, and whether it needs an output device - so changing it is one command rather than rebuilding the bus.</remarks>
/// <param name="BusIndex">Which bus.</param>
/// <param name="Role">Output, monitor or stream.</param>
public sealed record SetBusRoleRequest(int BusIndex, string Role) : IRequest<CommandReply>;
