using Microsoft.Extensions.Logging;

namespace Vam.TestKit.Logging;

/// <summary>One line a <see cref="RecordingLogger{TCategory}"/> was asked to write.</summary>
/// <param name="Level">How severe the caller said it was.</param>
/// <param name="Message">The rendered message, with its template arguments substituted.</param>
public readonly record struct LogRecord(LogLevel Level, string Message);
