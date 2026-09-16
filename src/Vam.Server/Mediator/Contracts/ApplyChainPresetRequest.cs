using Shiny.Mediator;
using Vam.Protocol.V1;

namespace Vam.Server.Mediator.Contracts;

/// <summary>Replaces a chain with a preset. B0d.</summary>
/// <remarks>A preset is a whole chain rather than a set of numbers, so applying one replaces the chain and recompiles.</remarks>
/// <param name="Target">Whose chain: a strip or a bus.</param>
/// <param name="Name">What to call it.</param>
public sealed record ApplyChainPresetRequest(ChainTarget Target, string Name) : IRequest<CommandReply>;
