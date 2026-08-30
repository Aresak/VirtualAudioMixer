using Vam.Engine.Diagnostics;
using Vam.Engine.Graph;
using Vam.Engine.Graph.Nodes;
using Vam.Engine.Modifiers;
using Vam.Protocol.V1;
using Vam.Server.Engine;
using Vam.Server.Logging;

namespace Vam.Server.Services;

/// <summary>
/// K1 to K7, gathered into one reply.
/// </summary>
/// <remarks>
/// <para>
/// Separate from <see cref="MixerService"/> because it is a different job: the service is a control
/// surface and this is a report. It is also the part most likely to grow, and growth here should not
/// push the commands off the bottom of the file.
/// </para>
/// <para>
/// <b>Built only when somebody asks.</b> The diagnostics view polls this while it is open and
/// nothing polls it otherwise. An operator running a meeting is not paying for a drift chart nobody
/// is looking at.
/// </para>
/// </remarks>
public static class MixerDiagnostics
{
    /// <summary>How many drift samples one reply carries.</summary>
    /// <remarks>
    /// Twenty minutes at four samples a second. Long enough to see a trend, short enough that the
    /// reply does not become the reason the console feels slow.
    /// </remarks>
    public const int DriftSamples = 4800;

    /// <summary>How many log lines one reply carries.</summary>
    public const int LogLines = 400;

    /// <summary>How many dropouts one reply carries.</summary>
    public const int Dropouts = 512;

    /// <summary>Builds the report.</summary>
    /// <param name="engine">The engine to report on.</param>
    /// <returns>Everything the diagnostics view draws.</returns>
    public static DiagnosticsState Build(VamEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);

        DiagnosticsState state = new()
        {
            Clock = BuildClock(engine),
            Callbacks = BuildCallbacks(engine),
            Allocations = BuildAllocations(engine)
        };

        AddDrift(state, engine);
        AddDropouts(state, engine);
        AddCosts(state, engine);
        AddLog(state);

        return state;
    }

    static ClockDiagnostics BuildClock(VamEngine engine)
    {
        ClockDiagnostics clock = new()
        {
            SourceName = engine.Clock?.PrimaryDeviceId.Value ?? string.Empty,
            NominalRate = 48000,
            BlockFrames = 120,
            IsTimerFallback = engine.Clock is null || engine.Clock.PrimaryDeviceId.IsNone
        };

        // The measured rate comes from whichever device is currently the clock, not from an average.
        // An average across devices would hide the one that is drifting, which is the only reason
        // anybody opens this panel.
        if (engine.Channels.Count > 0)
        {
            clock.MeasuredRate = engine.Channels.Channels[0].GetTelemetry().MeasuredSampleRate;
        }

        return clock;
    }

    static CallbackDiagnostics BuildCallbacks(VamEngine engine)
    {
        CallbackHistogram histogram = engine.Callbacks;
        long[] buckets = new long[histogram.BucketCount];

        histogram.CopyTo(buckets);

        CallbackDiagnostics callbacks = new()
        {
            BucketWidthFraction = histogram.BucketWidthFraction,
            WorstFraction = histogram.WorstFraction,
            Overruns = histogram.Overruns
        };

        callbacks.Buckets.AddRange(buckets);

        return callbacks;
    }

    static AllocationDiagnostics BuildAllocations(VamEngine engine) => new()
    {
        // The row that has to read zero. Rule 1 is asserted by a test on a quiet machine; this is
        // the same assertion made by a real session, and if it is ever not zero, that is the bug.
        AudioThreadBytes = engine.Allocations.TotalBytes,
        Gen0Collections = GC.CollectionCount(0),
        Gen1Collections = GC.CollectionCount(1),
        Gen2Collections = GC.CollectionCount(2),
        TotalManagedBytes = GC.GetTotalMemory(forceFullCollection: false),
        LongestPauseMs = GC.GetTotalPauseDuration().TotalMilliseconds
    };

    static void AddDrift(DiagnosticsState state, VamEngine engine)
    {
        DriftSample[] samples = new DriftSample[Math.Min(engine.Drift.Count, DriftSamples)];
        int written = engine.Drift.CopyTo(samples);

        for (int index = 0; index < written; index++)
        {
            DriftSample sample = samples[index];

            state.Drift.Add(new DriftPoint
            {
                ChannelIndex = sample.ChannelIndex,
                TimestampTicks = sample.Timestamp.UtcTicks,
                DriftPpm = sample.DriftPpm,
                FillFrames = (int)Math.Round(sample.FillPercentage),
                CorrectionPpm = sample.CorrectionPpm
            });
        }
    }

    static void AddDropouts(DiagnosticsState state, VamEngine engine)
    {
        DropoutRecord[] records = new DropoutRecord[Dropouts];
        int written = engine.Dropouts.Peek(records);

        for (int index = 0; index < written; index++)
        {
            DropoutRecord record = records[index];
            string endpoint = record.EndpointIndex < engine.Graph?.Config.Channels.Count
                ? engine.Graph.Config.Channels[record.EndpointIndex].Name
                : $"endpoint {record.EndpointIndex}";

            state.Dropouts.Add(new DropoutEntry
            {
                TimestampTicks = record.TimestampTicks,
                Endpoint = endpoint,
                Kind = record.Kind.ToString(),
                Frames = record.Frames
            });
        }
    }

    static void AddCosts(DiagnosticsState state, VamEngine engine)
    {
        if (engine.Graph is not { } graph)
        {
            return;
        }

        GraphSnapshot snapshot = graph.Publisher.Current;
        long blockTicks = graph.BlockTicks;

        foreach (AudioNode node in snapshot.Plan.Nodes)
        {
            if (node is not ChainNode chainNode)
            {
                continue;
            }

            ModifierChain chain = chainNode.Chain;
            ChainParams parameters = snapshot.ChainOf(chainNode.ChannelIndex);

            for (int link = 0; link < chain.Count; link++)
            {
                ModifierCost cost = chain.Costs[link];

                state.Costs.Add(new ModifierCostState
                {
                    ChannelIndex = chainNode.ChannelIndex,
                    LinkIndex = link,
                    ModifierId = chain.Modifiers[link].Descriptor.Id,
                    AverageFraction = cost.FractionOfBudget(blockTicks),
                    WorstFraction = blockTicks > 0 ? cost.PeakTicks / (double)blockTicks : 0,
                    Overruns = engine.OverrunsOf(chainNode.ChannelIndex, link),

                    // Bypassed and switched out look the same to the audio thread and are very
                    // different to an operator, which is why the count above is next to it. A
                    // modifier the engine turned off for overrunning has a number beside it; one
                    // somebody bypassed on purpose has a zero.
                    IsSwitchedOut = parameters.IsBypassed(link)
                });
            }
        }
    }

    static void AddLog(DiagnosticsState state)
    {
        IReadOnlyList<LogTailEntry> tail = LogTailTarget.Snapshot();
        int from = Math.Max(tail.Count - LogLines, 0);

        for (int index = from; index < tail.Count; index++)
        {
            LogTailEntry entry = tail[index];

            state.Log.Add(new LogLine
            {
                TimestampTicks = entry.Timestamp.Ticks,
                Level = entry.Level,
                Logger = entry.Source,
                Message = entry.Message
            });
        }
    }
}
