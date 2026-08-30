using System.Reflection;
using System.Text.Json;

namespace Vam.Ui.Localization;

/// <summary>
/// The console's words, in the language it is being run in. U7.
/// </summary>
/// <remarks>
/// <para>
/// The engine is written in English and stays that way — log lines and protocol reasons are for
/// whoever ends up reading a diagnostic bundle. The console is what an operator uses, and an
/// operator running a Czech council meeting should not have to read the word "Bypassed".
/// </para>
/// <para>
/// A missing key renders as the key. That is deliberate: a key on screen is a visible defect
/// somebody reports, where falling back silently to English produces a half-translated console
/// nobody notices is broken.
/// </para>
/// </remarks>
public sealed class VamLocalizer
{
    static readonly Language[] Languages =
    [
        new("en", "English"),
        new("cs", "Čeština")
    ];

    readonly Dictionary<string, Dictionary<string, string>> tables = [];

    Language current = Languages[0];

    /// <summary>Every language the console ships with.</summary>
    public static IReadOnlyList<Language> Available => Languages;

    /// <summary>Raised when the language changed and everything on screen has to be redrawn.</summary>
    public event Action? Changed;

    /// <summary>The language in use.</summary>
    public Language Current => current;

    /// <summary>Looks a key up.</summary>
    /// <param name="key">The key.</param>
    /// <returns>Its text, or the key itself when there is none.</returns>
    public string this[string key] => Translate(key);

    /// <summary>Switches language.</summary>
    /// <param name="code">A code from <see cref="Available"/>. Anything else is ignored.</param>
    public void Use(string code)
    {
        foreach (Language language in Languages)
        {
            if (!string.Equals(language.Code, code, StringComparison.OrdinalIgnoreCase) || current.Code == language.Code)
            {
                continue;
            }

            current = language;
            Changed?.Invoke();
            return;
        }
    }

    /// <summary>Looks a key up, with the arguments a formatted line needs.</summary>
    /// <param name="key">The key.</param>
    /// <param name="arguments">What to substitute.</param>
    /// <returns>The formatted text.</returns>
    public string Format(string key, params object?[] arguments) =>
        string.Format(System.Globalization.CultureInfo.CurrentCulture, Translate(key), arguments);

    string Translate(string key)
    {
        Dictionary<string, string> table = Table(current.Code);

        return table.TryGetValue(key, out string? text) ? text : key;
    }

    Dictionary<string, string> Table(string code)
    {
        if (tables.TryGetValue(code, out Dictionary<string, string>? cached))
        {
            return cached;
        }

        Dictionary<string, string> loaded = Load(code);

        tables[code] = loaded;
        return loaded;
    }

    static Dictionary<string, string> Load(string code)
    {
        Assembly assembly = typeof(VamLocalizer).Assembly;
        string name = $"Vam.Ui.Resources.strings.{code}.json";

        using Stream? stream = assembly.GetManifestResourceStream(name);

        if (stream is null)
        {
            return [];
        }

        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
    }
}
