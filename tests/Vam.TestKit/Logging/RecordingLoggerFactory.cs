using Microsoft.Extensions.Logging;

namespace Vam.TestKit.Logging;

/// <summary>
/// Hands out loggers that keep what they were told, and pools everything into one list.
/// </summary>
/// <remarks>
/// For the cases where a log line is part of the contract. VAM-019 asks for a device arriving or
/// leaving to appear in the log by name, which is not a detail — an operator diagnosing a dead strip
/// at five to seven has the log and nothing else.
/// </remarks>
public sealed class RecordingLoggerFactory : ILoggerFactory
{
    readonly List<LogRecord> entries = [];

    /// <summary>Everything written through any logger this factory made, in order.</summary>
    public IReadOnlyList<LogRecord> Entries => entries;

    /// <summary>Lines written at <see cref="LogLevel.Warning"/> or worse.</summary>
    public IReadOnlyList<LogRecord> Problems => [.. entries.Where(entry => entry.Level >= LogLevel.Warning)];

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new PooledLogger(entries);

    /// <inheritdoc />
    /// <remarks>Ignored. Nothing under test adds providers.</remarks>
    public void AddProvider(ILoggerProvider provider)
    {
    }

    /// <inheritdoc />
    public void Dispose() => entries.Clear();

    /// <summary>Whether anything written so far contains <paramref name="fragment"/>.</summary>
    /// <param name="fragment">Text to look for.</param>
    /// <returns>Whether it appeared.</returns>
    /// <summary>How many entries mention a fragment.</summary>
    /// <remarks>
    /// For the tests that care about folding. A device that has failed fails on every tick, and
    /// "did it say so" is a weaker question than "did it say so once".
    /// </remarks>
    /// <param name="fragment">What to look for.</param>
    /// <returns>The count.</returns>
    public int CountMentioning(string fragment) =>
        entries.Count(entry => entry.Message.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    /// <summary>A typed logger over the same pool.</summary>
    /// <typeparam name="T">Whose logger it is.</typeparam>
    /// <returns>The logger.</returns>
    public ILogger<T> CreateTyped<T>() => new Typed<T>(this);

    public bool Mentions(string fragment) =>
        entries.Any(entry => entry.Message.Contains(fragment, StringComparison.Ordinal));

    /// <summary>Adapts the pooled logger to the typed interface.</summary>
    sealed class Typed<T>(RecordingLoggerFactory factory) : ILogger<T>
    {
        readonly ILogger inner = factory.CreateLogger(typeof(T).Name);

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => inner.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => inner.IsEnabled(logLevel);

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            inner.Log(logLevel, eventId, state, exception, formatter);
    }

    sealed class PooledLogger(List<LogRecord> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            lock (entries)
            {
                entries.Add(new LogRecord(logLevel, formatter(state, exception)));
            }
        }
    }
}
