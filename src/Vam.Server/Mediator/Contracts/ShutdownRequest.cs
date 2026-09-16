using Shiny.Mediator;
using Vam.Protocol.V1;

namespace Vam.Server.Mediator.Contracts;

/// <summary>Stops the engine.</summary>
/// <remarks>
/// <para>
/// The only command that ends a session, and it is a command rather than something done to the
/// process from outside for a reason: asked this way, the engine saves the console, closes the
/// recording files and lets go of the devices. Killed, it does none of those.
/// </para>
/// <para>
/// A console never sends this on its own. G1 has the session outliving every console, so the only
/// thing that ends one is a person saying so.
/// </para>
/// </remarks>
/// <param name="Reason">What to write in the log, so a session that ended can be accounted for.</param>
public sealed record ShutdownRequest(string Reason) : IRequest<CommandReply>;
