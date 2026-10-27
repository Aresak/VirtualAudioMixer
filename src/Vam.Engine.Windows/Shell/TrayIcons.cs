namespace Vam.Engine.Windows.Shell;

/// <summary>The three icons, each the bytes of an .ico file.</summary>
/// <param name="Running">For <see cref="TrayGlyph.Running"/>.</param>
/// <param name="Recording">For <see cref="TrayGlyph.Recording"/>.</param>
/// <param name="Fault">For <see cref="TrayGlyph.Fault"/>.</param>
public sealed record TrayIcons(byte[] Running, byte[] Recording, byte[] Fault);
