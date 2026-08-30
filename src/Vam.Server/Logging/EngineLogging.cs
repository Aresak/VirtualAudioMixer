using NLog.Config;
using NLog.Extensions.Logging;
using NLog.Targets;
using NLogLevel = NLog.LogLevel;
using NLogManager = NLog.LogManager;

namespace Vam.Server.Logging;

/// <summary>
/// One log stream, three consumers. I4.
/// </summary>
/// <remarks>
/// <para>
/// A rotated file on disk, an in-memory tail the diagnostics view reads, and Sentry when a key is
/// configured. The same pipeline feeds all three, so what the console shows and what an engineer
/// reads afterwards are the same lines rather than two views that can disagree.
/// </para>
/// <para>
/// <b>No key, no problem.</b> With no Sentry DSN the engine starts, logs to file, and says one line
/// about it. Refusing to run without telemetry configured would be the software prioritising its own
/// diagnostics over the meeting.
/// </para>
/// <para>
/// <b>Nothing here is ever called from the audio thread.</b> Structured records go into a ring and
/// the pump on the control thread turns them into these calls — see <c>DropoutPump</c>.
/// </para>
/// </remarks>
public static class EngineLogging
{
    /// <summary>Where the rotated files go.</summary>
    public static string DefaultDirectory { get; } =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "VAM",
            "logs");

    /// <summary>
    /// Builds the pipeline and hands it to the host's logging.
    /// </summary>
    /// <param name="builder">Where to attach it.</param>
    /// <param name="directory">Where the files go, or null for the default.</param>
    /// <param name="sentryDsn">
    /// The Sentry key, or null. It comes from configuration or the environment and never from
    /// source: a key committed to a public repository is a key anybody can send events with.
    /// </param>
    public static void Configure(ILoggingBuilder builder, string? directory = null, string? sentryDsn = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        LoggingConfiguration configuration = new();
        string folder = directory ?? DefaultDirectory;

        Directory.CreateDirectory(folder);

        FileTarget file = new("file")
        {
            FileName = Path.Combine(folder, "vam-${shortdate}.log"),
            Layout = "${longdate} ${uppercase:${level}} ${logger:shortName=true} ${message} ${exception:format=tostring}",
            ArchiveAboveSize = 32L * 1024 * 1024,
            MaxArchiveFiles = 20,
            KeepFileOpen = true
        };

        LogTailTarget tail = new()
        {
            Name = "tail",
            Layout = "${message} ${exception:format=message}"
        };

        configuration.AddTarget(file);
        configuration.AddTarget(tail);
        configuration.AddRule(NLogLevel.Info, NLogLevel.Fatal, file);
        configuration.AddRule(NLogLevel.Debug, NLogLevel.Fatal, tail);

        if (!string.IsNullOrWhiteSpace(sentryDsn))
        {
            AddSentry(configuration, sentryDsn);
        }

        NLogManager.Configuration = configuration;

        builder.ClearProviders();
        builder.AddNLog(configuration);

        NLogManager.GetCurrentClassLogger().Info(
            string.IsNullOrWhiteSpace(sentryDsn)
                ? "Logging to {0}. No Sentry key configured, so nothing leaves this machine."
                : "Logging to {0}, with Sentry enabled.",
            folder);
    }

    static void AddSentry(LoggingConfiguration configuration, string dsn)
    {
        Sentry.NLog.SentryTarget sentry = new()
        {
            Name = "sentry",
            Dsn = dsn,
            Layout = "${message}",

            IncludeEventDataOnBreadcrumbs = false
        };

        // This records a council chamber. Nothing that identifies a person leaves the building: no
        // audio, no file contents, and no personal information the SDK would otherwise attach by
        // default. A diagnostic bundle an operator sends deliberately is a different thing from
        // telemetry going out on its own.
        sentry.Options.SendDefaultPii = false;

        configuration.AddTarget(sentry);
        configuration.AddRule(NLogLevel.Warn, NLogLevel.Fatal, sentry);
    }
}
