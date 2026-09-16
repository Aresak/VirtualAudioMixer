using Shiny.Mediator;
using Vam.Protocol.V1;

namespace Vam.Server.Mediator.Contracts;

/// <summary>Switches one of a strip's flags: mute, solo, pre-fade listen, polarity, mono fold, automix
/// participation. B7, A11, B8a, C2.</summary>
/// <remarks>One contract rather than six, because they are one gesture on the console and one rewrite in the graph.
/// The flag is named rather than numbered so a log line reads as something a person can act on.</remarks>
/// <param name="ChannelIndex">Which strip.</param>
/// <param name="Flag">Which flag. Named rather than numbered, so a refusal reads as something a person can act on.</param>
/// <param name="Enabled">Whether to switch it on.</param>
public sealed record SetChannelFlagRequest(int ChannelIndex, string Flag, bool Enabled) : IRequest<CommandReply>;
