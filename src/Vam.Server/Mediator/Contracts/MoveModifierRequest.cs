using Shiny.Mediator;
using Vam.Protocol.V1;

namespace Vam.Server.Mediator.Contracts;

/// <summary>Reorders a chain. B0a.</summary>
/// <remarks>Order is the configuration rather than an incidental list order: a gate before a denoise and a gate after one are different microphones.</remarks>
/// <param name="Target">Whose chain: a strip or a bus.</param>
/// <param name="FromIndex">Where it is now.</param>
/// <param name="ToIndex">Where it should go.</param>
public sealed record MoveModifierRequest(ChainTarget Target, int FromIndex, int ToIndex) : IRequest<CommandReply>;
