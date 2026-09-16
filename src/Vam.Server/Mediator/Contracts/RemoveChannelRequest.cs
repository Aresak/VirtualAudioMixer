using Shiny.Mediator;
using Vam.Protocol.V1;

namespace Vam.Server.Mediator.Contracts;

/// <summary>Removes a strip and every send that came from it. U17.</summary>
/// <param name="ChannelIndex">Which strip.</param>
public sealed record RemoveChannelRequest(int ChannelIndex) : IRequest<CommandReply>;
