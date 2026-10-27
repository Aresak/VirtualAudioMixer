using Vam.Engine.Graph;
using Vam.Protocol.V1;
using Vam.Server.Engine;

namespace Vam.Server.Mediator;

/// <summary>
/// How a handler changes the console: on the control loop, and answering once it has.
/// </summary>
/// <remarks>
/// Handlers run on the gRPC thread pool. The configuration and the retire queue have one writer, the
/// control loop, so a handler hands its work over rather than doing it where it happens to be.
/// </remarks>
public static class ControlLoopExtensions
{
    /// <summary>Runs a command against the graph on the control loop.</summary>
    /// <param name="engine">The engine.</param>
    /// <param name="command">The command. Runs on the control loop.</param>
    /// <param name="cancellationToken">Stops the waiting.</param>
    /// <returns>The command's reply, or a refusal when the engine is not running.</returns>
    public static async Task<CommandReply> CommandAsync(
        this VamEngine engine,
        Func<GraphController, CommandReply> command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(command);

        if (engine.Graph is not { } graph)
        {
            return Replies.Refused("The engine is not running.");
        }

        try
        {
            return await engine.OnControlLoopAsync(() => command(graph), cancellationToken).ConfigureAwait(false);
        }
        catch (EngineStoppedException stopped)
        {
            return Replies.Refused(stopped.Message);
        }
    }
}
