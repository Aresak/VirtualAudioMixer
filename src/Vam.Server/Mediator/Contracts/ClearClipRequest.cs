using Shiny.Mediator;
using Vam.Protocol.V1;

namespace Vam.Server.Mediator.Contracts;

/// <summary>Puts a latched clip indicator out. F1.</summary>
/// <remarks>An operator action and never automatic: a clip light that cleared itself would have nothing to say by
/// the time anybody looked at it.</remarks>
/// <param name="ChannelIndex">Which strip.</param>
public sealed record ClearClipRequest(int ChannelIndex) : IRequest<CommandReply>;
