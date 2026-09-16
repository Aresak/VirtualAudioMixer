using Shiny.Mediator;
using Vam.Protocol.V1;

namespace Vam.Server.Mediator.Contracts;

/// <summary>Changes where recordings are written from now on.</summary>
/// <param name="Path">The folder, as the engine's machine sees it.</param>
public sealed record SetRecordingsPathRequest(string Path) : IRequest<CommandReply>;
