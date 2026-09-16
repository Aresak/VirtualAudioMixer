using Shiny.Mediator;
using Vam.Protocol.V1;

namespace Vam.Server.Mediator.Contracts;

/// <summary>Adds a bus, and opens the device behind it if it names one. D1.</summary>
/// <remarks>A monitor is one of these with a different role. That is why adding a bus and adding a monitor are one
/// code path.</remarks>
/// <param name="Name">What to call it.</param>
/// <param name="Role">Output, monitor or stream.</param>
/// <param name="ChannelCount">How wide it is.</param>
/// <param name="OutputDeviceId">The endpoint it plays to, or empty.</param>
public sealed record AddBusRequest(string Name, string Role, int ChannelCount, string OutputDeviceId) : IRequest<CommandReply>;
