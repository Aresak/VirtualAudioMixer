using Vam.Engine.Devices.Abstractions;
using Vam.Engine.Graph;
using Vam.Engine.Graph.Nodes;
using Vam.Engine.Modifiers;
using Vam.Engine.Modifiers.BuiltIn;
using Vam.TestKit.Allocations;
using Vam.TestKit.Graph;
using Vam.TestKit.Harness;
using Vam.Modifiers.Abstractions;
using Xunit;

namespace Vam.Engine.Tests.Modifiers;

/// <summary>
/// EPIC-04. A chain the operator composes, with the audio thread still allocating nothing and no
/// glitch when the chain changes under it.
/// </summary>
public class ModifierChainTests
{
    static readonly AudioDeviceId Microphone = new("null:capture:mayor");

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AModifierInTheChainChangesTheAudio()
    {
        ConsoleFixture console = Build(Gain("first", -6.0206f));

        console.Controller.Submit(GraphCommand.SetSend(0, 0, isOn: true, decibels: 0));
        console.Controller.Pump();

        console.Feed(0, 0.5f);
        console.RenderUntilSettled();

        // Six decibels down is half. The whole framework, proved by the smallest thing that uses
        // all of it: a parameter, its smoothing, its clamping and its telemetry.
        Assert.Equal(0.25f, console.OutputPeak(), 0.005f);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ABypassedLinkDoesNothing()
    {
        ModifierSetting link = Gain("first", -6.0206f) with { IsBypassed = true };
        ConsoleFixture console = Build(link);

        console.Controller.Submit(GraphCommand.SetSend(0, 0, isOn: true, decibels: 0));
        console.Controller.Pump();

        console.Feed(0, 0.5f);
        console.RenderUntilSettled();

        Assert.Equal(0.5f, console.OutputPeak(), 0.005f);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ReorderingAChainKeepsTheInstancesItAlreadyHad()
    {
        ModifierSetting first = Gain("first", 0f);
        ModifierSetting second = Gain("second", 0f);

        ConsoleFixture console = Build(first, second);

        Modifier[] before = InstancesOf(console);

        console.Controller.Config.Channels[0].Chain.Clear();
        console.Controller.Config.Channels[0].Chain.Add(second);
        console.Controller.Config.Channels[0].Chain.Add(first);
        console.Controller.Recompile();

        Modifier[] after = InstancesOf(console);

        // Same objects, new order. Minting fresh ones would restart every filter history and
        // envelope in the chain, and a denoise restarting mid-sentence is audible - which is
        // exactly what a reorder must not be.
        Assert.Same(before[0], after[1]);
        Assert.Same(before[1], after[0]);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void RunningTheChainAllocatesNothing()
    {
        ConsoleFixture console = Build(Gain("first", -3f), Gain("second", -3f));

        console.Controller.Submit(GraphCommand.SetSend(0, 0, isOn: true, decibels: 0));
        console.Controller.Pump();
        console.Feed(0, 0.4f);
        console.RenderUntilSettled();

        AllocationAssert.None(console, static fixture => fixture.Render());
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AParameterChangeArrivesAsASwellRatherThanAStep()
    {
        ConsoleFixture console = Build(Gain("first", 0f));

        console.Controller.Submit(GraphCommand.SetSend(0, 0, isOn: true, decibels: 0));
        console.Controller.Pump();
        console.Feed(0, 0.5f);
        console.RenderUntilSettled();

        float before = console.OutputPeak();

        // The largest jump this parameter can make, asked for in one go.
        console.Controller.Config.Channels[0].Chain[0].Values["level"] = -60f;
        console.Controller.Submit(GraphCommand.SetFader(0, 0));
        console.Controller.Pump();
        console.Render();

        float afterOneBlock = console.OutputPeak();

        // One block later it has moved, and nowhere near all the way. A step here would be a click,
        // and the host smoothing the parameter is what stops every modifier having to.
        Assert.True(afterOneBlock < before, "The parameter did not move at all.");
        Assert.True(
            afterOneBlock > before * 0.5f,
            $"The parameter jumped from {before:F4} to {afterOneBlock:F4} in one block, which is a step.");
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AChainThatDoesNotFitIsRefusedNamingTheLink()
    {
        // A modifier that insists on one channel, handed a stereo strip.
        IReadOnlyList<ChainProblem> problems = ModifierChain.Validate([new MonoOnlyModifier()], channelCount: 2);

        ChainProblem problem = Assert.Single(problems);

        Assert.Equal(ChainProblemKind.ChannelCountMismatch, problem.Kind);

        // Naming it is the point. "Channel count mismatch" tells an operator nothing they can act on.
        Assert.Contains("Mono only", problem.Description, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AModifierThatOverrunsItsBudgetIsSwitchedOutAndReported()
    {
        ConsoleFixture console = Build(Gain("slow", 0f));

        List<ModifierOverrun> overruns = [];
        console.Controller.Overran += (_, overrun) => overruns.Add(overrun);

        console.Controller.Submit(GraphCommand.SetSend(0, 0, isOn: true, decibels: 0));
        console.Controller.Pump();
        console.Feed(0, 0.4f);

        // Real measurements, taken on the audio thread by rendering. The budget is then set absurdly
        // low so that any real cost at all exceeds it - which proves the measure-then-decide path
        // without shipping a modifier that is deliberately slow, and without a spin loop whose
        // duration would depend on the machine.
        for (int block = 0; block < 128; block++)
        {
            console.Render();
        }

        Assert.Equal(1, console.Controller.GuardCostBudget(budgetFraction: 1e-9));

        ModifierOverrun overrun = Assert.Single(overruns);

        Assert.Equal("Gain", overrun.ModifierName);
        Assert.True(console.Controller.Publisher.Current.ChainOf(0).IsBypassed(0));
    }

    static ChainNode NodeOf(ConsoleFixture console)
    {
        foreach (AudioNode node in console.Controller.Publisher.Current.Plan.Nodes)
        {
            if (node is ChainNode chain)
            {
                return chain;
            }
        }

        throw new InvalidOperationException("The console has no chain.");
    }

    static Modifier[] InstancesOf(ConsoleFixture console)
    {
        ChainNode node = NodeOf(console);

        return [.. node.Chain.Modifiers.ToArray()];
    }

    static ModifierSetting Gain(string linkId, float decibels) =>
        new()
        {
            ModifierId = "vam.gain",
            LinkId = linkId,
            Values = new Dictionary<string, float>(StringComparer.Ordinal) { ["level"] = decibels }
        };

    static ConsoleFixture Build(params ModifierSetting[] chain)
    {
        GraphConfig config = new();

        config.InputDeviceOrder.Add(Microphone);
        config.Channels.Add(new ChannelConfig
        {
            DeviceId = Microphone,
            Name = "Mayor 180 degrees",
            Chain = [.. chain]
        });

        config.Buses.Add(new BusConfig { Name = "Stream", Role = BusRole.Output, ChannelCount = 2 });

        ConsoleFixture console = new(config, ModifierRegistry.CreateDefault());

        console.AddDevice(Microphone, 1);

        return console;
    }

    /// <summary>A modifier that will only take one channel, for the validation test.</summary>
    sealed class MonoOnlyModifier : Modifier
    {
        public override ModifierDescriptor Descriptor { get; } =
            new("test.monoonly", "Mono only", ChannelsIn: 1, ChannelsOut: 1, LatencySamples: 0, CanProcessInPlace: true);

        public override ReadOnlySpan<ParameterDescriptor> Parameters => default;

        public override void Prepare(int sampleRate, int maxFrames, int channelCount)
        {
        }

        public override void Process(ref ModifierContext context)
        {
        }
    }
}
