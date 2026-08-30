using Shiny.Mediator;
using Vam.Protocol.V1;

namespace Vam.Server.Mediator.Contracts;

/// <summary>Places one strip across a stereo bus. B8.</summary>
/// <remarks>Does nothing to a mono stream and a great deal to a monitor somebody wears for two hours.</remarks>
/// <param name="ChannelIndex">Which strip.</param>
/// <param name="Pan">From -1 for hard left to 1 for hard right.</param>
public sealed record SetPanRequest(int ChannelIndex, double Pan) : IRequest<CommandReply>;
