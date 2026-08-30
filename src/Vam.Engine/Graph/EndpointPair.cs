using Vam.Engine.Devices.Abstractions;

namespace Vam.Engine.Graph;

/// <summary>
/// A device whose microphone and speaker belong to the same person. D4.
/// </summary>
/// <remarks>
/// <para>
/// A conference speakerphone is one physical device carrying both a capture and a render endpoint,
/// and anything sent to its speaker is heard by whoever is speaking into its microphone. Declaring
/// that relationship is what lets the compiler work out mix-minus rather than the operator wiring
/// it by hand.
/// </para>
/// <para>
/// <b>Declared, never inferred.</b> Guessing from friendly names works right up until somebody
/// renames a device, and the failure mode is a councillor hearing themselves two hundred
/// milliseconds late in front of a public gallery.
/// </para>
/// </remarks>
/// <param name="CaptureDeviceId">The microphone.</param>
/// <param name="RenderDeviceId">The speaker the same person hears.</param>
public readonly record struct EndpointPair(AudioDeviceId CaptureDeviceId, AudioDeviceId RenderDeviceId);
