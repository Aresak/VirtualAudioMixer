using Vam.Protocol.V1;
using Vam.TestKit.Harness;
using Vam.Ui.Extensions;
using Vam.Ui.State;
using Xunit;

namespace Vam.Ui.Tests.Extensions;

/// <summary>
/// K7's filter, and the count of lines that arrived while the reader was scrolled away.
/// </summary>
public class LogLineExtensionsTests
{
    static readonly LogLine Started = Line(1, "Info", "Engine started");
    static readonly LogLine Drift = Line(2, "Warn", "UCA222 drift -67 ppm");
    static readonly LogLine Widened = Line(3, "Info", "Resampler ratio widened");
    static readonly LogLine Lost = Line(4, "Error", "Jabra disappeared");
    static readonly LogLine Fatal = Line(5, "Fatal", "Engine stopped");

    static readonly IReadOnlyList<LogLine> Log = [Started, Drift, Widened, Lost, Fatal];

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void TypingWarnKeepsWarnLinesAndDropsInfoLines()
    {
        // The defect: the text box matched only the message, so "Warn" found nothing.
        Assert.Equal([Drift], Log.Filter(LogLevelFilter.All, "Warn"));
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void TheWarningChipKeepsWarnLinesAndDropsInfoLines()
    {
        Assert.Equal([Drift], Log.Filter(LogLevelFilter.Warning, string.Empty));
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void TheErrorChipIncludesFatal()
    {
        Assert.Equal([Lost, Fatal], Log.Filter(LogLevelFilter.Error, string.Empty));
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ChipAndTextBothApply()
    {
        Assert.Equal([Widened], Log.Filter(LogLevelFilter.Info, "ratio"));
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void NoFilterIsTheWholeLog()
    {
        Assert.Same(Log, Log.Filter(LogLevelFilter.All, string.Empty));
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void CountsLinesAfterTheLastOneShown()
    {
        // The tail is full: the oldest lines left as new ones arrived.
        IReadOnlyList<LogLine> shown = [Started, Drift, Widened];
        IReadOnlyList<LogLine> current = [Widened, Lost, Fatal];

        Assert.Equal(2, current.CountNewerThan(shown));
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void CountsAllOnceTheLastShownLineHasLeftTheTail()
    {
        IReadOnlyList<LogLine> shown = [Started];
        IReadOnlyList<LogLine> current = [Drift, Widened];

        Assert.Equal(2, current.CountNewerThan(shown));
    }

    static LogLine Line(long ticks, string level, string message) =>
        new() { TimestampTicks = ticks, Level = level, Message = message };
}
