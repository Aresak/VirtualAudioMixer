using Microsoft.Extensions.Logging;
using Vam.Engine.Diagnostics;
using Vam.TestKit.Allocations;
using Vam.TestKit.Harness;
using Vam.TestKit.Logging;
using Xunit;

namespace Vam.Engine.Tests.Diagnostics;

/// <summary>
/// EPIC-12's I2: a list you can read afterwards, not a counter.
/// </summary>
/// <remarks>
/// A number saying a hundred and four dropouts happened tells an operator nothing about whether they
/// were one bad minute or spread across three hours, and those have completely different causes.
/// </remarks>
public class DropoutLogTests
{
    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void RecordingFromTheAudioThreadAllocatesNothing()
    {
        DropoutLog log = new(64);

        // The whole reason the record is a fixed struct with no strings in it. A logging call here
        // allocates, may format a message and may take a lock, at the moment something is already
        // going wrong - which is how one dropout becomes several.
        AllocationAssert.None(
            log,
            static target => target.Record(new DropoutRecord(0, 1, DropoutKind.CaptureUnderrun, 120, 0.5f)));
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void EverythingWrittenComesBackInOrder()
    {
        DropoutLog log = new(64);

        for (int index = 0; index < 10; index++)
        {
            log.Record(index, DropoutKind.CaptureUnderrun, index, index);
        }

        DropoutRecord[] taken = new DropoutRecord[64];
        int count = log.Drain(taken);

        Assert.Equal(10, count);

        for (int index = 0; index < count; index++)
        {
            Assert.Equal(index, taken[index].EndpointIndex);
        }
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AFullRingLosesTheOldestRatherThanBlocking()
    {
        DropoutLog log = new(8);

        for (int index = 0; index < 40; index++)
        {
            log.Record(index, DropoutKind.CaptureUnderrun, 1, index);
        }

        DropoutRecord[] taken = new DropoutRecord[64];
        int count = log.Drain(taken);

        // If the ring filled between two drains then something is going very wrong and the first few
        // records already said what. Waiting to keep them all would be the audio thread blocking on
        // a diagnostic, which is exactly backwards.
        Assert.Equal(log.Capacity, count);
        Assert.Equal(40, log.TotalRecorded);
        Assert.Equal(39, taken[count - 1].EndpointIndex);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ThePumpNamesTheEndpointAndFoldsRepeats()
    {
        DropoutLog log = new(512);
        RecordingLoggerFactory loggers = new();
        DropoutPump pump = new(log, new PumpLogger(loggers));

        pump.SetNames(["Mayor 180 degrees", "Lectern"]);

        for (int index = 0; index < 100; index++)
        {
            log.Record(0, DropoutKind.CaptureUnderrun, 120, 0f);
        }

        log.Record(1, DropoutKind.RenderUnderrun, 60, 0f);

        pump.Pump();
        pump.Flush();

        // A device that has started underrunning does it every block. A hundred identical lines
        // would push the one that mattered off the top of the log.
        Assert.Equal(2, pump.Reported);
        Assert.Equal(99, pump.Folded);

        // Named, because a row of numbers is not something an operator can act on.
        Assert.True(loggers.Mentions("Mayor 180 degrees"), "The log did not name the endpoint.");
        Assert.True(loggers.Mentions("Lectern"));
    }

    /// <summary>Adapts the pooled test logger to the typed interface the pump wants.</summary>
    sealed class PumpLogger(RecordingLoggerFactory factory) : ILogger<DropoutPump>
    {
        readonly ILogger inner = factory.CreateLogger("pump");

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => inner.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => inner.IsEnabled(logLevel);

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            inner.Log(logLevel, eventId, state, exception, formatter);
    }
}
