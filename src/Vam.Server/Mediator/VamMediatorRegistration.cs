using Microsoft.Extensions.DependencyInjection;
using Shiny.Mediator;
using Vam.Server.Mediator.Handlers;
using Vam.Server.Mediator.Middleware;

namespace Vam.Server.Mediator;

/// <summary>
/// Wires the application layer up. G2.
/// </summary>
/// <remarks>
/// <para>
/// Every operator action is a contract, every handler owns one kind of thing, and the cross-cutting
/// concerns are middleware rather than the same three lines copied into thirty places.
/// </para>
/// <para>
/// <b>The boundary, restated because it is the one that matters:</b> the mediator owns everything
/// above the snapshot swap and nothing below it. It allocates, resolves from DI, walks a pipeline
/// and awaits — all four forbidden on the audio thread. A handler mutates the pending configuration
/// and publishes a snapshot; the audio thread reads that snapshot and has never heard of any of this.
/// </para>
/// </remarks>
public static class VamMediatorRegistration
{
    /// <summary>Registers the mediator, the handlers and the middleware.</summary>
    /// <param name="services">Where they go.</param>
    /// <returns>The same collection.</returns>
    public static IServiceCollection AddVamMediator(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddShinyMediator(builder =>
        {
            // Performance logging is the library's, and it earns its place here: a command that
            // takes longer than it should is the first sign of a control thread being held up by
            // something, and the audio thread is on the other side of that.
            builder.AddPerformanceLoggingMiddleware();

            // Validation from the contracts' own annotations, so a nonsense index is refused before
            // a handler ever sees it rather than by each handler checking for itself.
            builder.AddDataAnnotations();

            // Ours. Every refusal reaches the log with the sentence the engine wrote for the
            // operator, so a session can be reconstructed afterwards from more than what somebody
            // remembers seeing.
            builder.AddOpenRequestMiddleware(typeof(RefusalLoggingMiddleware<,>));
        });

        // Registered explicitly rather than by scanning. Each of these implements several handler
        // interfaces, and every one of them has to be reachable through all of them.
        services.AddSingletonHandlers<ChannelCommandHandler>();
        services.AddSingletonHandlers<BusCommandHandler>();
        services.AddSingletonHandlers<ChainCommandHandler>();
        services.AddSingletonHandlers<EngineCommandHandler>();

        return services;
    }

    /// <summary>
    /// Registers one class under every handler interface it implements.
    /// </summary>
    /// <remarks>
    /// A handler that owns a strip owns eleven contracts, and registering it eleven times by hand is
    /// eleven chances to forget one. Forgetting one produces a runtime failure on an operation
    /// nobody tested by hand, which is exactly the kind of thing to let the compiler find instead.
    /// </remarks>
    /// <typeparam name="THandler">The handler.</typeparam>
    /// <param name="services">Where it goes.</param>
    static void AddSingletonHandlers<THandler>(this IServiceCollection services)
        where THandler : class
    {
        services.AddSingleton<THandler>();

        foreach (Type contract in typeof(THandler).GetInterfaces())
        {
            if (contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IRequestHandler<,>))
            {
                services.AddSingleton(contract, provider => provider.GetRequiredService<THandler>());
            }
        }
    }
}
