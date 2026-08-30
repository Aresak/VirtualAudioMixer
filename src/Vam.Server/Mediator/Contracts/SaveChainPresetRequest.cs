using Shiny.Mediator;
using Vam.Protocol.V1;

namespace Vam.Server.Mediator.Contracts;

/// <summary>Saves a chain under a name. B0d and B12.</summary>
/// <param name="Target">Whose chain: a strip or a bus.</param>
/// <param name="Name">What to call it.</param>
public sealed record SaveChainPresetRequest(ChainTarget Target, string Name) : IRequest<CommandReply>;
