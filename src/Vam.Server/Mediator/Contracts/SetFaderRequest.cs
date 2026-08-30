using Shiny.Mediator;
using Vam.Protocol.V1;

namespace Vam.Server.Mediator.Contracts;

/// <summary>Moves one strip's fader. B8.</summary>
/// <param name="ChannelIndex">Which strip.</param>
/// <param name="Decibels">The new level.</param>
public sealed record SetFaderRequest(int ChannelIndex, double Decibels) : IRequest<CommandReply>;
