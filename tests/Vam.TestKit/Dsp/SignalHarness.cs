using Vam.Modifiers.Abstractions;

namespace Vam.TestKit.Dsp;

/// <summary>
/// Runs a modifier against a signal a test can describe exactly, and reports what came out.
/// </summary>
/// <remarks>
/// Every modifier test wants the same thing: a known input, a run of blocks, and a level at the
/// end. Doing it here once means the tests are about the modifier rather than about the plumbing.
/// </remarks>
public sealed class SignalHarness
{
    readonly Modifier modifier;
    readonly float[] audio;
    readonly float[] parameters;
    readonly float[] scratch;
    readonly int channelCount;
    readonly int frameCount;

    ModifierTelemetry telemetry;

    /// <summary>Prepares a modifier and the buffers to run it against.</summary>
    /// <param name="modifier">What to test.</param>
    /// <param name="sampleRate">The rate to prepare it at.</param>
    /// <param name="frameCount">Frames per block.</param>
    /// <param name="channelCount">Channels.</param>
    public SignalHarness(Modifier modifier, int sampleRate = 48000, int frameCount = 120, int channelCount = 1)
    {
        ArgumentNullException.ThrowIfNull(modifier);

        this.modifier = modifier;
        this.frameCount = frameCount;
        this.channelCount = channelCount;

        SampleRate = sampleRate;

        audio = new float[frameCount * channelCount];
        scratch = new float[frameCount * channelCount];
        parameters = new float[modifier.Parameters.Length];

        for (int index = 0; index < parameters.Length; index++)
        {
            parameters[index] = modifier.Parameters[index].Default;
        }

        modifier.Prepare(sampleRate, frameCount, channelCount);
    }

    /// <summary>The rate the modifier was prepared at.</summary>
    public int SampleRate { get; }

    /// <summary>What the modifier last reported.</summary>
    public ModifierTelemetry Telemetry => telemetry;

    /// <summary>The block as it stands, after whatever has been run over it.</summary>
    public ReadOnlySpan<float> Audio => audio;

    /// <summary>Sets one parameter by its identifier.</summary>
    /// <param name="id">Which parameter.</param>
    /// <param name="value">Its value, as the modifier's own unit.</param>
    public void Set(string id, float value)
    {
        for (int index = 0; index < modifier.Parameters.Length; index++)
        {
            if (string.Equals(modifier.Parameters[index].Id, id, StringComparison.Ordinal))
            {
                parameters[index] = modifier.Parameters[index].Clamp(value);
                return;
            }
        }

        throw new ArgumentException($"No parameter called '{id}'.", nameof(id));
    }

    /// <summary>Fills the block with a sine at a frequency and amplitude.</summary>
    /// <param name="frequencyHz">Its frequency.</param>
    /// <param name="amplitude">Its peak.</param>
    /// <param name="phase">Where in the cycle to start, carried between blocks by the caller.</param>
    /// <returns>The phase to carry into the next block.</returns>
    public double Fill(double frequencyHz, float amplitude, double phase = 0.0)
    {
        double increment = 2.0 * Math.PI * frequencyHz / SampleRate;

        for (int frame = 0; frame < frameCount; frame++)
        {
            float sample = (float)(Math.Sin(phase) * amplitude);

            phase += increment;

            for (int channel = 0; channel < channelCount; channel++)
            {
                audio[(channel * frameCount) + frame] = sample;
            }
        }

        return phase % (2.0 * Math.PI);
    }

    /// <summary>Fills the block with a constant.</summary>
    /// <param name="value">The value.</param>
    public void FillConstant(float value) => Array.Fill(audio, value);

    /// <summary>Runs the modifier over the block that is there now.</summary>
    public void Process()
    {
        ModifierContext context = new(
            audio,
            parameters,
            scratch,
            ref telemetry,
            channelCount,
            frameCount,
            frameCount);

        modifier.Process(ref context);
    }

    /// <summary>
    /// Runs a sine through for a while and reports the peak of the last block.
    /// </summary>
    /// <param name="frequencyHz">The tone.</param>
    /// <param name="amplitude">Its peak going in.</param>
    /// <param name="blocks">How many blocks to run.</param>
    /// <returns>The peak coming out of the final block.</returns>
    public float RunTone(double frequencyHz, float amplitude, int blocks)
    {
        double phase = 0.0;

        for (int block = 0; block < blocks; block++)
        {
            phase = Fill(frequencyHz, amplitude, phase);
            Process();
        }

        return Peak();
    }

    /// <summary>The largest absolute sample in the block as it stands.</summary>
    /// <returns>The peak.</returns>
    public float Peak()
    {
        float peak = 0f;

        foreach (float sample in audio)
        {
            peak = Math.Max(peak, Math.Abs(sample));
        }

        return peak;
    }
}
