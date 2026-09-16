using System.Text.Json;

namespace Vam.Engine.Modifiers;

/// <summary>
/// The chain presets an engine knows about, on disk. B0d and B12.
/// </summary>
/// <remarks>
/// <para>
/// <b>Kept by the engine, not by each console.</b> A preset saved at the operator's desk has to be
/// there on the tablet, and a preset that lived in a client would be a preset that vanished when
/// somebody reinstalled a browser.
/// </para>
/// <para>
/// A preset is a whole chain rather than a set of numbers, which is what makes "Jabra shared" and
/// "Studio 180 degrees" genuinely different objects instead of the same object at different
/// settings. That distinction is the point of B0d: the second one has a denoise in it and the first
/// one does not, and no amount of parameter copying gets you from one to the other.
/// </para>
/// <para>
/// Control thread only. Nothing here is on any audio path.
/// </para>
/// </remarks>
public sealed class ChainPresetStore(string path)
{
    static readonly JsonSerializerOptions Format = new() { WriteIndented = true };

    readonly Dictionary<string, ChainPreset> presets = new(StringComparer.OrdinalIgnoreCase);

    bool isLoaded;

    /// <summary>Where the presets live.</summary>
    public string Path => path;

    /// <summary>Every preset, by name.</summary>
    /// <returns>The presets, in the order they were saved.</returns>
    public IReadOnlyCollection<ChainPreset> All()
    {
        Load();

        return presets.Values;
    }

    /// <summary>Finds one.</summary>
    /// <param name="name">Its name.</param>
    /// <returns>The preset, or null.</returns>
    public ChainPreset? Find(string name)
    {
        Load();

        return presets.GetValueOrDefault(name);
    }

    /// <summary>
    /// Saves a chain under a name, replacing any preset already using it.
    /// </summary>
    /// <param name="name">What to call it.</param>
    /// <param name="links">The chain, as it stands.</param>
    public void Save(string name, IReadOnlyList<ModifierSetting> links)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(links);

        Load();

        // Copied rather than referenced. The chain handed in belongs to a live strip, and a preset
        // that moved every time somebody nudged a knob would not be a preset.
        List<ModifierSetting> copy = [];

        foreach (ModifierSetting link in links)
        {
            copy.Add(link.Copy());
        }

        presets[name] = new ChainPreset(name, copy);

        Flush();
    }

    /// <summary>Removes one.</summary>
    /// <param name="name">Its name.</param>
    /// <returns>Whether it was there.</returns>
    public bool Delete(string name)
    {
        Load();

        if (!presets.Remove(name))
        {
            return false;
        }

        Flush();

        return true;
    }

    /// <summary>
    /// Whether a live chain still matches the preset it came from.
    /// </summary>
    /// <remarks>
    /// By what the links are and what they are set to, not by identity: applying a preset mints new
    /// link identities, so comparing those would report every applied preset as modified the instant
    /// it was applied.
    /// </remarks>
    /// <param name="name">The preset's name, or empty for a chain that came from none.</param>
    /// <param name="links">The live chain.</param>
    /// <returns>True when they differ, and false when the name is empty or unknown.</returns>
    public bool IsModified(string name, IReadOnlyList<ModifierSetting> links)
    {
        ArgumentNullException.ThrowIfNull(links);

        if (string.IsNullOrEmpty(name) || Find(name) is not { } preset)
        {
            return false;
        }

        if (preset.Links.Count != links.Count)
        {
            return true;
        }

        for (int index = 0; index < links.Count; index++)
        {
            if (!Matches(preset.Links[index], links[index]))
            {
                return true;
            }
        }

        return false;
    }

    static bool Matches(ModifierSetting saved, ModifierSetting live)
    {
        if (saved.ModifierId != live.ModifierId || saved.IsBypassed != live.IsBypassed)
        {
            return false;
        }

        if (saved.Values.Count != live.Values.Count)
        {
            return false;
        }

        foreach ((string id, float value) in saved.Values)
        {
            if (!live.Values.TryGetValue(id, out float other) || Math.Abs(other - value) > 0.0001f)
            {
                return false;
            }
        }

        return true;
    }

    void Load()
    {
        if (isLoaded)
        {
            return;
        }

        isLoaded = true;

        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            using FileStream stream = File.OpenRead(path);

            foreach (ChainPreset preset in JsonSerializer.Deserialize<List<ChainPreset>>(stream) ?? [])
            {
                presets[preset.Name] = preset;
            }
        }
        catch (Exception failure) when (failure is IOException or JsonException)
        {
            // A preset file that will not parse must not stop a meeting. The presets are gone for
            // this session and the chains an operator already has are untouched, which is a much
            // smaller problem than a console that refuses to open.
            presets.Clear();
        }
    }

    void Flush()
    {
        string? folder = System.IO.Path.GetDirectoryName(path);

        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }

        // Written beside and moved into place, so an interrupted save leaves the previous file
        // rather than half of a new one.
        string temporary = path + ".tmp";

        using (FileStream stream = File.Create(temporary))
        {
            JsonSerializer.Serialize(stream, presets.Values.ToList(), Format);
        }

        File.Move(temporary, path, overwrite: true);
    }
}
