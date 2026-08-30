using Shiny.Mediator;
using Vam.Protocol.V1;

namespace Vam.Server.Mediator.Contracts;

/// <summary>Adds a strip and opens its device. U17.</summary>
/// <param name="Name">What to call it.</param>
/// <param name="DeviceId">The endpoint, or empty for none.</param>
/// <param name="ChannelCount">How wide it is.</param>
/// <param name="ParticipatesInAutomix">Whether it takes part in gain sharing.</param>
public sealed record AddChannelRequest(
    string Name,
    string DeviceId,
    int ChannelCount,
    bool ParticipatesInAutomix
) : IRequest<CommandReply>;
