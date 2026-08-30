using Vam.Engine.Devices.Abstractions;
using Vam.Engine.Dsp;
using Vam.Engine.Graph;
using Vam.Engine.Graph.Nodes;
using Vam.TestKit.Harness;
using Xunit;

namespace Vam.Engine.Tests.Dsp;

/// <summary>
/// B3. The detector, where it sits, and the rule that it biases rather than gates.
/// </summary>
public class VoiceActivityTests
{
    const int SampleRate = 48000;
    const int BlockFrames = 480;

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void SilenceIsNotSpeech()
    {
        VoiceActivityDetector detector = new(SampleRate, BlockFrames);
        float[] block = new float[BlockFrames];

        for (int pass = 0; pass < 40; pass++)
        {
            Assert.False(detector.Observe(block));
        }
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void SomethingInTheSpeechBandWellAboveTheFloorIsSpeech()
    {
        VoiceActivityDetector detector = new(SampleRate, BlockFrames);
        float[] quiet = new float[BlockFrames];
        bool spoke = false;

        // A quiet room first, so the floor has something to learn.
        for (int pass = 0; pass < 60; pass++)
        {
            detector.Observe(quiet);
        }

        float[] voice = Tone(900, 0.3f);

        for (int pass = 0; pass < 20; pass++)
        {
            spoke |= detector.Observe(voice);
        }

        Assert.True(spoke, "A tone in the middle of the speech band never read as speech.");
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ARumbleBelowSpeechIsNotSpeech()
    {
        VoiceActivityDetector detector = new(SampleRate, BlockFrames);
        float[] rumble = Tone(45, 0.5f);
        bool spoke = false;

        for (int pass = 0; pass < 40; pass++)
        {
            spoke |= detector.Observe(rumble);
        }

        // Ventilation, a lift, a lorry outside. Loud, and not a person — which is the whole reason
        // the detector is band-limited rather than a level threshold.
        Assert.False(spoke, "A 45 Hz rumble read as somebody speaking.");
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ItLearnsHissAsTheRoomAndRefusesToLearnAVoice()
    {
        // Two halves of one behaviour, and the second is the reason the first is hard.
        //
        // A fixed threshold has to be set for the quietest chamber it will ever meet and then leaks
        // in every louder one, so the floor has to rise to meet the room. But it must not learn a
        // speaker as background, or a councillor who talks for four minutes disappears.
        VoiceActivityDetector inHiss = new(SampleRate, BlockFrames);
        float[] hiss = Hiss(0.08f);

        for (int pass = 0; pass < 4000; pass++)
        {
            inHiss.Observe(hiss);
        }

        Assert.True(inHiss.NoiseFloorDb > -80f, $"The floor stayed at {inHiss.NoiseFloorDb} dB in a hissy room.");

        VoiceActivityDetector inSpeech = new(SampleRate, BlockFrames);
        float[] voice = Tone(900, 0.3f);

        for (int pass = 0; pass < 4000; pass++)
        {
            inSpeech.Observe(voice);
        }

        // Louder than the hiss and deliberately not learned. This is what stops a long answer from
        // being absorbed into the background and the speaker from going quiet at the end of it.
        Assert.True(
            inSpeech.NoiseFloorDb < inSpeech.LevelDb - 6f,
            $"The floor climbed to {inSpeech.NoiseFloorDb} dB against a voice at {inSpeech.LevelDb} dB.");
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ConfidenceNeverReachesZeroInTheAutomixer()
    {
        // The rule that matters more than the detector itself. C1 claims the automixer is not a
        // gate; a confidence that multiplied a strip's weight to nothing would make it one, and a
        // voice the detector missed would be silenced rather than merely disfavoured.
        //
        // The first implementation did exactly that, and five tests that were not about the
        // detector caught it by going to zero.
        Assert.True(AutomixNode.MinimumConfidenceWeight > 0f);
        Assert.True(AutomixNode.MinimumConfidenceWeight <= 1f);

        double worstDisadvantageDb = 20.0 * Math.Log10(AutomixNode.MinimumConfidenceWeight);

        Assert.True(worstDisadvantageDb > -12.0, $"An unrecognised voice loses {worstDisadvantageDb:0.0} dB.");
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void TheTapIsCompiledBeforeTheChain()
    {
        // B3's placement, asserted rather than left to a comment. A detector reading denoised audio
        // agrees with the denoise instead of checking it, so the tap has to run first — and the
        // order of the node array is the evaluation order by construction.
        GraphConfig config = new();
        AudioDeviceId device = new("null:capture:mayor");

        config.InputDeviceOrder.Add(device);
        config.Channels.Add(new ChannelConfig
        {
            DeviceId = device,
            Name = "Mayor 180 degrees",
            Chain = { new Vam.Engine.Modifiers.ModifierSetting { ModifierId = "vam.denoise" } }
        });

        config.Buses.Add(new BusConfig { Name = "Stream", Role = BusRole.Output, ChannelCount = 2 });

        GraphController controller = new(
            config,
            BlockFrames,
            SampleRate,
            Vam.Engine.Modifiers.ModifierRegistry.CreateDefault());

        int tap = -1;
        int chain = -1;
        int index = 0;

        foreach (AudioNode node in controller.Publisher.Current.Plan.Nodes)
        {
            if (node is VoiceActivityTapNode)
            {
                tap = index;
            }

            if (node is ChainNode)
            {
                chain = index;
            }

            index++;
        }

        Assert.True(tap >= 0, "The plan has no voice activity tap in it at all.");
        Assert.True(chain >= 0, "The strip's chain was not compiled.");
        Assert.True(tap < chain, $"The tap is at {tap} and the chain at {chain}; it must read first.");
    }

    /// <summary>
    /// Something the detector will not mistake for a voice: too busy, and spread across the band.
    /// </summary>
    /// <remarks>
    /// Deterministic on purpose. A soak or a detector test that fails one run in ten gets disabled
    /// inside a week, and then nothing is protecting anything.
    /// </remarks>
    static float[] Hiss(float amplitude)
    {
        float[] block = new float[BlockFrames];
        uint state = 0x9E3779B9;

        for (int frame = 0; frame < block.Length; frame++)
        {
            state = (state * 1664525u) + 1013904223u;

            block[frame] = ((state >> 8) / (float)(1 << 24) * 2f - 1f) * amplitude;
        }

        return block;
    }

    static float[] Tone(double frequencyHz, float amplitude)
    {
        float[] block = new float[BlockFrames];

        for (int frame = 0; frame < block.Length; frame++)
        {
            block[frame] = (float)(Math.Sin(2.0 * Math.PI * frequencyHz * frame / SampleRate) * amplitude);
        }

        return block;
    }
}
