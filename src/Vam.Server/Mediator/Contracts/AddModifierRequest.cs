using Shiny.Mediator;
using Vam.Protocol.V1;

namespace Vam.Server.Mediator.Contracts;

/// <summary>Adds a link to a chain. B0a.</summary>
/// <param name="Target">Whose chain: a strip or a bus.</param>
/// <param name="ModifierId">Which modifier.</param>
/// <param name="AtIndex">Where in the chain to put it.</param>
public sealed record AddModifierRequest(ChainTarget Target, string ModifierId, int AtIndex) : IRequest<CommandReply>;
