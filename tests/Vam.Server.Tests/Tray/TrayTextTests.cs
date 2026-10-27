using System.Globalization;
using Vam.Server.Tray;
using Vam.TestKit.Harness;
using Xunit;

namespace Vam.Server.Tests.Tray;

/// <summary>#60. The tray's two tables, embedded and in step.</summary>
public class TrayTextTests
{
    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void EveryLanguageActuallyShips()
    {
        foreach (string language in TrayText.Available)
        {
            TrayText text = new(CultureInfo.GetCultureInfo(language));

            Assert.Equal(language, text.Language);
            Assert.NotEqual("tray.openConsole", text["tray.openConsole"]);
        }
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void TheTablesHaveTheSameKeys()
    {
        TrayText english = new(CultureInfo.GetCultureInfo("en"));
        TrayText czech = new(CultureInfo.GetCultureInfo("cs-CZ"));

        Assert.Equal(english.Keys.Order(), czech.Keys.Order());
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void WindowsInCzechGetsCzech()
    {
        Assert.Equal("Otevřít konzoli", new TrayText(CultureInfo.GetCultureInfo("cs-CZ"))["tray.openConsole"]);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AnyOtherLanguageGetsEnglish()
    {
        Assert.Equal("Open console", new TrayText(CultureInfo.GetCultureInfo("de-DE"))["tray.openConsole"]);
        Assert.Equal("Open console", new TrayText(CultureInfo.InvariantCulture)["tray.openConsole"]);
    }

    [Theory]
    [Trait("Category", TestCategories.Unit)]
    [InlineData("running")]
    [InlineData("recording")]
    [InlineData("fault")]
    public void TheArtworkShipsToo(string state)
    {
        Assert.Contains($"Vam.Server.Tray.engine-{state}.ico", typeof(EngineTray).Assembly.GetManifestResourceNames());
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AMissingKeyRendersAsItself()
    {
        Assert.Equal("nothing.here", new TrayText(CultureInfo.GetCultureInfo("en"))["nothing.here"]);
    }
}
