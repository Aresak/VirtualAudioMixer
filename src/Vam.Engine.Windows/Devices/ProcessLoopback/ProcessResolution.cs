namespace Vam.Engine.Windows.Devices.ProcessLoopback;

/// <summary>Which running process a <see cref="ProcessLoopbackId"/> names right now.</summary>
/// <param name="Outcome">Whether one was found.</param>
/// <param name="Process">The process, when <paramref name="Outcome"/> is <see cref="ProcessResolutionOutcome.Found"/>.</param>
/// <param name="Candidates">How many process trees fitted.</param>
public readonly record struct ProcessResolution(ProcessResolutionOutcome Outcome, ProcessEntry Process, int Candidates);
