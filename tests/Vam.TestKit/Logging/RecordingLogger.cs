using Microsoft.Extensions.Logging;

namespace Vam.TestKit.Logging;

/// <summary>
/// An <see cref="ILogger{TCategory}"/> that keeps what it was told instead of writing it anywhere.
/// </summary>
/// <remarks>
/// For the cases where a log line is part of the contract rather than a side effect. VAM-017's third
/// acceptance criterion is one: a nonsense estimate has to produce a clamp <i>and</i> say so, and a
/// clamp nobody is told about is exactly the failure the criterion is guarding against.
/// </remarks>
/// <typeparam name="TCategory">The category type, as the generic logger interface requires.</typeparam>
public sealed class RecordingLogger<TCategory> : ILogger<TCategory>
{
    readonly List<LogRecord> entries = [];

    /// <summary>Everything written so far, in order.</summary>
    public IReadOnlyList<LogRecord> Entries => entries;

    /// <summary>Lines written at <see cref="LogLevel.Warning"/> or worse.</summary>
    public IReadOnlyList<LogRecord> Problems =>
        [.. entries.Where(entry => entry.Level >= LogLevel.Warning)];

    /// <inheritdoc />
    /// <remarks>Scopes are not recorded; nothing under test here opens one.</remarks>
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    /// <inheritdoc />
    /// <remarks>Always enabled, so a test never passes because a level happened to be filtered out.</remarks>
    public bool IsEnabled(LogLevel logLevel) => true;

    /// <inheritdoc />
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);

        entries.Add(new LogRecord(logLevel, formatter(state, exception)));
    }
}
