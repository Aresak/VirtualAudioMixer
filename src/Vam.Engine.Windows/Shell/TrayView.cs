namespace Vam.Engine.Windows.Shell;

/// <summary>Everything the tray shows at one moment.</summary>
/// <param name="Glyph">The icon.</param>
/// <param name="Tooltip">The hover text. Windows cuts it at 127 characters.</param>
/// <param name="Caption">The title of any question the tray asks.</param>
/// <param name="Menu">The menu, top to bottom.</param>
/// <param name="DefaultId">The item a double-click chooses, shown in bold.</param>
public sealed record TrayView(
    TrayGlyph Glyph,
    string Tooltip,
    string Caption,
    IReadOnlyList<TrayMenuItem> Menu,
    int DefaultId);
