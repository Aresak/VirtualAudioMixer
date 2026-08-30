using Shiny.Mediator;
using Vam.Protocol.V1;

namespace Vam.Server.Mediator.Contracts;

/// <summary>Changes what the engine does at its next start. H3 and E4.</summary>
/// <remarks>Neither takes effect now, and the console says so - \"load the last console\" could not mean anything else.</remarks>
/// <param name="LoadLastConsole">Whether to come up in the console it went down in.</param>
/// <param name="RecordAutomatically">Whether to start recording with the engine.</param>
public sealed record SetStartupOptionsRequest(bool LoadLastConsole, bool RecordAutomatically) : IRequest<CommandReply>;
