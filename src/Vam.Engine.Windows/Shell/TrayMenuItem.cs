namespace Vam.Engine.Windows.Shell;

/// <summary>One line of the tray's menu.</summary>
/// <param name="Id">What <see cref="ITrayModel.Choose"/> is told. Zero draws a separator.</param>
/// <param name="Text">What it says.</param>
/// <param name="Confirmation">A question to answer yes to first, or null to act at once.</param>
public sealed record TrayMenuItem(int Id, string Text, string? Confirmation = null)
{
    /// <summary>A line between groups of items.</summary>
    public static TrayMenuItem Separator { get; } = new(0, string.Empty);
}
