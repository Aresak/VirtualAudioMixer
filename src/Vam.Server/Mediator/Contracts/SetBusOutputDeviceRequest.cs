using Shiny.Mediator;
using Vam.Protocol.V1;

namespace Vam.Server.Mediator.Contracts;

/// <summary>Points a bus at a different endpoint, and re-opens the device thread behind it. D7.</summary>
/// <remarks>Both halves together, always. A bus whose configuration changed without its thread being re-opened keeps playing to the device it used to have, which is worse than silence.</remarks>
/// <param name="BusIndex">Which bus.</param>
/// <param name="DeviceId">The endpoint, or empty for none.</param>
public sealed record SetBusOutputDeviceRequest(int BusIndex, string DeviceId) : IRequest<CommandReply>;
