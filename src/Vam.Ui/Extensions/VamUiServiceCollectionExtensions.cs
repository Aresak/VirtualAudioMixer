using Microsoft.Extensions.DependencyInjection;
using Vam.Ui.Abstractions;
using Vam.Ui.Localization;
using Vam.Ui.Services;
using Vam.Ui.State;

namespace Vam.Ui.Extensions;

/// <summary>
/// One call a host makes to get a console.
/// </summary>
/// <remarks>
/// The whole of a host's contribution is its startup file, its platform services and this call. If a
/// host ever needs to register something else, that something else is a feature that has escaped
/// into the hosts, and it will be written twice and fixed once.
/// </remarks>
public static class VamUiServiceCollectionExtensions
{
    /// <summary>Registers the console.</summary>
    /// <typeparam name="TPlatform">This host's platform services.</typeparam>
    /// <param name="services">Where to register it.</param>
    /// <param name="configure">A chance to point it at an engine.</param>
    /// <returns>The same collection.</returns>
    public static IServiceCollection AddVamUi<TPlatform>(
        this IServiceCollection services,
        Action<VamSessionOptions>? configure = null)
        where TPlatform : class, IPlatformServices
    {
        ArgumentNullException.ThrowIfNull(services);

        VamSessionOptions options = new();

        configure?.Invoke(options);

        services.AddSingleton(options);
        services.AddSingleton<IPlatformServices, TPlatform>();

        // Stateless, and asked once at startup by every console that opens.
        services.AddSingleton<EngineProbe>();

        // Scoped, not singleton: in a Blazor Server host every browser tab is its own circuit, and
        // one shared session would mean one tab's meter handler being replaced by the next tab's.
        // The engine does not mind; it is built to have several consoles looking at it.
        services.AddScoped<VamSessionClient>();
        services.AddScoped<IVamSession>(provider => provider.GetRequiredService<VamSessionClient>());
        services.AddScoped<ShellState>();
        services.AddScoped<VamLocalizer>();

        // Scoped with the session it connects, and for the same reason: two browser tabs are two
        // consoles, and one of them being pointed somewhere does not move the other.
        services.AddScoped<EngineConnector>();

        return services;
    }
}
