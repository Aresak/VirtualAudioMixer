namespace Vam.Engine.Devices;

/// <summary>
/// Turns one block from every input into one block for the primary output.
/// </summary>
/// <remarks>
/// <para>
/// This is the graph's entry point, before the graph exists. EPIC-03 implements it; until then the
/// clock runs with no consumer and plays silence, which is the honest behaviour rather than a stub —
/// there is genuinely nothing to mix yet.
/// </para>
/// <para>
/// <b>Inside the audio path.</b> No allocation, no lock, no wait. Returning fewer frames than asked
/// is allowed and plays as silence; blocking to avoid that would stop the output instead.
/// </para>
/// </remarks>
/// <param name="blocks">One block from each input device.</param>
/// <param name="output">Where to write the mix, interleaved at the primary output's channel count.</param>
/// <param name="frameCount">Frames wanted.</param>
/// <returns>Frames written to <paramref name="output"/>.</returns>
public delegate int MixCallback(MixBlocks blocks, Span<float> output, int frameCount);
