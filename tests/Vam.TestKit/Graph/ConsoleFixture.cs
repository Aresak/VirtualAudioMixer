using Vam.Engine.Devices;
using Vam.Engine.Devices.Abstractions;
using Vam.Engine.Graph;

namespace Vam.TestKit.Graph;

/// <summary>
/// A small console wired up, with a way to push audio through it a block at a time.
/// </summary>
/// <remarks>
/// The graph's inputs normally arrive from the master clock as a <c>ref struct</c> over an arena.
/// This builds the same thing from plain arrays so a test can put a known signal in and look at what
/// comes out, without a device anywhere.
/// </remarks>
public sealed class ConsoleFixture
{
    /// <summary>Frames per block, matching the engine's own.</summary>
    public const int BlockFrames = 120;

    /// <summary>The rate the engine runs at.</summary>
    public const int SampleRate = 48000;

    /// <summary>Blocks to render before a smoothed parameter has effectively arrived.</summary>
    /// <remarks>
    /// Three hundred milliseconds. A signal passes through two smoothed stages in series - the
    /// fader and its send - so five time constants each leaves half a percent of error, which is
    /// enough to fail a level assertion. Fifteen leaves nothing measurable.
    ///
    /// The ramp itself is deliberate: every fader climbing from zero at session start is what stops
    /// the first block being a thump.
    /// </remarks>
    public const int BlocksToSettle = 120;

    readonly List<float[]> deviceBuffers = [];
    readonly List<int> deviceWidths = [];

    // Built when a device is added and never again. A ToArray here would allocate on every block,
    // and then the allocation assertion would be measuring this harness rather than the engine -
    // which is exactly the trap docs/audio-path.md warns about.
    BlockSlice[] slices = [];
    float[] arena = [];

    /// <summary>Builds a console over a configuration.</summary>
    /// <param name="config">What to compile.</param>
    public ConsoleFixture(GraphConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        Controller = new GraphController(config, BlockFrames, SampleRate);
    }

    /// <summary>The control side.</summary>
    public GraphController Controller { get; }

    /// <summary>What the primary output received on the last block.</summary>
    public float[] Output { get; private set; } = [];

    /// <summary>
    /// Adds a device whose block the graph will read, in the master clock's pull order.
    /// </summary>
    /// <param name="deviceId">Its identity, for the configuration to point at.</param>
    /// <param name="channelCount">Channels it presents.</param>
    /// <returns>The index the graph will address it by.</returns>
    public int AddDevice(AudioDeviceId deviceId, int channelCount)
    {
        deviceBuffers.Add(new float[BlockFrames * channelCount]);
        deviceWidths.Add(channelCount);

        Rebuild();

        return deviceBuffers.Count - 1;
    }

    /// <summary>Puts a constant value into every sample of one device's next block.</summary>
    /// <param name="deviceIndex">Which device.</param>
    /// <param name="value">The value.</param>
    public void Feed(int deviceIndex, float value) => Array.Fill(deviceBuffers[deviceIndex], value);

    /// <summary>Renders one block.</summary>
    /// <param name="outputChannelCount">Channels the primary output presents.</param>
    public void Render(int outputChannelCount = 2)
    {
        if (Output.Length != BlockFrames * outputChannelCount)
        {
            Output = new float[BlockFrames * outputChannelCount];
        }

        CopyDeviceBuffers();

        MixBlocks blocks = new(arena, slices, BlockFrames);

        Controller.Render(blocks, Output, BlockFrames);
    }

    /// <summary>Renders enough blocks for a smoothed parameter to have arrived.</summary>
    /// <param name="outputChannelCount">Channels the primary output presents.</param>
    public void RenderUntilSettled(int outputChannelCount = 2)
    {
        for (int block = 0; block < BlocksToSettle; block++)
        {
            Render(outputChannelCount);
        }
    }

    /// <summary>The largest absolute sample in the last output block.</summary>
    /// <returns>The peak.</returns>
    public float OutputPeak()
    {
        float peak = 0f;

        foreach (float sample in Output)
        {
            peak = Math.Max(peak, Math.Abs(sample));
        }

        return peak;
    }

    void Rebuild()
    {
        int total = 0;

        slices = new BlockSlice[deviceBuffers.Count];

        for (int index = 0; index < deviceBuffers.Count; index++)
        {
            slices[index] = new BlockSlice(total, deviceWidths[index]);
            total += deviceBuffers[index].Length;
        }

        arena = new float[Math.Max(total, 1)];
    }

    void CopyDeviceBuffers()
    {
        for (int index = 0; index < deviceBuffers.Count; index++)
        {
            deviceBuffers[index].CopyTo(arena, slices[index].Offset);
        }
    }
}
