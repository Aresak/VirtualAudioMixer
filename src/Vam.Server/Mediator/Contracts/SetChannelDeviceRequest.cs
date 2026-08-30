using Shiny.Mediator;
using Vam.Protocol.V1;

namespace Vam.Server.Mediator.Contracts;

/// <summary>Points a strip at a different capture endpoint.</summary>
/// <remarks>In place rather than by rebuilding it: a strip carries its sends, its chain and its name, and none of that should be lost because a microphone came back on a different endpoint.</remarks>
/// <param name="ChannelIndex">Which strip.</param>
/// <param name="DeviceId">The endpoint, or empty for none.</param>
public sealed record SetChannelDeviceRequest(int ChannelIndex, string DeviceId) : IRequest<CommandReply>;
