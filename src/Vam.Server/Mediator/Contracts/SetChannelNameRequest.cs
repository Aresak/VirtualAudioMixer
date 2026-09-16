using Shiny.Mediator;
using Vam.Protocol.V1;

namespace Vam.Server.Mediator.Contracts;

/// <summary>Renames a strip. H2.</summary>
/// <param name="ChannelIndex">Which strip.</param>
/// <param name="Name">What to call it.</param>
public sealed record SetChannelNameRequest(int ChannelIndex, string Name) : IRequest<CommandReply>;
