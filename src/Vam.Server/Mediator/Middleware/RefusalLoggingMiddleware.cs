using Microsoft.Extensions.Logging;
using Shiny.Mediator;
using Vam.Protocol.V1;

namespace Vam.Server.Mediator.Middleware;

/// <summary>
/// Writes down every operation the engine refused, and why.
/// </summary>
/// <remarks>
/// <para>
/// The reason middleware was worth the mediator. Before this, a refusal reached one console and
/// nowhere else — an operator saw "that send is excluded by mix-minus", closed the panel, and the
/// engine's log had no idea anything had happened. An hour later nobody could reconstruct what had
/// been tried.
/// </para>
/// <para>
/// One line per refusal, at warning, naming the contract and carrying the sentence the engine wrote
/// for the operator. Accepted operations are not logged at all: thirty fader moves a minute would
/// bury everything else, and a fader that moved is visible in the console state anyway.
/// </para>
/// <para>
/// Open middleware, so it applies to every request without being listed against each one. A refusal
/// somebody forgot to log is a refusal that stops existing the moment the console is closed.
/// </para>
/// </remarks>
/// <typeparam name="TRequest">The contract.</typeparam>
/// <typeparam name="TResult">What it returns.</typeparam>
public sealed class RefusalLoggingMiddleware<TRequest, TResult>(ILogger<RefusalLoggingMiddleware<TRequest, TResult>> logger)
    : IRequestMiddleware<TRequest, TResult>
    where TRequest : IRequest<TResult>
{
    /// <inheritdoc />
    public async Task<TResult> Process(
        IMediatorContext context,
        RequestHandlerDelegate<TResult> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        TResult result = await next().ConfigureAwait(false);

        if (result is CommandReply { Accepted: false } refusal)
        {
            logger.LogWarning(
                "{Operation} was refused: {Reason}",
                typeof(TRequest).Name,
                refusal.Reason);
        }

        return result;
    }
}
