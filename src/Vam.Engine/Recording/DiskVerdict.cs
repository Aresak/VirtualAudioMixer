namespace Vam.Engine.Recording;

/// <summary>
/// Whether there is room to record, and the numbers behind the answer.
/// </summary>
/// <remarks>
/// Carries the figures rather than only a yes or no, because an operator refused at five to seven
/// needs to know how much to free up, not that something was wrong.
/// </remarks>
/// <param name="CanStart">Whether recording may begin.</param>
/// <param name="FreeBytes">What was free when the check ran. Zero when it could not be read.</param>
/// <param name="ProjectedBytes">How large the session was expected to be.</param>
/// <param name="Description">What to tell the person.</param>
public readonly record struct DiskVerdict(bool CanStart, long FreeBytes, long ProjectedBytes, string Description);
