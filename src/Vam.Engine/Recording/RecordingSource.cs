namespace Vam.Engine.Recording;

/// <summary>
/// Which channel or bus a track is recording.
/// </summary>
/// <remarks>
/// Carried on the track rather than implied by its position in the list. The graph used to find a
/// track by counting — inputs first, the primary bus last — which was true only while every session
/// recorded everything. A session that records the buses and not the inputs would then have written
/// the first microphone into the file named after the bus.
/// </remarks>
/// <param name="Kind">Whether this is an input or a bus.</param>
/// <param name="Index">Its index in the graph's configuration.</param>
public readonly record struct RecordingSource(RecordingSourceKind Kind, int Index);
