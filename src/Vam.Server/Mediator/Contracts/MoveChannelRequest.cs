using Shiny.Mediator;
using Vam.Protocol.V1;

namespace Vam.Server.Mediator.Contracts;

/// <summary>Moves a strip, and its sends with it. U13.</summary>
/// <remarks>From and to rather than a whole order, so two consoles reordering at once cannot each send the list they started from and silently undo each other.</remarks>
/// <param name="FromIndex">Where it is now.</param>
/// <param name="ToIndex">Where it should go.</param>
public sealed record MoveChannelRequest(int FromIndex, int ToIndex) : IRequest<CommandReply>;
