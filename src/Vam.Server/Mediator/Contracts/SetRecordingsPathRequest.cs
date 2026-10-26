using Shiny.Mediator;
using Vam.Protocol.V1;

namespace Vam.Server.Mediator.Contracts;

/// <summary>Changes where recordings are written from now on.</summary>
/// <param name="Path">The folder, as the engine's machine sees it.</param>
/// <param name="Reset">Back to the default, whatever the path says.</param>
public sealed record SetRecordingsPathRequest(string Path, bool Reset) : IRequest<CommandReply>;
