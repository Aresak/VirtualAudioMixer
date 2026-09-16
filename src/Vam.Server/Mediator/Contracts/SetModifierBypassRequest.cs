using Shiny.Mediator;
using Vam.Protocol.V1;

namespace Vam.Server.Mediator.Contracts;

/// <summary>Switches one link out, or back in. B0b.</summary>
/// <param name="Target">Whose chain: a strip or a bus.</param>
/// <param name="LinkIndex">Which link of the chain.</param>
/// <param name="Bypassed">Whether to switch it out.</param>
public sealed record SetModifierBypassRequest(ChainTarget Target, int LinkIndex, bool Bypassed) : IRequest<CommandReply>;
