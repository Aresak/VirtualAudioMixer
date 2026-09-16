namespace Vam.Ui.Localization;

/// <summary>One language the console can be run in. U7.</summary>
/// <param name="Code">The two-letter code, and the name of its resource file.</param>
/// <param name="Name">What it calls itself, in itself. A person looking for their own language finds it under its
/// own name, not under an English one.</param>
public readonly record struct Language(string Code, string Name);
