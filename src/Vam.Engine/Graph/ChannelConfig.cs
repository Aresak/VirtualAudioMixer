using Vam.Engine.Devices.Abstractions;
using Vam.Engine.Modifiers;

namespace Vam.Engine.Graph;

/// <summary>
/// One input strip, as the operator configured it.
/// </summary>
/// <remarks>
/// Levels are in decibels here and linear in <see cref="ChannelParams"/>. The conversion happens
/// once, in the compiler, because a decibel is what a person adjusts and a multiply is what the
/// audio thread wants.
/// </remarks>
public sealed record ChannelConfig
{
    /// <summary>The device this strip listens to.</summary>
    public required AudioDeviceId DeviceId { get; init; }

    /// <summary>What to call it on the console.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// The colour its strip is topped with. U5.
    /// </summary>
    /// <remarks>
    /// Kept by the engine rather than by each console, so two operators watching the same meeting
    /// see the same room. A colour that lived in the client would mean the strip an operator calls
    /// "the green one" is a different strip on the tablet next to them.
    /// </remarks>
    public string Colour { get; init; } = string.Empty;

    /// <summary>Channels the strip carries after any fold.</summary>
    public int ChannelCount { get; init; } = 1;

    /// <summary>Input trim. A8.</summary>
    public double TrimDb { get; init; }

    /// <summary>Fader position. B8.</summary>
    public double FaderDb { get; init; }

    /// <summary>Mute, solo, polarity and mono fold.</summary>
    public ChannelFlags Flags { get; init; } = ChannelFlags.None;

    /// <summary>
    /// The modifier chain between the head stage and the fader. B0.
    /// </summary>
    /// <remarks>
    /// Order is part of the configuration rather than an incidental list order. A gate before a
    /// denoise and a gate after one are different microphones.
    /// </remarks>
    public List<ModifierSetting> Chain { get; init; } = [];

    /// <summary>
    /// The preset this chain came from, or empty. B12.
    /// </summary>
    /// <remarks>
    /// Remembered so the console can say when the live chain has drifted away from it. An operator
    /// about to save over a preset needs to know whether what they are saving is what they think.
    /// </remarks>
    public string PresetName { get; init; } = string.Empty;

    /// <summary>
    /// Whether this strip takes part in gain sharing. C2.
    /// </summary>
    /// <remarks>
    /// Off by default for anything that is not a microphone in the room. Including the audience feed
    /// or the online return means the automixer shares gain with a loudspeaker playing back what it
    /// just sent, which is a loop with a very slow period and an unpleasant sound.
    /// </remarks>
    public bool ParticipatesInAutomix { get; init; }

    /// <summary>
    /// How much louder this microphone reads than the others for the same voice.
    /// </summary>
    /// <remarks>
    /// A lectern microphone somebody leans into and a table microphone a metre away are not
    /// comparable without it, and the automixer would hand the gain to the closer one every time.
    /// </remarks>
    public float AutomixWeight { get; init; } = 1f;
}
