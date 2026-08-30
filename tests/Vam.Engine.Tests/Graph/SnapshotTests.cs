using Vam.Engine.Devices;
using Vam.Engine.Devices.Abstractions;
using Vam.Engine.Graph;
using Vam.TestKit.Allocations;
using Vam.TestKit.Graph;
using Vam.TestKit.Harness;
using Xunit;

namespace Vam.Engine.Tests.Graph;

/// <summary>
/// The snapshot model, which the epic calls the most important piece of engineering in the project
/// after drift compensation.
/// </summary>
/// <remarks>
/// Get it wrong and every later epic inherits either a lock in the audio path or a class of race
/// that turns up once a session. So it gets tested for the two things that would go wrong: a torn
/// read while a swap happens, and an allocation on the thread that must not allocate.
/// </remarks>
public class SnapshotTests
{
    const int Swaps = 2000;

    static readonly AudioDeviceId Microphone = new("null:capture:mayor");

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AParameterChangeSharesThePlanRatherThanRebuildingIt()
    {
        ConsoleFixture console = Build();
        GraphSnapshot before = console.Controller.Publisher.Current;

        console.Controller.Submit(GraphCommand.SetFader(0, -6));
        console.Controller.Pump();

        GraphSnapshot after = console.Controller.Publisher.Current;

        Assert.NotSame(before, after);

        // The expensive things are the arena and the nodes, and a fader move touches neither. This
        // is what lets a dragged fader publish repeatedly without the collector noticing.
        Assert.Same(before.Plan, after.Plan);
        Assert.Same(before.Plan.Arena, after.Plan.Arena);
        Assert.True(after.Version > before.Version);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AWholeFaderDragBecomesOneSnapshot()
    {
        ConsoleFixture console = Build();
        GraphSnapshot before = console.Controller.Publisher.Current;

        // What a dragged fader actually looks like: fifty updates a second, all of them stale
        // except the last.
        for (int step = 0; step < 50; step++)
        {
            console.Controller.Submit(GraphCommand.SetFader(0, -step));
        }

        int applied = console.Controller.Pump();

        Assert.Equal(50, applied);

        // One publish, not fifty. The drain-then-build is the whole reason commands are queued
        // rather than applied where they arrive.
        Assert.Equal(1, console.Controller.Publisher.PendingRetirements);
        Assert.Equal(-49, console.Controller.Config.Channels[0].FaderDb);
        Assert.NotSame(before, console.Controller.Publisher.Current);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void RenderingABlockAllocatesNothing()
    {
        ConsoleFixture console = Build();

        console.Controller.Submit(GraphCommand.SetSend(0, 0, isOn: true, decibels: 0));
        console.Controller.Pump();
        console.Feed(0, 0.5f);
        console.RenderUntilSettled();

        AllocationAssert.None(console, static fixture => fixture.Render());
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void SwappingSnapshotsWhileAudioRunsTearsNothingAndAllocatesNothing()
    {
        ConsoleFixture console = Build();

        console.Controller.Submit(GraphCommand.SetSend(0, 0, isOn: true, decibels: 0));
        console.Controller.Pump();
        console.Feed(0, 0.25f);
        console.RenderUntilSettled();

        using CancellationTokenSource finished = new();

        Thread control = new(() => ChurnUntilCancelled(console.Controller, finished.Token))
        {
            IsBackground = true,
            Name = "graph-control"
        };

        control.Start();

        try
        {
            // The busy test. A snapshot swap landing between two nodes of the same block would show
            // up as a level that is not one of the two the console was ever set to; an allocation
            // would show up here. Neither is allowed while the operator is working the desk.
            AllocationAssert.None(console, static fixture => fixture.Render(), warmup: 32, iterations: 256);

            Assert.InRange(console.OutputPeak(), 0f, 0.26f);
        }
        finally
        {
            finished.Cancel();
            control.Join(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void RetiredSnapshotsAreReleasedOnceTheAudioThreadHasMovedPast()
    {
        ConsoleFixture console = Build();
        SnapshotPublisher publisher = console.Controller.Publisher;

        for (int change = 0; change < 5; change++)
        {
            console.Controller.Submit(GraphCommand.SetFader(0, -change));
            console.Controller.Pump();
        }

        Assert.True(publisher.PendingRetirements > 0);

        // The audio thread takes the newest one, which is what tells the control thread that
        // everything older is safe to let go. Without this the audio thread could end up being the
        // last reference to a multi-megabyte pinned arena and would free it itself.
        console.Render();
        publisher.Collect();

        Assert.Equal(0, publisher.PendingRetirements);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void TheSnapshotTheAudioThreadIsRenderingIsNeverReleased()
    {
        ConsoleFixture console = Build();
        SnapshotPublisher publisher = console.Controller.Publisher;

        console.Render();

        long rendering = publisher.LastSeenVersion;

        console.Controller.Submit(GraphCommand.SetFader(0, -3));
        console.Controller.Pump();
        publisher.Collect();

        // Strictly-less-than, not less-than-or-equal. The snapshot whose version equals what the
        // audio thread last saw is the one it may be inside right now.
        Assert.Equal(rendering, publisher.LastSeenVersion);
        Assert.True(publisher.PendingRetirements > 0);
    }

    static void ChurnUntilCancelled(GraphController controller, CancellationToken cancellationToken)
    {
        int step = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            controller.Submit(GraphCommand.SetFader(0, step % 2 == 0 ? 0 : -3));
            controller.Pump();

            step++;

            if (step > Swaps)
            {
                step = 0;
            }
        }
    }

    static ConsoleFixture Build()
    {
        GraphConfig config = new();

        config.InputDeviceOrder.Add(Microphone);
        config.Channels.Add(new ChannelConfig { DeviceId = Microphone, Name = "Mayor 180 degrees" });
        config.Buses.Add(new BusConfig { Name = "Stream", Role = BusRole.Output, ChannelCount = 2 });

        ConsoleFixture console = new(config);

        console.AddDevice(Microphone, 1);

        return console;
    }
}
