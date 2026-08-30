using Shiny.Mediator;
using Vam.Protocol.V1;

namespace Vam.Server.Mediator.Contracts;

/// <summary>Sets one strip's input trim. A8.</summary>
/// <remarks>Before anything else in the chain, which is why it is not a modifier: it describes the signal arriving rather than a treatment of it.</remarks>
/// <param name="ChannelIndex">Which strip.</param>
/// <param name="Decibels">The new level.</param>
public sealed record SetTrimRequest(int ChannelIndex, double Decibels) : IRequest<CommandReply>;
