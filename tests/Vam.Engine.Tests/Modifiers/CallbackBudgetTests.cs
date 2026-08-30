using System.Diagnostics;
using Vam.Engine.Devices.Abstractions;
using Vam.Engine.Graph;
using Vam.Engine.Modifiers;
using Vam.TestKit.Graph;
using Vam.TestKit.Harness;
using Xunit;

namespace Vam.Engine.Tests.Modifiers;

/// <summary>
/// EPIC-05's gate: the full chain on five channels, inside the callback budget, with headroom.
/// </summary>
/// <remarks>
/// <para>
/// A block is 2.5 milliseconds at 120 frames and 48 kHz. Every one of them has to be rendered in
/// less than that, and comfortably less — a graph that fits exactly has no room for the machine to
/// be doing anything else, and the machine is also encoding a video stream.
/// </para>
/// <para>
/// <b>It reports rather than only asserting.</b> The number is the point: an epic that says "with
/// headroom" needs somebody to be able to read how much, and a pass or fail alone hides the day it
/// went from twelve per cent to sixty.
/// </para>
/// </remarks>
public class CallbackBudgetTests
{
    const int Channels = 5;
    const int BlockFrames = 120;
    const int SampleRate = 48000;

    /// <summary>How much of a block the whole graph may take.</summary>
    /// <remarks>
    /// A quarter, which is the same budget the cost guard holds a single modifier to. It is a
    /// deliberately hard target on a development machine so that a slower one still has room.
    /// </remarks>
    const double BudgetFraction = 0.25;

    /// <summary>
    /// What this build is actually held to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A debug build is not the product. The DSP kernels here are tight loops over spans, which is
    /// exactly the code the optimiser earns its keep on — measuring them unoptimised and calling the
    /// result a budget would be asserting something about a binary nobody runs.
    /// </para>
    /// <para>
    /// So the real budget is asserted in release and a debug build is only required to fit inside a
    /// block at all. The number is printed either way, which is the part worth having: a build that
    /// slides from a third of a block to two thirds is visible in the output long before it fails.
    /// </para>
    /// </remarks>
#if DEBUG
    const double Limit = 1.0;
#else
    const double Limit = BudgetFraction;
#endif

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void FiveChannelsWithAFullChainFitInsideTheCallbackWithHeadroom()
    {
        ConsoleFixture console = Build();

        for (int channel = 0; channel < Channels; channel++)
        {
            console.Feed(channel, 0.3f);
        }

        // Warmed first. The first blocks include the just-in-time compilation of everything the
        // chain touches, and measuring those would be measuring the runtime rather than the graph.
        for (int block = 0; block < 500; block++)
        {
            console.Render();
        }

        double blockSeconds = (double)BlockFrames / SampleRate;
        long ticks = Stopwatch.GetTimestamp();
        const int Blocks = 4000;

        for (int block = 0; block < Blocks; block++)
        {
            console.Render();
        }

        double elapsed = (Stopwatch.GetTimestamp() - ticks) / (double)Stopwatch.Frequency;
        double perBlock = elapsed / Blocks;
        double fraction = perBlock / blockSeconds;

        TestContext.Current.TestOutputHelper?.WriteLine(
            $"{Channels} channels, full chain: {perBlock * 1_000_000:0} us per block, "
            + $"{fraction:P1} of a {blockSeconds * 1000:0.0} ms budget.");

        // Deliberately reported through a failure, so the number is in the test output whether or
        // not it passed. A gate that only says pass or fail hides the day it went from twelve per
        // cent to sixty, which is the day somebody needed to know.
        Assert.True(
            fraction < Limit,
            $"The graph used {fraction:P1} of the callback budget at {perBlock * 1_000_000:0} us per block, "
            + $"and the limit for this build is {Limit:P0}.");
    }

    static ConsoleFixture Build()
    {
        GraphConfig config = new() { IsAutomixBypassed = false };

        for (int channel = 0; channel < Channels; channel++)
        {
            AudioDeviceId device = new($"null:capture:{channel}");

            config.InputDeviceOrder.Add(device);
            config.Channels.Add(new ChannelConfig
            {
                DeviceId = device,
                Name = $"Microphone {channel + 1}",
                ParticipatesInAutomix = true,

                // The chain EPIC-05 argues for, in the order it argues for, on every strip. This is
                // the expensive case: nobody runs a council room with a bare signal path.
                Chain =
                {
                    new ModifierSetting { ModifierId = "vam.highpass" },
                    new ModifierSetting { ModifierId = "vam.gate" },
                    new ModifierSetting { ModifierId = "vam.denoise" },
                    new ModifierSetting { ModifierId = "vam.equaliser" },
                    new ModifierSetting { ModifierId = "vam.adaptivegain" },
                    new ModifierSetting { ModifierId = "vam.compressor" }
                }
            });
        }

        // A stream bus and a monitor, which is the smallest arrangement a real session has - and the
        // stream bus brings its mandatory limiter with it.
        config.Buses.Add(new BusConfig { Name = "Stream", Role = BusRole.Stream, ChannelCount = 2 });
        config.Buses.Add(new BusConfig { Name = "Monitor", Role = BusRole.Monitor, ChannelCount = 2 });

        for (int channel = 0; channel < Channels; channel++)
        {
            config.Sends.Add(new SendConfig(channel, 0, IsOn: true, LevelDb: 0));
            config.Sends.Add(new SendConfig(channel, 1, IsOn: true, LevelDb: 0));
        }

        ConsoleFixture console = new(config, ModifierRegistry.CreateDefault());

        for (int channel = 0; channel < Channels; channel++)
        {
            console.AddDevice(new AudioDeviceId($"null:capture:{channel}"), 1);
        }

        return console;
    }
}
