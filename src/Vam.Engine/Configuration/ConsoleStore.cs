using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Vam.Engine.Devices;
using Vam.Engine.Graph;

namespace Vam.Engine.Configuration;

/// <summary>
/// Saves and restores a console. H1, H2 and H3.
/// </summary>
/// <remarks>
/// <para>
/// <b>Everything is keyed by identity, never by position.</b> Devices by their endpoint identifier,
/// modifier parameters by their own identifiers, buses by their place in a list that is saved with
/// them. A configuration that referred to "the third microphone" would silently point somewhere else
/// the first time somebody unplugged something.
/// </para>
/// <para>
/// <b>Written to a temporary file and moved into place.</b> A configuration half-written because the
/// machine lost power during a save is a console that will not open, and the meeting is at ten.
/// </para>
/// <para>
/// A file that cannot be read is not a reason to refuse to start. It is a reason to say so loudly
/// and open an empty console, because an operator can rebuild a console and cannot rebuild a
/// meeting that did not happen.
/// </para>
/// </remarks>
public sealed class ConsoleStore(ILogger<ConsoleStore> logger)
{
    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Writes a console to a file, atomically.</summary>
    /// <param name="path">Where it goes.</param>
    /// <param name="config">What to save.</param>
    public void Save(string path, GraphConfig config)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(config);

        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temporary = path + ".writing";

        File.WriteAllText(temporary, JsonSerializer.Serialize(StoredConsole.From(config), Options));

        // Moved rather than written in place. The window in which a file is half a console is the
        // window in which the power goes off, and the console is needed at ten.
        File.Move(temporary, path, overwrite: true);

        logger.LogInformation("Saved the console to {Path}.", path);
    }

    /// <summary>
    /// Reads a console back, or returns an empty one and says why.
    /// </summary>
    /// <param name="path">Where to look.</param>
    /// <returns>The console. Never null, never throws.</returns>
    public GraphConfig Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            logger.LogInformation("No console at {Path}. Starting empty.", path);
            return new GraphConfig();
        }

        try
        {
            StoredConsole? stored = JsonSerializer.Deserialize<StoredConsole>(File.ReadAllText(path), Options);

            if (stored is null)
            {
                logger.LogWarning("The console at {Path} was empty. Starting empty.", path);
                return new GraphConfig();
            }

            return stored.ToConfig();
        }
        catch (Exception error)
        {
            // Loud, and not fatal. An operator can rebuild a console; nobody can rebuild a meeting
            // that did not happen because the software refused to start.
            logger.LogError(error, "The console at {Path} could not be read. Starting empty.", path);

            return new GraphConfig();
        }
    }

    /// <summary>The saved shape. Separate from the live configuration so the file format can move independently.</summary>
    sealed record StoredConsole
    {
        public int Version { get; init; } = 1;

        public List<StoredChannel> Channels { get; init; } = [];

        public List<StoredBus> Buses { get; init; } = [];

        public List<SendConfig> Sends { get; init; } = [];

        public List<StoredPair> EndpointPairs { get; init; } = [];

        public int PrimaryBusIndex { get; init; }

        public int PrimaryOutputChannelCount { get; init; } = 2;

        public double AutomixDepthDb { get; init; } = -15.0;

        public double AutomixResponseMilliseconds { get; init; } = 120.0;

        public bool IsAutomixBypassed { get; init; } = true;

        public static StoredConsole From(GraphConfig config) => new()
        {
            Channels = [.. config.Channels.Select(StoredChannel.From)],
            Buses = [.. config.Buses.Select(StoredBus.From)],
            Sends = [.. config.Sends],
            EndpointPairs = [.. config.EndpointPairs.Select(pair => new StoredPair
            {
                CaptureDeviceId = pair.CaptureDeviceId.Value,
                RenderDeviceId = pair.RenderDeviceId.Value
            })],
            PrimaryBusIndex = config.PrimaryBusIndex,
            PrimaryOutputChannelCount = config.PrimaryOutputChannelCount,
            AutomixDepthDb = config.AutomixDepthDb,
            AutomixResponseMilliseconds = config.AutomixResponseMilliseconds,
            IsAutomixBypassed = config.IsAutomixBypassed
        };

        public GraphConfig ToConfig()
        {
            GraphConfig config = new()
            {
                PrimaryBusIndex = PrimaryBusIndex,
                PrimaryOutputChannelCount = PrimaryOutputChannelCount,
                AutomixDepthDb = AutomixDepthDb,
                AutomixResponseMilliseconds = AutomixResponseMilliseconds,
                IsAutomixBypassed = IsAutomixBypassed
            };

            foreach (StoredChannel channel in Channels)
            {
                config.Channels.Add(channel.ToConfig());
                config.InputDeviceOrder.Add(new Devices.Abstractions.AudioDeviceId(channel.DeviceId));
            }

            foreach (StoredBus bus in Buses)
            {
                config.Buses.Add(bus.ToConfig());
            }

            config.Sends.AddRange(Sends);

            foreach (StoredPair pair in EndpointPairs)
            {
                config.EndpointPairs.Add(new EndpointPair(
                    new Devices.Abstractions.AudioDeviceId(pair.CaptureDeviceId),
                    new Devices.Abstractions.AudioDeviceId(pair.RenderDeviceId)));
            }

            return config;
        }
    }

    sealed record StoredChannel
    {
        public required string DeviceId { get; init; }

        public required string Name { get; init; }

        public int ChannelCount { get; init; } = 1;

        public double TrimDb { get; init; }

        public double FaderDb { get; init; }

        public ChannelFlags Flags { get; init; }

        public bool ParticipatesInAutomix { get; init; }

        public float AutomixWeight { get; init; } = 1f;

        public List<Modifiers.ModifierSetting> Chain { get; init; } = [];

        public static StoredChannel From(ChannelConfig channel) => new()
        {
            DeviceId = channel.DeviceId.Value,
            Name = channel.Name,
            ChannelCount = channel.ChannelCount,
            TrimDb = channel.TrimDb,
            FaderDb = channel.FaderDb,

            // Solo is not saved. It is a monitoring state an operator sets during a meeting, and a
            // console that opened with something soloed would be silent on every other strip until
            // somebody worked out why.
            Flags = channel.Flags & ~ChannelFlags.Soloed,
            ParticipatesInAutomix = channel.ParticipatesInAutomix,
            AutomixWeight = channel.AutomixWeight,
            Chain = [.. channel.Chain]
        };

        public ChannelConfig ToConfig() => new()
        {
            DeviceId = new Devices.Abstractions.AudioDeviceId(DeviceId),
            Name = Name,
            ChannelCount = ChannelCount,
            TrimDb = TrimDb,
            FaderDb = FaderDb,
            Flags = Flags,
            ParticipatesInAutomix = ParticipatesInAutomix,
            AutomixWeight = AutomixWeight,
            Chain = [.. Chain]
        };
    }

    sealed record StoredBus
    {
        public required string Name { get; init; }

        public required BusRole Role { get; init; }

        public int ChannelCount { get; init; } = 2;

        public double GainDb { get; init; }

        public bool IsMuted { get; init; }

        public string OutputDeviceId { get; init; } = string.Empty;

        public static StoredBus From(BusConfig bus) => new()
        {
            Name = bus.Name,
            Role = bus.Role,
            ChannelCount = bus.ChannelCount,
            GainDb = bus.GainDb,
            IsMuted = bus.IsMuted,
            OutputDeviceId = bus.OutputDeviceId.Value
        };

        public BusConfig ToConfig() => new()
        {
            Name = Name,
            Role = Role,
            ChannelCount = ChannelCount,
            GainDb = GainDb,
            IsMuted = IsMuted,
            OutputDeviceId = new Devices.Abstractions.AudioDeviceId(OutputDeviceId)
        };
    }

    sealed record StoredPair
    {
        public required string CaptureDeviceId { get; init; }

        public required string RenderDeviceId { get; init; }
    }
}
