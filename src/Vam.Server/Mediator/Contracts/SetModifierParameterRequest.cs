using Shiny.Mediator;
using Vam.Protocol.V1;

namespace Vam.Server.Mediator.Contracts;

/// <summary>Sets one parameter of one link.</summary>
/// <remarks>By identifier, never by position. The audio thread reads by ordinal because that is fast; configuration reads by name because that is stable.</remarks>
/// <param name="Target">Whose chain: a strip or a bus.</param>
/// <param name="LinkIndex">Which link of the chain.</param>
/// <param name="ParameterId">Which parameter, by its stable identifier.</param>
/// <param name="Value">Its new value.</param>
public sealed record SetModifierParameterRequest(ChainTarget Target, int LinkIndex, string ParameterId, double Value) : IRequest<CommandReply>;
