using Shiny.Mediator;
using Vam.Protocol.V1;

namespace Vam.Server.Mediator.Contracts;

/// <summary>Removes a link from a chain. B0a.</summary>
/// <param name="Target">Whose chain: a strip or a bus.</param>
/// <param name="LinkIndex">Which link of the chain.</param>
public sealed record RemoveModifierRequest(ChainTarget Target, int LinkIndex) : IRequest<CommandReply>;
