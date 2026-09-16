using Vam.TestKit.Harness;
using Vam.Ui.Localization;
using Xunit;

namespace Vam.Ui.Tests.Localization;

/// <summary>
/// U7. Two tables that have to stay in step, and a build that has to keep embedding them.
/// </summary>
public class VamLocalizerTests
{
    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void EveryLanguageActuallyShips()
    {
        // This is the test that would have caught it: MSBuild reads ".en." and ".cs." as culture
        // codes and will happily turn the tables into satellite assemblies, which leaves every
        // string on the console rendering as its own key and nothing failing to build.
        foreach (Language language in VamLocalizer.Available)
        {
            VamLocalizer localizer = new();

            localizer.Use(language.Code);

            Assert.NotEqual("view.mixer", localizer["view.mixer"]);
        }
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void TheTablesHaveTheSameKeys()
    {
        VamLocalizer english = new();
        VamLocalizer other = new();

        foreach (Language language in VamLocalizer.Available)
        {
            other.Use(language.Code);

            foreach (string key in Keys)
            {
                // A key present in one table and missing from another is a console that is
                // half-translated, and the person who notices is an operator mid-meeting.
                Assert.NotEqual(key, other[key]);
                Assert.NotEqual(key, english[key]);
            }
        }
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AMissingKeyRendersAsItself()
    {
        VamLocalizer localizer = new();

        // Deliberate. Falling back to English would produce a console nobody notices is broken;
        // a key on screen is a defect somebody reports.
        Assert.Equal("nothing.here", localizer["nothing.here"]);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void SwitchingLanguageTellsTheConsoleToRedraw()
    {
        VamLocalizer localizer = new();
        int changes = 0;

        localizer.Changed += () => changes++;

        localizer.Use("cs");
        localizer.Use("cs");
        localizer.Use("nonsense");

        // Once, for the one that changed anything. A console that redrew for every call would redraw
        // for a language it is already in.
        Assert.Equal(1, changes);
        Assert.Equal("cs", localizer.Current.Code);
    }

    static IEnumerable<string> Keys =>
    [
        "app.name",
        "view.mixer",
        "view.diagnostics",
        "status.connected",
        "mixer.matrix",
        "send.locked",
        "send.lockedWhy",
        "bus.role.monitor",
        "automix.depth",
        "recording.start",
        "diag.audioThread",
        "settings.language",
        "common.confirm"
    ];
}
