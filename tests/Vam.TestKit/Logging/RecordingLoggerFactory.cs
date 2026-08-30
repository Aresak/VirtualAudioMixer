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
    public bool Mentions(string fragment) =>
        entries.Any(entry => entry.Message.Contains(fragment, StringComparison.Ordinal));

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
