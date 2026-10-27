using System.Globalization;
using Vam.Engine.Windows.Shell;
using Vam.Server.Mediator.Contracts;
using Vam.Server.Tray;
using Vam.TestKit.Harness;
using Xunit;

namespace Vam.Server.Tests.Tray;

/// <summary>#60. What the tray shows, and which mediator request each menu item sends.</summary>
public class TrayPresenterTests
{
    const string Address = "http://localhost:5211";

    static readonly TrayPresenter English = new(new TrayText(CultureInfo.GetCultureInfo("en-GB")));

    static readonly TrayStatus Idle = new() { Address = Address, Dropouts = 3 };

    static readonly TrayStatus Recording = Idle with { RecordingFor = new TimeSpan(1, 2, 3, 4) };

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void TheTooltipNamesTheAddressAndTheDropouts()
    {
        Assert.Equal("VAM engine — running\nListening on http://localhost:5211\nDropouts: 3", English.Tooltip(Idle));
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void WhileRecordingTheTooltipSaysForHowLong()
    {
        // Hours past a day stay hours: a recording is read as one running time, not a date.
        Assert.StartsWith("VAM engine — recording 26:03:04\n", English.Tooltip(Recording), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AFaultedStripIsInTheTooltipAndTheIcon()
    {
        TrayStatus faulted = Recording with { FaultedStrips = 2 };

        TrayView view = English.Present(faulted);

        Assert.Equal(TrayGlyph.Fault, view.Glyph);
        Assert.StartsWith("VAM engine — recording 26:03:04\nMuted, device failed: 2\n", view.Tooltip, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void TheIconSaysWhetherItIsRecording()
    {
        Assert.Equal(TrayGlyph.Running, English.Present(Idle).Glyph);
        Assert.Equal(TrayGlyph.Recording, English.Present(Recording).Glyph);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void TheTooltipFitsWhatWindowsShows()
    {
        TrayStatus worst = new()
        {
            Address = Address,
            Dropouts = 99_999_999,
            FaultedStrips = 16,
            RecordingFor = TimeSpan.FromHours(999)
        };

        foreach (string language in TrayText.Available)
        {
            TrayPresenter presenter = new(new TrayText(CultureInfo.GetCultureInfo(language)));

            Assert.True(presenter.Tooltip(worst).Length <= 127, presenter.Tooltip(worst));
        }
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void DoubleClickOpensTheConsole()
    {
        TrayView view = English.Present(Idle);

        Assert.Equal((int)TrayCommand.OpenConsole, view.DefaultId);
        Assert.Equal("Open console", view.Menu.Single(item => item.Id == view.DefaultId).Text);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void StoppingTheEngineAsksOnlyWhileRecording()
    {
        Assert.Null(StopEngine(English.Present(Idle)).Confirmation);
        Assert.Contains("recording", StopEngine(English.Present(Recording)).Confirmation, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void TheRecordingItemSaysWhatItWillDo()
    {
        Assert.Equal("Start recording", Toggle(English.Present(Idle)).Text);
        Assert.Equal("Stop recording", Toggle(English.Present(Recording)).Text);

        // U8, as the console's own stop button does.
        Assert.NotNull(Toggle(English.Present(Recording)).Confirmation);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void TheRecordingItemSendsTheConsolesRequest()
    {
        // Fixed by the item rather than worked out from the state when it is chosen, which could
        // have changed while the menu was open.
        SetRecordingRequest start = Assert.IsType<SetRecordingRequest>(TrayPresenter.RequestFor(TrayCommand.StartRecording));
        SetRecordingRequest stop = Assert.IsType<SetRecordingRequest>(TrayPresenter.RequestFor(TrayCommand.StopRecording));

        // An empty directory is the configured root, exactly what the console sends.
        Assert.Equal(new SetRecordingRequest(true, string.Empty), start);
        Assert.Equal(new SetRecordingRequest(false, string.Empty), stop);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void StopEngineSendsTheShutdownRequest()
    {
        Assert.IsType<ShutdownRequest>(TrayPresenter.RequestFor(TrayCommand.StopEngine));
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void OpeningTheConsoleIsNotAnEngineCommand()
    {
        Assert.Null(TrayPresenter.RequestFor(TrayCommand.OpenConsole));
        Assert.Null(TrayPresenter.RequestFor(TrayCommand.None));
    }

    static TrayMenuItem StopEngine(TrayView view) => view.Menu.Single(item => item.Id == (int)TrayCommand.StopEngine);

    static TrayMenuItem Toggle(TrayView view) =>
        view.Menu.Single(item => item.Id is (int)TrayCommand.StartRecording or (int)TrayCommand.StopRecording);
}
