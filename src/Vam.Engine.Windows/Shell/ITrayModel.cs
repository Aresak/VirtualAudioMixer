namespace Vam.Engine.Windows.Shell;

/// <summary>What a <see cref="TrayIcon"/> shows and what it does when used.</summary>
/// <remarks>Both members are called on the tray's own thread and must not block it for long.</remarks>
public interface ITrayModel
{
    /// <summary>Reads the current state. Called about once a second and before the menu opens.</summary>
    /// <returns>What to show.</returns>
    TrayView Read();

    /// <summary>Acts on a menu item the latest <see cref="Read"/> still offers, after any confirmation.</summary>
    /// <param name="id">The item's <see cref="TrayMenuItem.Id"/>.</param>
    void Choose(int id);
}
