using Shiny.Mediator;
using Vam.Protocol.V1;

namespace Vam.Server.Mediator.Contracts;

/// <summary>Starts or stops the recording. E3.</summary>
/// <remarks>A refusal carries the disk's own words: \"there is room for forty minutes\" is something an operator can act on before a meeting, and \"recording failed\" is not.</remarks>
/// <param name="Recording">Whether to be recording.</param>
/// <param name="Directory">Where the session folder goes, or empty for the configured root.</param>
public sealed record SetRecordingRequest(bool Recording, string Directory) : IRequest<CommandReply>;
