namespace Vam.Ui.State;

/// <summary>
/// The colours a strip gets when nobody has chosen one. U5.
/// </summary>
/// <remarks>
/// <para>
/// A console where every strip is the same grey is a console an operator navigates by reading, and
/// reading is slower than looking. The fallback is derived from the index so it is stable: the strip
/// that was blue this morning is blue this afternoon, on every console watching.
/// </para>
/// <para>
/// The colours are the mockup's accents, which were picked to sit apart from the signal colours.
/// Nothing here may be mistaken for a meter at a glance, which rules out the greens, ambers and reds.
/// </para>
/// </remarks>
public static class StripPalette
{
    static readonly string[] Colours =
    [
        "#6ea8ff",
        "#39b9ae",
        "#9b8cd9",
        "#d6a64a",
        "#5f9ea0",
        "#c07a9c",
        "#7fa66b",
        "#b3865c"
    ];

    /// <summary>Every colour the picker offers.</summary>
    public static IReadOnlyList<string> All => Colours;

    /// <summary>The colour a strip falls back to.</summary>
    /// <param name="index">Which strip.</param>
    /// <returns>A hex colour.</returns>
    public static string For(int index) => Colours[Math.Abs(index) % Colours.Length];
}
