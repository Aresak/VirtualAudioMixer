using Shiny.Mediator;
using Vam.Protocol.V1;

namespace Vam.Server.Mediator.Contracts;

/// <summary>Removes a preset from the library. B12.</summary>
/// <remarks>The chains that came from it are untouched and keep working.</remarks>
/// <param name="Name">What to call it.</param>
public sealed record DeleteChainPresetRequest(string Name) : IRequest<CommandReply>;
