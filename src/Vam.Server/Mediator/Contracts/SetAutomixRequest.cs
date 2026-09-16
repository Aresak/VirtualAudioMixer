using Shiny.Mediator;
using Vam.Protocol.V1;

namespace Vam.Server.Mediator.Contracts;

/// <summary>Sets the automixer's bypass, depth and response. C3, C10.</summary>
/// <param name="Bypassed">Whether to switch it out.</param>
/// <param name="DepthDb">How far the automixer may turn a channel down.</param>
/// <param name="ResponseMs">How fast the gain moves between microphones.</param>
public sealed record SetAutomixRequest(bool Bypassed, double DepthDb, double ResponseMs) : IRequest<CommandReply>;
