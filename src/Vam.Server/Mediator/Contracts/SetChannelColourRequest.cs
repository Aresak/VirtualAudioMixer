using Shiny.Mediator;
using Vam.Protocol.V1;

namespace Vam.Server.Mediator.Contracts;

/// <summary>Colours a strip. U5.</summary>
/// <remarks>Kept by the engine, so two operators watching the same meeting see the same room.</remarks>
/// <param name="ChannelIndex">Which strip.</param>
/// <param name="Colour">A hex colour.</param>
public sealed record SetChannelColourRequest(int ChannelIndex, string Colour) : IRequest<CommandReply>;
