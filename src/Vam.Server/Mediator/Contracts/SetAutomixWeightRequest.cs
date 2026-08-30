using Shiny.Mediator;
using Vam.Protocol.V1;

namespace Vam.Server.Mediator.Contracts;

/// <summary>Biases the sharing for a strip that is quieter or further away by nature. C1.</summary>
/// <param name="ChannelIndex">Which strip.</param>
/// <param name="Weight">How much louder this microphone reads than the others for the same voice.</param>
public sealed record SetAutomixWeightRequest(int ChannelIndex, double Weight) : IRequest<CommandReply>;
