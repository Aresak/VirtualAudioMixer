using System.Globalization;
using System.Text.Json;

namespace Vam.Server.Tray;

/// <summary>
/// The tray's words, in the language Windows is in.
/// </summary>
/// <remarks>
/// The same rules as the console's localiser: two tables embedded together, and a missing key shown
/// as the key so it is reported rather than silently falling back. The engine's log stays English.
/// </remarks>
public sealed class TrayText
{
    const string Fallback = "en";

    static readonly string[] Languages = [Fallback, "cs"];

    readonly Dictionary<string, string> table;

    /// <summary>Loads the table for a culture, or English when there is none for it.</summary>
    /// <param name="culture">Usually <see cref="CultureInfo.CurrentUICulture"/>.</param>
    public TrayText(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        Culture = culture;
        Language = Languages.Contains(culture.TwoLetterISOLanguageName) ? culture.TwoLetterISOLanguageName : Fallback;
        table = Load(Language);
    }

    /// <summary>Every language the tray ships with.</summary>
    public static IReadOnlyList<string> Available => Languages;

    /// <summary>The language in use.</summary>
    public string Language { get; }

    /// <summary>The culture numbers are formatted in.</summary>
    public CultureInfo Culture { get; }

    /// <summary>Every key in the table in use.</summary>
    public IEnumerable<string> Keys => table.Keys;

    /// <summary>Looks a key up.</summary>
    /// <param name="key">The key.</param>
    /// <returns>Its text, or the key itself when there is none.</returns>
    public string this[string key] => table.TryGetValue(key, out string? text) ? text : key;

    /// <summary>Looks a key up and fills it in.</summary>
    /// <param name="key">The key.</param>
    /// <param name="arguments">What to substitute.</param>
    /// <returns>The formatted text.</returns>
    public string Format(string key, params object?[] arguments) => string.Format(Culture, this[key], arguments);

    static Dictionary<string, string> Load(string code)
    {
        using Stream? stream = typeof(TrayText).Assembly.GetManifestResourceStream($"Vam.Server.Tray.tray.{code}.json");

        return stream is null ? [] : JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
    }
}
