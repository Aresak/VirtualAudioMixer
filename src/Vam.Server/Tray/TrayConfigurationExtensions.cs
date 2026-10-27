namespace Vam.Server.Tray;

/// <summary>Reads the tray's one setting.</summary>
public static class TrayConfigurationExtensions
{
    /// <summary>The key. On unless it says false.</summary>
    public const string Key = "Vam:Tray";

    /// <summary>Whether the engine should show a tray icon.</summary>
    /// <param name="configuration">The engine's configuration.</param>
    /// <returns>False only for an explicit false.</returns>
    public static bool WantsTray(this IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return !bool.TryParse(configuration[Key], out bool wanted) || wanted;
    }
}
