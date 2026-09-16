using Vam.Engine.Modifiers;
using Vam.TestKit.Harness;
using Xunit;

namespace Vam.Engine.Tests.Modifiers;

/// <summary>
/// B0d and B12. A preset is a whole chain, not a set of numbers.
/// </summary>
public class ChainPresetStoreTests : IDisposable
{
    readonly string path = Path.Combine(Path.GetTempPath(), $"vam-presets-{Guid.NewGuid():n}.json");

    public void Dispose()
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void APresetSurvivesTheEngineThatSavedIt()
    {
        new ChainPresetStore(path).Save("Jabra shared", Chain());

        // A second store over the same file, as a restarted engine would be. Presets are kept by the
        // engine so that one saved at the desk is there on the tablet, which is only true if they
        // outlive the process.
        ChainPreset preset = Assert.Single(new ChainPresetStore(path).All());

        Assert.Equal("Jabra shared", preset.Name);
        Assert.Equal(2, preset.Links.Count);
        Assert.Equal("vam.gate", preset.Links[1].ModifierId);
        Assert.Equal(-42f, preset.Links[1].Values["threshold"]);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void SavingCopiesTheChainRatherThanKeepingIt()
    {
        ChainPresetStore store = new(path);
        List<ModifierSetting> live = Chain();

        store.Save("Studio", live);

        // The chain handed in belongs to a live strip. A preset that followed every knob somebody
        // turned afterwards would not be a preset.
        live[1].Values["threshold"] = -6f;
        live.RemoveAt(0);

        ChainPreset preset = store.Find("Studio")!;

        Assert.Equal(2, preset.Links.Count);
        Assert.Equal(-42f, preset.Links[1].Values["threshold"]);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AnAppliedPresetDoesNotImmediatelyReadAsModified()
    {
        ChainPresetStore store = new(path);

        store.Save("Studio", Chain());

        List<ModifierSetting> applied = store.Find("Studio")!.ToChain();

        // ToChain mints fresh link identities, so a comparison by identity would report every
        // applied preset as modified the instant it was applied. It compares what the links are and
        // what they are set to.
        Assert.False(store.IsModified("Studio", applied));
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ChangingAValueOrTheShapeReadsAsModified()
    {
        ChainPresetStore store = new(path);

        store.Save("Studio", Chain());

        List<ModifierSetting> tweaked = store.Find("Studio")!.ToChain();
        tweaked[1].Values["threshold"] = -30f;

        Assert.True(store.IsModified("Studio", tweaked));

        List<ModifierSetting> shorter = store.Find("Studio")!.ToChain();
        shorter.RemoveAt(0);

        Assert.True(store.IsModified("Studio", shorter));

        List<ModifierSetting> bypassed = store.Find("Studio")!.ToChain();
        bypassed[0] = bypassed[0] with { IsBypassed = true };

        Assert.True(store.IsModified("Studio", bypassed));
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AChainThatCameFromNoPresetIsNeverModified()
    {
        ChainPresetStore store = new(path);

        // Nothing to have drifted from. A console showing a modified marker on a chain nobody saved
        // would be telling an operator to save work they never started.
        Assert.False(store.IsModified(string.Empty, Chain()));
        Assert.False(store.IsModified("never saved", Chain()));
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ADamagedFileCostsThePresetsAndNotTheSession()
    {
        File.WriteAllText(path, "{ this is not a preset library");

        ChainPresetStore store = new(path);

        // A meeting has to start. Losing the preset library is a much smaller problem than a console
        // that refuses to open, and the chains an operator already has are untouched either way.
        Assert.Empty(store.All());
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void DeletingSaysWhetherItWasThere()
    {
        ChainPresetStore store = new(path);

        store.Save("Studio", Chain());

        Assert.True(store.Delete("Studio"));
        Assert.False(store.Delete("Studio"));
        Assert.Empty(store.All());
    }

    static List<ModifierSetting> Chain() =>
    [
        new() { ModifierId = "vam.highpass", Values = { ["frequency"] = 90f } },
        new() { ModifierId = "vam.gate", Values = { ["threshold"] = -42f } }
    ];
}
