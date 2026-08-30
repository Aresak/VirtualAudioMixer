---
name: csharp-code-writer
description: C# coding standards for VAM. Enforces the Aviva Solutions guidelines, VAM's own overrides, Google C# Style member ordering, and the audio-path exception. Use when writing or refactoring any C# code.
---

# C# Code Writing Standards

## When to Use This Skill

- Writing new C# classes, methods, or properties
- Refactoring existing C# code
- Reviewing pull requests for code quality
- Resolving analyzer warnings
- Before committing C# code changes
- When uncertain about C# coding conventions

**This skill enforces:**
- **Aviva Solutions C# Coding Guidelines** (https://csharpcodingguidelines.com/) — the `AV####` rule IDs used throughout come from there
- **VAM project overrides** (critical!)
- **Google C# Style Guide** member ordering
- **The audio-path exception**, which outranks all of the above inside the audio path

---

## 🔴 FIRST: The Audio Path Overrides This Entire Document

`AGENTS.md` rule 1: **nothing in the audio path allocates, locks, or waits.** No `new`, no LINQ, no strings, no boxing, no closures, no `async`, no locks, no blocking calls inside an audio callback or anywhere in the mix graph. It is asserted by a test, not by good intentions.

The boundary is defined in `docs/audio-path.md`. Read it before writing anything below the line.

Three rules in this document must be **inverted** inside the audio path:

| Rule | Inside the audio path |
|---|---|
| **AV1130** Return interfaces for collections | ❌ `IEnumerable<T>` allocates an enumerator when walked and boxes a struct enumerator. Use `float[]`, `Span<float>`, `ReadOnlySpan<float>` |
| **AV1135** Never return null for collections; return `Enumerable.Empty<T>()` | ❌ That is LINQ, banned by name. Also covers `Task` — there is no `async` below the line at all |
| **AV1562** Avoid `ref`/`out` parameters | ❌ `out int consumed, out int produced` and `ref RenderContext` are how buffers and counts move without allocating. A `ref struct` context is also what makes "you may not retain this buffer" compiler-enforced for third-party modifiers |

```csharp
// ✅ CORRECT - audio path: spans, out counts, no allocation
public void Process(ReadOnlySpan<float> input, Span<float> output, out int consumed, out int produced)
{
    // ...
}

// ❌ WRONG - audio path: every one of these allocates or blocks
public async Task<IEnumerable<float>> ProcessAsync(List<float> input)
{
    return input.Where(sample => sample > threshold).ToList();
}
```

**AV1500** (max 14 statements) bends rather than breaks: a tight DSP kernel or a device copy loop may exceed it, because splitting one costs a call per sample. That is an exception per method, argued in a comment — not a general licence.

**AV1008** (avoid static classes) does *not* need an exception, because its extension-method carve-out covers the case. See [Extension methods instead of static helpers](#extension-methods-instead-of-static-helpers).

**Everything above the graph snapshot swap — mediator handlers, protocol, configuration, diagnostics, UI — follows this document unmodified.**

---

## 🚨 CRITICAL: VAM Project Overrides

**These rules OVERRIDE the standard Aviva Solutions guidelines and MUST be followed in this project.**

### 0. Modern C# — Always Use Latest Language Features

**The project targets .NET 10 with the latest C# version. Always prefer the most concise modern syntax available.** Don't fall back to verbose older patterns when shorter idiomatic alternatives exist.

```csharp
// ✅ CORRECT - Collection expressions
List<BusConfig> buses = [];
channelsByDevice[deviceId] = [channel];
List<int> channelIndices = [0, 1, 2];

// ❌ WRONG - Verbose pre-C# 12 syntax
List<BusConfig> buses = new List<BusConfig>();
channelsByDevice[deviceId] = new List<ChannelConfig> { channel };
List<int> channelIndices = new List<int> { 0, 1, 2 };

// ✅ CORRECT - Target-typed new
BusConfig bus = new() { Name = "Stream" };

// ❌ WRONG - Redundant type name
BusConfig bus = new BusConfig() { Name = "Stream" };
```

**One exception, and it is a big one:** collection expressions and target-typed `new` still allocate. Neither belongs inside the audio path, where everything is pre-allocated at graph build time.

### 1. Primary Constructors REQUIRED (No Underscore Prefixes)

```csharp
// ✅ CORRECT - Use primary constructors (modern C# style)
public class DeviceSupervisor(ILogger<DeviceSupervisor> logger, IDeviceRegistry registry)
{
    public async Task<AudioDeviceInfo> OpenAsync(AudioDeviceId deviceId, CancellationToken cancellationToken)
    {
        logger.LogInformation("Opening capture device {DeviceId}", deviceId);
        return await registry.ResolveAsync(deviceId, cancellationToken);
    }
}

// ❌ WRONG - Do NOT use underscore prefixes or old-style fields
public class DeviceSupervisor
{
    private readonly ILogger<DeviceSupervisor> _logger;
    private readonly IDeviceRegistry _registry;

    public DeviceSupervisor(ILogger<DeviceSupervisor> logger, IDeviceRegistry registry)
    {
        _logger = logger;
        _registry = registry;
    }
}
```

### 2. NO var Keyword (Explicit Types Only)

```csharp
// ✅ CORRECT - Always use explicit types
IReadOnlyList<AudioDeviceInfo> devices = await registry.EnumerateAsync(DeviceDirection.Capture);
AudioDeviceInfo? primary = devices.FirstOrDefault();
int deviceCount = devices.Count;
string displayName = primary?.FriendlyName ?? "Unknown";

// ❌ WRONG - Never use var (project override from AV1520)
var devices = await registry.EnumerateAsync(DeviceDirection.Capture);
var primary = devices.FirstOrDefault();
var deviceCount = devices.Count;
var displayName = primary?.FriendlyName ?? "Unknown";
```

### 3. Omit private Modifier (It's the Default)

```csharp
// ✅ CORRECT - Omit private modifier
public class DriftEstimator
{
    double estimatedRateHz; // Private by default

    void UpdateEstimate(int fillFrames) { } // Private by default
}

// ❌ WRONG - Do NOT write private modifier
public class DriftEstimator
{
    private double estimatedRateHz;

    private void UpdateEstimate(int fillFrames) { }
}
```

### 4. Blazor Injection Pattern

```csharp
// ✅ CORRECT - Use public required for Blazor [Inject]
public partial class ChannelStrip
{
    [Inject]
    public required IVamSession Session { get; set; }

    [Inject]
    public required NavigationManager Navigation { get; set; }
}

// ❌ WRONG - Do NOT use private for [Inject]
public partial class ChannelStrip
{
    [Inject]
    private IVamSession Session { get; set; }
}
```

### 5. File-Scoped Namespaces

```csharp
// ✅ CORRECT - Use file-scoped namespaces
namespace Vam.Engine.Devices;

public sealed class AudioRingBuffer
{
    // Implementation
}

// ❌ WRONG - Do NOT use block-scoped namespaces
namespace Vam.Engine.Devices
{
    public sealed class AudioRingBuffer
    {
        // Implementation
    }
}
```

### 6. Method Complexity: Max 14 Statements (Not 7)

**Project override from AV1500** — VAM allows max 14 statements per method (the standard guideline is 7).

```csharp
// ✅ CORRECT - Method within 14 statement limit
public async Task<BusConfig> CreateBusAsync(string name, BusRole role, CancellationToken cancellationToken)
{
    BusConfig? existing = await store.FindBusAsync(name, cancellationToken);

    if (existing is not null)
    {
        throw new DuplicateBusException($"A bus named {name} already exists");
    }

    BusConfig bus = new()
    {
        Name = name,
        Role = role,
        CreatedAt = timeProvider.GetUtcNow()
    };

    await store.AddBusAsync(bus, cancellationToken);
    await controller.PublishSnapshotAsync(cancellationToken);

    logger.LogInformation("Created bus {BusName} with role {BusRole}", name, role);
    return bus;
}
// Statement count: 11 (within limit)
```

### 7. One Type Per File

Every class, interface, enum, record and delegate gets its own file, named after the type. A nested enum that exists only for one type still gets its own file — the folder is the index.

Folders describe responsibility, not layer: `Devices/Clock/`, `Graph/Nodes/`, `Dsp/`, `Modifiers/BuiltIn/`, `Components/`, `Views/`.

### 8. Comments Earn Their Place

Comments explain **why**, never **what**. No file banners, no `// constructor`, no restating the signature.

```csharp
// ✅ CORRECT - explains a decision that is not derivable from the code
// Head and tail are padded to separate cache lines: without it the producer's
// write invalidates the consumer's line on every block, times one ring per device.
[StructLayout(LayoutKind.Explicit, Size = 128)]
struct PaddedCursor
{
    [FieldOffset(0)]
    public long Value;
}

// ❌ WRONG - restates the code
// The cursor value
public long Value;
```

Public API surface gets XML docs. Internals do not, unless the why-rule applies.

---

## Class Design Rules (AV1000-series)

### AV1000: Single Responsibility Principle

A class should have one clear purpose within the system.

```csharp
// ✅ CORRECT - Single responsibility
public class ChannelMapValidator
{
    public ValidationResult Validate(ChannelMap map) { }
}

public class DeviceRegistry
{
    public AudioDeviceInfo? Resolve(AudioDeviceId deviceId) { }
}

// ❌ WRONG - Multiple responsibilities
public class DeviceManager
{
    public ValidationResult ValidateMap(ChannelMap map) { }
    public AudioDeviceInfo? ResolveDevice(AudioDeviceId deviceId) { }
    public void WriteConfigFile(string path) { }
}
```

**Red flag:** class names containing "And", "Manager", "Helper", "Utility".

### AV1001: Constructors Must Return Useful Objects (Max 3-4 Parameters)

```csharp
// ✅ CORRECT - Object is fully functional after construction
public class AudioRingBuffer(int capacityFrames, int channelCount)
{
    public int CapacityFrames { get; } = capacityFrames;
    public int ChannelCount { get; } = channelCount;
}

// ❌ WRONG - Too many constructor parameters (indicates too many responsibilities)
public class EngineController(
    ILogger<EngineController> logger,
    IDeviceRegistry registry,
    IConfigStore store,
    IRecordingService recording,
    IMeterPublisher meters)
{
}

// ✅ CORRECT - Refactor to separate concerns
public class EngineController(
    ILogger<EngineController> logger,
    IDeviceRegistry registry,
    IConfigStore store)
{
}
```

### AV1003: Interfaces Should Be Small and Focused

```csharp
// ✅ CORRECT - Focused interfaces
public interface IDeviceEnumerator
{
    IReadOnlyList<AudioDeviceInfo> Enumerate(DeviceDirection direction);
    AudioDeviceInfo? Resolve(AudioDeviceId deviceId);
}

public interface IDeviceOpener
{
    ICaptureStream OpenCapture(AudioDeviceId deviceId, CaptureOptions options);
    IRenderStream OpenRender(AudioDeviceId deviceId, RenderOptions options);
}

// ❌ WRONG - Interface does too much
public interface IDeviceManager
{
    IReadOnlyList<AudioDeviceInfo> Enumerate(DeviceDirection direction);
    AudioDeviceInfo? Resolve(AudioDeviceId deviceId);
    ICaptureStream OpenCapture(AudioDeviceId deviceId, CaptureOptions options);
    IRenderStream OpenRender(AudioDeviceId deviceId, RenderOptions options);
    void SaveConfiguration(string path);
    void PublishTelemetry();
}
```

### AV1004: Prefer Interfaces Over Base Classes

```csharp
// ✅ CORRECT - Use interfaces for extension points
public interface ICommandHandler<TCommand>
{
    Task HandleAsync(TCommand command, CancellationToken cancellationToken);
}

public class SetFaderCommandHandler(IEngineController controller)
    : ICommandHandler<SetFaderCommand>
{
    public async Task HandleAsync(SetFaderCommand command, CancellationToken cancellationToken)
    {
        await controller.SetFaderAsync(command.ChannelIndex, command.LevelDb, cancellationToken);
    }
}

// ❌ WRONG - Base class forces unwanted behavior
public abstract class CommandHandlerBase
{
    protected void LogStart() { } // Forces all handlers to have logging
    protected void ValidateOperator() { } // Forces all handlers to validate
}
```

**Exception, and it is deliberate:** `AudioNode` and `Modifier` are abstract base classes, not interfaces. An interface call on a value type boxes, and dispatch happens once per node per block. The base class is the allocation-free choice, and the audio path outranks this rule.

### AV1005: Use Interfaces to Prevent Coupling

```csharp
// ✅ CORRECT - Depends on interface
public class MixerService(IEngineController controller)
{
    public async Task<BusState> GetBusAsync(int busIndex, CancellationToken cancellationToken)
    {
        return await controller.ReadBusAsync(busIndex, cancellationToken);
    }
}

// ❌ WRONG - Depends on concrete implementation
public class MixerService(EngineController controller)
{
}
```

### AV1008: Avoid Static Classes (Except Extension Methods)

```csharp
// ✅ CORRECT - Instance class with DI
public class DiskGuard(ILogger<DiskGuard> logger)
{
    public DiskGuardVerdict CheckBeforeStart(string path, long projectedBytes) { }
}

// ✅ CORRECT - Extension methods (only valid use of static classes)
public static class BusRoleExtensions
{
    public static bool IsMonitor(this BusRole role) => role == BusRole.Monitor;

    public static bool RequiresOutputDevice(this BusRole role) => role is BusRole.Output or BusRole.Monitor;
}

// ❌ WRONG - Static class for utilities (hard to test, hard to mock)
public static class DiskHelper
{
    public static bool HasRoom(string path, long bytes) { }
}
```

<a id="extension-methods-instead-of-static-helpers"></a>
#### Extension methods instead of static helpers

The audio path needs allocation-free math helpers, and routing them through DI would put a vtable call in the callback. **Write them as extension methods**, which this rule already permits:

```csharp
// ✅ CORRECT - extensions on the natural receiver, allocation-free
namespace Vam.Engine.Dsp.Extensions;

public static class SpanExtensions
{
    public static void MixInto(this Span<float> destination, ReadOnlySpan<float> source, float gain) { }

    public static void FlushDenormals(this Span<float> buffer) { }

    public static float PeakAbs(this ReadOnlySpan<float> buffer) { }
}

public static class DecibelExtensions
{
    public static float ToLinear(this float decibels) { }

    public static float ToDecibels(this float linear) { }
}

// ❌ WRONG - a plain static utility class
public static class SimdOps
{
    public static void MixInto(Span<float> destination, ReadOnlySpan<float> source, float gain) { }
}
```

`Span<float>` is legal as a `this` parameter, so this stays allocation-free and reads better inside a kernel. The same pattern covers enums: `role.IsMonitor()`, `state.IsUsable()`, `error.ToMessage()`.

**Two conditions:**
1. These extensions live in **their own namespace**, imported only where needed, so `float` and `Span<float>` do not sprout VAM methods everywhere in IntelliSense.
2. Inside the audio path, test flags with `&`, never `Enum.HasFlag`.

**Two plain statics are still allowed**, because they have no natural receiver: biquad coefficient factories, and `AllocationAssert` in `Vam.TestKit`, which VAM-004 specifies as a static class verbatim.

### AV1011: Liskov Substitution Principle

Derived types must be usable wherever base types are expected.

```csharp
// ✅ CORRECT - Derived class honors base contract
public abstract class AudioNode
{
    public abstract void Process(ref RenderContext context);
}

public sealed class FaderNode : AudioNode
{
    public override void Process(ref RenderContext context)
    {
        ApplyGain(ref context);
    }
}

// ❌ WRONG - Breaks polymorphism
public sealed class PlaceholderNode : AudioNode
{
    public override void Process(ref RenderContext context)
    {
        throw new NotImplementedException("Placeholder nodes cannot render");
    }
}
```

An `AudioNode` that throws is worse than a bug — the audio thread cannot handle an exception. A node with nothing to do writes silence.

### AV1014: Law of Demeter (Avoid Exposing Dependencies)

```csharp
// ✅ CORRECT - Don't chain through dependencies
public class ChannelService(IDeviceRegistry registry)
{
    public string GetChannelDeviceName(ChannelConfig channel)
    {
        return registry.Resolve(channel.DeviceId)?.FriendlyName ?? "Absent";
    }
}

// ❌ WRONG - Exposes internal structure
public class ChannelService(IDeviceRegistry registry)
{
    public string GetChannelDeviceName(ChannelConfig channel)
    {
        return registry.Resolve(channel.DeviceId).Endpoint.Properties.FriendlyName;
    }
}
```

### AV1025: Classes Need Both State and Behavior

```csharp
// ✅ CORRECT - Combines data with logic
public class ChannelConfig
{
    public int Index { get; set; }
    public string Name { get; set; } = string.Empty;
    public AudioDeviceId DeviceId { get; set; }
    public bool ParticipatesInAutomix { get; set; }

    public bool IsUsable(IDeviceRegistry registry) => registry.Resolve(DeviceId) is not null;
}

// ❌ WRONG - Data-only class paired with a static behavior class
public class ChannelConfig
{
    public int Index { get; set; }
    public AudioDeviceId DeviceId { get; set; }
}

public static class ChannelConfigHelpers
{
    public static bool IsUsable(ChannelConfig channel, IDeviceRegistry registry) { }
}
```

**Exceptions:** DTOs are intentionally data-only, and so are the snapshot parameter structs (`ChannelParams`, `BusParams`). Those are read by the audio thread and must stay plain data with no behaviour that could allocate.

---

## Member Design Rules (AV1100-series)

### AV1100: Properties Can Be Set in Any Order

```csharp
// ✅ CORRECT - Properties are independent
public class RecordingRequest
{
    public string? DestinationFolder { get; set; }
    public RecordingFormat Format { get; set; }
    public bool IncludeBuses { get; set; }
}

// ❌ WRONG - Properties affect each other
public class SendConfig
{
    int busIndex;
    float levelDb;

    public int BusIndex
    {
        get => busIndex;
        set
        {
            busIndex = value;
            levelDb = 0f; // Resets another property!
        }
    }
}
```

### AV1105: Use Method Instead of Property

Use a method when the operation is expensive, performs a conversion, yields a different result each call, or has side effects.

```csharp
// ✅ CORRECT - Expensive operation is a method
public class DeviceRegistry
{
    public IReadOnlyList<AudioDeviceInfo> Enumerate(DeviceDirection direction)
    {
        return EnumerateEndpoints(direction); // Expensive - COM enumeration
    }
}

// ❌ WRONG - Expensive operation as property
public class DeviceRegistry
{
    public IReadOnlyList<AudioDeviceInfo> AllDevices => EnumerateEndpoints(DeviceDirection.Any);
}
```

### AV1130: Return Interfaces for Collections

```csharp
// ✅ CORRECT - Return interface (prevents modification)
public class BusRegistry
{
    List<BusConfig> buses = [];

    public IReadOnlyList<BusConfig> GetBuses() => buses;
}

// ❌ WRONG - Returns concrete collection (caller can modify internal list)
public class BusRegistry
{
    List<BusConfig> buses = [];

    public List<BusConfig> GetBuses() => buses;
}
```

**🔴 Inverted inside the audio path.** `IEnumerable<T>` allocates an enumerator when walked and boxes a struct enumerator. Below the line, return `float[]`, `Span<float>` or `ReadOnlySpan<float>`, or fill a caller-supplied span:

```csharp
// ✅ CORRECT - audio path and hot telemetry polling
public void GetAllTelemetry(Span<DeviceTelemetry> destination) { }
```

### AV1135: Never Return null for Strings, Collections, or Tasks

```csharp
// ✅ CORRECT - Return empty collection instead of null
public class BusRegistry
{
    public IReadOnlyList<BusConfig> GetMonitors()
    {
        return monitors ?? [];
    }

    public string GetBusName(int busIndex)
    {
        return buses.FirstOrDefault(bus => bus.Index == busIndex)?.Name ?? string.Empty;
    }
}

// ❌ WRONG - Returns null, forcing every caller to check
public class BusRegistry
{
    public IReadOnlyList<BusConfig>? GetMonitors() => null;

    public string? GetBusName(int busIndex) => null;
}
```

**🔴 Inverted inside the audio path.** `Enumerable.Empty<T>()` and `FirstOrDefault` are LINQ, banned by name. `Task` is irrelevant because there is no `async` below the line at all. Below the line, a buffer with nothing in it is a span of length zero, which allocates nothing and needs no null check.

### AV1137: Define Parameters as Specific as Possible

```csharp
// ✅ CORRECT - Accept only what you need
public async Task RenameChannelAsync(int channelIndex, string newName, CancellationToken cancellationToken)
{
    ChannelConfig? channel = await store.FindChannelAsync(channelIndex, cancellationToken);

    if (channel is not null)
    {
        channel.Name = newName;
        await store.UpdateChannelAsync(channel, cancellationToken);
    }
}

// ❌ WRONG - Accepts the whole object when only index and name are used
public async Task RenameChannelAsync(ChannelConfig channel, CancellationToken cancellationToken)
{
}
```

---

## Maintainability Rules (AV1500-series)

### AV1500: Methods Should Not Exceed 14 Statements

**Project override: VAM allows 14 statements (the standard is 7).**

Bends inside the audio path, as noted at the top: a DSP kernel or device copy loop may exceed it, because splitting a tight loop costs a call per sample. Argue the exception in a comment.

### AV1515: No Magic Numbers (Use Named Constants)

**Constants MUST be at the top of the class, after nested types and before fields.**

```csharp
// ✅ CORRECT - Named constants at top of class
public class FillServo
{
    const double MaxCorrectionPpm = 500.0;
    const double IntegralTimeConstantSeconds = 10.0;
    const int UpdateIntervalMilliseconds = 100;

    public double Update(int currentFillFrames, double elapsedSeconds)
    {
        // ...
    }
}

// ❌ WRONG - Magic numbers
public class FillServo
{
    public double Update(int currentFillFrames, double elapsedSeconds)
    {
        if (Math.Abs(ratioPpm) > 500.0) // Why 500?
        {
            ratioPpm = 500.0;
        }
    }
}
```

Where a constant encodes a physical fact rather than a preference, the comment says which: `// 480 frames is RNNoise's fixed frame size and cannot be changed.`

### AV1525: Don't Compare Booleans to true/false

```csharp
// ✅ CORRECT - Direct boolean check
if (channel.IsMuted)
{
    return;
}

if (!device.IsPresent)
{
    logger.LogWarning("Device {DeviceId} is absent", device.Id);
}

// ❌ WRONG - Explicit comparison
if (channel.IsMuted == true) { }
if (device.IsPresent == false) { }
```

### AV1535: Always Add Blocks After Keywords

```csharp
// ✅ CORRECT - Always use braces
if (device is null)
{
    return DeviceStreamState.Absent;
}

foreach (BusConfig bus in buses)
{
    bus.Reset();
}

// ❌ WRONG - No braces (error-prone when adding statements)
if (device is null)
    return DeviceStreamState.Absent;

foreach (BusConfig bus in buses)
    bus.Reset();
```

### AV1536: Always Include default in switch

```csharp
// ✅ CORRECT - Has default case
public string GetStateLabel(DeviceStreamState state)
{
    return state switch
    {
        DeviceStreamState.Stopped => "Stopped",
        DeviceStreamState.Running => "Running",
        DeviceStreamState.Faulted => "Faulted",
        DeviceStreamState.Absent => "Absent",
        _ => throw new ArgumentException($"Unknown device state: {state}")
    };
}

// ❌ WRONG - Missing default case
public string GetStateLabel(DeviceStreamState state)
{
    return state switch
    {
        DeviceStreamState.Stopped => "Stopped",
        DeviceStreamState.Running => "Running"
    };
}
```

### AV1546: Prefer Interpolated Strings Over Concatenation

```csharp
// ✅ CORRECT - String interpolation, and structured logging templates
string message = $"Bus {bus.Name} is {bus.Role}";
logger.LogInformation("Device {DeviceId} drifted {DriftPpm} ppm", device.Id, driftPpm);

// ❌ WRONG - String concatenation
string message = "Bus " + bus.Name + " is " + bus.Role;

// ❌ WRONG - interpolation into a log template destroys structured logging
logger.LogInformation($"Device {device.Id} drifted {driftPpm} ppm");
```

**No string of any kind is built inside the audio path.** The audio thread writes a structured record with an index; the control-thread pump resolves the index against a name table and formats it.

### AV1561: Don't Declare Signatures with More Than 3-4 Parameters

```csharp
// ✅ CORRECT - Max 4 parameters
public async Task<BusConfig> CreateBusAsync(
    string name,
    BusRole role,
    AudioDeviceId outputDevice,
    CancellationToken cancellationToken)
{
}

// ✅ CORRECT - Use a parameter object when there are more
public record CreateBusRequest(
    string Name,
    BusRole Role,
    AudioDeviceId OutputDevice,
    float InitialLevelDb,
    bool LimiterEnabled);

public async Task<BusConfig> CreateBusAsync(CreateBusRequest request, CancellationToken cancellationToken)
{
}
```

### AV1562: Avoid ref or out Parameters

```csharp
// ✅ CORRECT - Return a tuple or an object
public (bool Found, AudioDeviceInfo? Device) TryResolve(AudioDeviceId deviceId)
{
    AudioDeviceInfo? device = registry.Resolve(deviceId);
    return device is not null ? (true, device) : (false, null);
}

// ✅ CORRECT - Standard TryParse pattern
public bool TryParseDeviceId(string input, out AudioDeviceId deviceId) { }

// ❌ WRONG - out parameter outside the TryParse pattern
public bool GetDevice(AudioDeviceId id, out AudioDeviceInfo device) { }
```

**🔴 Inverted inside the audio path.** Below the line, `out` counts and `ref` contexts are the correct tool:

```csharp
// ✅ CORRECT - audio path
public void Process(ReadOnlySpan<float> input, Span<float> output, out int consumed, out int produced) { }

public abstract void Process(ref RenderContext context);
```

The `ref struct` context is not merely an optimisation — it is what makes "you may not retain this buffer" compiler-enforced for third-party modifiers, instead of a comment nobody reads.

### AV1575: Never Check In Commented-Out Code

```csharp
// ✅ CORRECT - Clean code
public AudioDeviceInfo? Resolve(AudioDeviceId deviceId)
{
    return registry.Resolve(deviceId);
}

// ❌ WRONG - Commented-out code checked into the repository
public AudioDeviceInfo? Resolve(AudioDeviceId deviceId)
{
    // Old implementation
    // var device = devices.FirstOrDefault(d => d.Id == deviceId);
    // if (device == null)
    //     throw new NotFoundException();
    // return device;

    return registry.Resolve(deviceId);
}
```

---

## Naming Conventions (AV1700-series)

### AV1702: Proper Casing

```csharp
// ✅ CORRECT - PascalCase for public members
public class DriftEstimator
{
    public double DriftPpm { get; }
    public void Observe(in ClockReading reading) { }
}

public enum BusRole
{
    Output,
    Monitor,
    SubMix
}

// ✅ CORRECT - camelCase for parameters and local variables
public void UpdateChannel(string channelName, int channelIndex)
{
    string normalizedName = channelName.Trim();
}

// ✅ CORRECT - camelCase for private fields (no underscore!)
public class DriftEstimator
{
    double estimatedRateHz;
    int settledSampleCount;
}
```

### AV1705: No Field Prefixes

```csharp
// ✅ CORRECT - No prefixes
public class MasterClock
{
    long framePosition;
    int blockFrames;
}

// ❌ WRONG - Field prefixes
public class MasterClock
{
    long _framePosition;
    int m_blockFrames;
    static int s_defaultBlockFrames;
}
```

### AV1706: No Abbreviations (Use Full Words)

```csharp
// ✅ CORRECT - Full words
public class DeviceRegistry
{
    public AudioDeviceInfo? Resolve(AudioDeviceId deviceId) { }
    public string GetFriendlyName(AudioDeviceInfo device) { }
}

// ❌ WRONG - Abbreviations
public class DevReg
{
    public AudioDeviceInfo? Res(AudioDeviceId id) { }
    public string GetFrNm(AudioDeviceInfo dev) { }
}

// ✅ ACCEPTABLE - Well-known acronyms and domain terms
public class WasapiBackend { }        // WASAPI is the API's name
public double DriftPpm { get; }       // ppm is the unit
public float LevelDb { get; }         // dB is the unit
public int Nom { get; }               // NOM is the audio term (number of open microphones)
public Guid Id { get; set; }
```

Audio and signal-processing terms are not abbreviations to be expanded — `Rms`, `Lufs`, `Vad`, `Eq`, `Hpf` are the names of the things. Spelling them out would make the code harder to read for anyone who knows the domain.

### No Shortcut Variable Names (Use Descriptive Names)

**NEVER use single-letter or abbreviated variable names. Always use full descriptive names.**

```csharp
// ✅ CORRECT - Full descriptive names
for (int channelIndex = 0; channelIndex < channels.Count; channelIndex++)
{
    ChannelConfig channel = channels[channelIndex];
}

foreach (BusConfig bus in buses)
{
    float levelDb = bus.LevelDb;
}

// ❌ WRONG - Single letter or abbreviated names
for (int i = 0; i < channels.Count; i++)
{
    var c = channels[i];
}
```

**One exception, inside DSP kernels.** Where the code implements a published formula, the formula's own symbols keep their published names, because renaming them makes the code harder to check against the source, not easier:

```csharp
// ✅ CORRECT - transposed direct form II, names follow the standard notation
for (int sampleIndex = 0; sampleIndex < buffer.Length; sampleIndex++)
{
    float x = buffer[sampleIndex];
    float y = b0 * x + z1;
    z1 = b1 * x - a1 * y + z2;
    z2 = b2 * x - a2 * y;
    buffer[sampleIndex] = y;
}
```

The loop variable is still `sampleIndex`, and a comment names the formula.

### AV1715: Property Naming (Booleans)

```csharp
// ✅ CORRECT - Boolean properties with Is/Has/Can/Allows/Supports
public class ChannelConfig
{
    public bool IsMuted { get; set; }
    public bool HasDrifted { get; set; }
    public bool CanParticipateInAutomix { get; set; }
    public bool SupportsExclusiveMode { get; set; }
}

// ❌ WRONG - Boolean property without prefix
public class ChannelConfig
{
    public bool Muted { get; set; }
    public bool Drifted { get; set; }
}
```

### AV1720: Method Names Should Be Verbs

```csharp
// ✅ CORRECT - Verb or verb-object pairs
public class EngineController
{
    public void Start() { }
    public void PublishSnapshot() { }
    public BusConfig CreateBus(string name) { }
    public bool ValidateChannelMap(ChannelMap map) { }
}

// ❌ WRONG - Noun, or contains "And"
public class EngineController
{
    public void Snapshot() { }
    public void CreateAndPublishBus(string name) { }
}
```

### AV1755: Async Methods Must Have Async Suffix

```csharp
// ✅ CORRECT - Async suffix
public async Task<BusConfig> GetBusAsync(int busIndex, CancellationToken cancellationToken) { }

// ❌ WRONG - Missing Async suffix
public async Task<BusConfig> GetBus(int busIndex, CancellationToken cancellationToken) { }
```

There is no `async` inside the audio path, so this rule never applies there.

---

## Code Layout and Member Ordering (AV2400-series + Google C# Style)

### Modifier Order

```
public → protected → internal → private → new → abstract → virtual → override →
sealed → static → readonly → extern → unsafe → volatile → async
```

```csharp
// ✅ CORRECT - Proper modifier order
public abstract class AudioNode
{
    public static readonly int DefaultBlockFrames = 120;
    protected virtual void OnPrepared() { }
}

public sealed class FaderNode : AudioNode
{
    protected override void OnPrepared() { }
}
```

**Nodes and modifiers are `sealed class`, never `struct`** — a struct reached through an interface boxes, and sealing lets the JIT devirtualize leaf calls inside the chain loop.

### Class Member Ordering

**Order class members in these groups:**

1. Nested classes, enums, delegates, events
2. Static, const, readonly fields
3. Fields and properties
4. Constructors and finalizers
5. Instance methods (public → private)
6. Static methods (at bottom)

**Within each group, order by visibility:** Public → Internal → Protected internal → Protected → Private

```csharp
// ✅ CORRECT - Proper member ordering
public class DriftEstimator
{
    // 1. Nested types — but see rule 7: they get their own file unless
    //    they are genuinely private to this class
    // 2. Static, const, readonly fields
    const double SettleThresholdPpm = 1.0;
    static readonly TimeSpan ObservationWindow = TimeSpan.FromSeconds(30);

    // 3. Fields and properties (public → private)
    public double DriftPpm { get; private set; }
    double estimatedRateHz;

    // 4. Constructor
    public DriftEstimator(int nominalRateHz) { }

    // 5. Instance methods (public → private)
    public void Observe(in ClockReading reading)
    {
        UpdateEstimate(reading);
    }

    void UpdateEstimate(in ClockReading reading) { }

    // 6. Static methods (at bottom)
    public static DriftEstimator ForDevice(AudioDeviceInfo device) { }
}
```

### Blazor Component Member Ordering (.razor.cs)

1. `[CascadingParameter]` properties
2. `[Inject]` properties
3. `[Parameter]` properties
4. Private fields
5. Lifecycle methods (`OnInitializedAsync`, `OnParametersSetAsync`, `Dispose`)
6. Private methods (event handlers, helpers)

### Blazor StateHasChanged — ALWAYS Use InvokeAsync

**In async methods, ALWAYS use `await InvokeAsync(StateHasChanged)` instead of `StateHasChanged()`.**

```csharp
// ✅ CORRECT - Always use InvokeAsync in async methods
public partial class MixerView
{
    async Task LoadChannelsAsync()
    {
        isLoading = true;
        await InvokeAsync(StateHasChanged);

        try
        {
            channels = await Session.GetChannelsAsync();
        }
        finally
        {
            isLoading = false;
            await InvokeAsync(StateHasChanged);
        }
    }
}

// ❌ WRONG - Direct StateHasChanged in an async method
public partial class MixerView
{
    async Task LoadChannelsAsync()
    {
        isLoading = true;
        StateHasChanged();  // NOT thread-safe!
    }
}
```

**Why:** after an `await`, code may resume on a different thread. `InvokeAsync(StateHasChanged)` ensures the UI update happens on the correct synchronization context. Meter frames arrive on a gRPC stream thread, so this is not theoretical here.

**Meters never go through `StateHasChanged` at all.** Binary meter frames are drawn straight into a canvas through JS interop. Diffing the DOM 25 times a second across 16 channels is how this UI would be made to feel slow.

```csharp
// ✅ CORRECT - Proper Blazor component ordering
public partial class ChannelStrip : ComponentBase, IDisposable
{
    // 1. Cascading parameters
    [CascadingParameter]
    public required MixerState Mixer { get; set; }

    // 2. Injected services
    [Inject]
    public required IVamSession Session { get; set; }

    // 3. Component parameters
    [Parameter]
    public required int ChannelIndex { get; set; }

    [Parameter]
    public EventCallback<int> OnChannelSelected { get; set; }

    // 4. Private fields
    ChannelState? channel;
    bool isLoading = true;

    // 5. Lifecycle methods
    protected override async Task OnInitializedAsync()
    {
        await LoadChannelAsync();
    }

    public void Dispose()
    {
        // Cleanup
    }

    // 6. Private methods
    async Task LoadChannelAsync()
    {
        channel = await Session.GetChannelAsync(ChannelIndex);
        isLoading = false;
        await InvokeAsync(StateHasChanged);
    }

    Task HandleStripClickAsync() => OnChannelSelected.InvokeAsync(ChannelIndex);
}
```

### Namespace Organization (AV2402)

```csharp
// ✅ CORRECT - System namespaces first, then others alphabetically
using System;
using System.Buffers;
using System.Threading;
using Microsoft.Extensions.Logging;
using Vam.Engine.Audio;
using Vam.Engine.Devices;

namespace Vam.Engine.Graph;
```

### Line Length and Formatting (AV2400)

Lines under 130 characters, 4-space indentation.

---

## Common Mistakes

### Mistake 1: Using var Instead of Explicit Types

```csharp
// ❌ WRONG
var devices = registry.Enumerate(DeviceDirection.Capture);

// ✅ CORRECT
IReadOnlyList<AudioDeviceInfo> devices = registry.Enumerate(DeviceDirection.Capture);
```

### Mistake 2: Adding private Modifier

```csharp
// ❌ WRONG
private void UpdateEstimate() { }
private double estimatedRateHz;

// ✅ CORRECT
void UpdateEstimate() { }
double estimatedRateHz;
```

### Mistake 3: Using Underscore Prefixes for Fields

```csharp
// ❌ WRONG
public class DeviceSupervisor
{
    private readonly ILogger _logger;
    public DeviceSupervisor(ILogger logger) => _logger = logger;
}

// ✅ CORRECT
public class DeviceSupervisor(ILogger logger)
{
    public void Start() => logger.LogInformation("Supervisor started");
}
```

### Mistake 4: Wrong Blazor Injection Pattern

```csharp
// ❌ WRONG
[Inject] private IVamSession Session { get; set; }

// ✅ CORRECT
[Inject] public required IVamSession Session { get; set; }
```

### Mistake 5: Block-Scoped Namespaces

```csharp
// ❌ WRONG
namespace Vam.Engine.Devices
{
    public sealed class AudioRingBuffer { }
}

// ✅ CORRECT
namespace Vam.Engine.Devices;

public sealed class AudioRingBuffer { }
```

### Mistake 6: Choosing Between First() and FirstOrDefault()

**Use `First()` when the data MUST exist** (absence indicates a bug). Use `FirstOrDefault()` when absence is a valid state.

```csharp
// ✅ CORRECT - Data must exist (validated at configuration time)
BusConfig masterBus = buses.First(bus => bus.IsMaster);

// ✅ CORRECT - Absence is valid (a device that may have been unplugged)
AudioDeviceInfo? device = devices.FirstOrDefault(candidate => candidate.Id == deviceId);
if (device is null)
{
    return DeviceStreamState.Absent;
}

// ✅ CORRECT - Custom message with a null-coalescing throw
BusConfig streamBus = buses.FirstOrDefault(bus => bus.IsMaster)
    ?? throw new InvalidOperationException("No master bus is configured");
```

**Anti-patterns to avoid:**

```csharp
// ❌ WRONG - Redundant Any() before First() (iterates twice)
if (buses.Any(bus => bus.IsMaster))
{
    BusConfig master = buses.First(bus => bus.IsMaster);
}

// ❌ WRONG - FirstOrDefault + null check + throw is First() with more code
BusConfig? master = buses.FirstOrDefault(bus => bus.IsMaster);
if (master is null)
    throw new InvalidOperationException("Not found");

// ❌ WRONG - Log AND throw (produces duplicate Sentry entries)
if (master is null)
{
    logger.LogError("No master bus");
    throw new InvalidOperationException("No master bus");
}
```

**Rule:** verify data is valid at the source, not at every access point. Use `Single()` when exactly one element is expected.

**None of this applies inside the audio path**, where LINQ is banned outright and the index was validated at graph build time.

### Mistake 7: Logging From the Audio Thread

```csharp
// ❌ WRONG - allocates, formats a string, may take a lock, inside the callback
public override void Process(ref RenderContext context)
{
    if (underrun)
    {
        logger.LogWarning("Underrun on device {DeviceId}", deviceId);
    }
}

// ✅ CORRECT - a fixed-size record into a pre-allocated ring; the pump logs it
public override void Process(ref RenderContext context)
{
    if (underrun)
    {
        dropoutLog.TryRecord(new DropoutRecord(timestamp, endpointIndex, DropoutKind.Underrun, frames, 0f));
    }
}
```

---

## Code Reuse (DRY Principle)

**NEVER duplicate code. Always look for an existing implementation before writing a new one.**

Before writing any utility method, formatting logic or helper:

1. Search `Vam.Engine/Dsp/` for an existing signal primitive
2. Search the relevant `Extensions` namespace for an existing extension
3. Search `Vam.Ui/` before adding anything to a host project

### Where shared code goes

| Shared thing | Home |
|---|---|
| Signal primitives — biquads, envelopes, delay lines, detectors | `Vam.Engine/Dsp/` |
| Span and numeric extensions used by kernels | `Vam.Engine/Dsp/Extensions/` |
| Anything drawn on screen — components, views, view models, session client | `Vam.Ui/` |
| The modifier ABI | `Vam.Modifiers.Abstractions` |

### Extension Naming Conventions

**File naming:** extensions are named after the TYPE they extend, not the domain concept.

- `SpanExtensions.cs` — extends `Span<float>` / `ReadOnlySpan<float>`
- `FloatExtensions.cs` — extends `float`
- `BusRoleExtensions.cs` — extends the `BusRole` enum
- `TimeSpanExtensions.cs` — extends `TimeSpan`

**Method naming:** methods describe what they return, including the domain concept.

- `ToLinear()` / `ToDecibels()` — gain conversion
- `ToReadableDuration()` — `TimeSpan` to a session-length string
- `ToReadableSize()` — a byte count to a recording size string
- `IsMonitor()` / `RequiresOutputDevice()` — enum predicates

```csharp
// ✅ CORRECT - one extension, used everywhere
namespace Vam.Ui.Extensions;

public static class LongExtensions
{
    public static string ToReadableSize(this long bytes)
    {
        if (bytes >= 1_000_000_000_000)
        {
            return $"{bytes / 1_000_000_000_000.0:F2} TB";
        }

        if (bytes >= 1_000_000_000)
        {
            return $"{bytes / 1_000_000_000.0:F1} GB";
        }

        return $"{bytes / 1_000_000.0:F0} MB";
    }
}

// ❌ WRONG - the same helper copied into two components
public partial class RecordView
{
    static string FormatSize(long bytes) { /* logic */ }
}

public partial class DiagnosticsView
{
    static string FormatSize(long bytes) { /* duplicated logic */ }
}
```

### The one accepted duplication, and why

`Vam.Server` is AGPL; `Vam.Client` and `Vam.WebClient` are MPL and **must not reference `Vam.Engine`** — that separation is the whole point of the client speaking gRPC. So logging and telemetry bootstrap exists twice: client-side in `Vam.Ui`, server-side in `Vam.Server`.

That is a licence boundary, not carelessness. It is the only duplication in the tree that does not need fixing, and the code says so where it occurs.

### Razor Components: No Duplicate Code

- **NEVER** put duplicate formatting methods in code-behind files
- **ALWAYS** use extension methods from the shared `Extensions` namespace
- Add `@using Vam.Ui.Extensions` to use them in `.razor` files

```razor
@using Vam.Ui.Extensions

@* ✅ CORRECT - Using the shared extension *@
<td>
    @Session.BytesWritten.ToReadableSize()
</td>

@* ❌ WRONG - Using a local static method (creates duplicates) *@
<td>
    @FormatSize(Session.BytesWritten)
</td>
```

---

## Quick Reference

```csharp
// ============================================
// 🔴 AUDIO PATH (OVERRIDES EVERYTHING BELOW)
// ============================================

// No new, LINQ, strings, boxing, closures, async, locks. See docs/audio-path.md
public abstract void Process(ref RenderContext context);
public void Process(ReadOnlySpan<float> input, Span<float> output, out int consumed, out int produced);
public void GetAllTelemetry(Span<DeviceTelemetry> destination);
if ((flags & ChannelFlags.Muted) != 0) { }   // not Enum.HasFlag

// ============================================
// 🚨 VAM OVERRIDES
// ============================================

public class DeviceSupervisor(ILogger<DeviceSupervisor> logger) { }  // primary constructors
IReadOnlyList<BusConfig> buses = registry.GetBuses();                // no var
void UpdateEstimate() { }                                            // omit private
[Inject] public required IVamSession Session { get; set; }           // Blazor injection
namespace Vam.Engine.Devices;                                        // file-scoped
// max 14 statements per method; one type per file

// ============================================
// CLASS DESIGN
// ============================================

public class DriftEstimator { }               // single responsibility
public MyClass(IFirst a, ISecond b, IThird c) // max 3-4 constructor parameters
public interface IDeviceEnumerator { }        // prefer interfaces over base classes
public static class BusRoleExtensions { }     // the only valid static class

// ============================================
// MEMBER DESIGN
// ============================================

public IReadOnlyList<BusConfig> GetBuses() => buses ?? [];  // no null collections
BusConfig master = buses.First(bus => bus.IsMaster);        // must exist
AudioDeviceInfo? d = devices.FirstOrDefault(...);           // may not exist

// ============================================
// MAINTAINABILITY
// ============================================

const double MaxCorrectionPpm = 500.0;                      // no magic numbers
if (channel.IsMuted) { }                                    // no == true
if (condition) { DoSomething(); }                           // always braces
_ => throw new ArgumentException()                          // always default
logger.LogInformation("Drift {Ppm} ppm", driftPpm);         // template, not interpolation

// ============================================
// NAMING
// ============================================

public class DriftEstimator { }               // PascalCase types and members
void Process(string channelName) { }          // camelCase parameters and locals
double estimatedRateHz;                       // camelCase fields, no underscore
public bool IsMuted { get; set; }             // Is/Has/Can/Allows/Supports
public async Task StartAsync() { }            // Async suffix
public double DriftPpm { get; }               // domain units are not abbreviations

// ============================================
// MEMBER ORDERING
// ============================================

public class MyClass
{
    // 1. Nested types  2. Static/const/readonly  3. Fields and properties
    // 4. Constructors  5. Methods (public → private)  6. Static methods
}

// ============================================
// BLAZOR COMPONENT ORDERING (.razor.cs)
// ============================================

public partial class ChannelStrip
{
    // 1. [CascadingParameter]  2. [Inject]  3. [Parameter]
    // 4. Private fields  5. Lifecycle  6. Private methods
    // await InvokeAsync(StateHasChanged) — never bare StateHasChanged()
}
```

---

## External Resources

- **[Aviva Solutions C# Coding Guidelines](https://csharpcodingguidelines.com/)** — the source of the `AV####` rules used throughout
- **[Google C# Style Guide](https://google.github.io/styleguide/csharp-style.html)** — member ordering
- **`AGENTS.md`** — the project's binding rules, including the audio-path rule this document defers to
- **`docs/audio-path.md`** — where the audio-path boundary is drawn, with worked examples
- **`razor-markup-formatting` skill** — for `.razor` markup

---

## Key Rules

**🔴 CRITICAL — the audio path outranks this document:**
- Nothing allocates, locks or waits below the line — see `docs/audio-path.md`
- AV1130, AV1135 and AV1562 are inverted there: spans not interfaces, no LINQ, `ref`/`out` required
- AV1500 bends for DSP kernels; argue it in a comment
- No logging call, no mediator call, no string, ever

**CRITICAL — VAM overrides:**
- ALWAYS use primary constructors (no underscore prefixes)
- NEVER use the `var` keyword
- ALWAYS omit the `private` modifier
- ALWAYS use `public required` for Blazor `[Inject]`
- ALWAYS use file-scoped namespaces
- Methods max 14 statements
- One type per file

**Class design:** single responsibility · max 3-4 constructor parameters · prefer interfaces over base classes (`AudioNode` and `Modifier` excepted) · static classes only for extension methods

**Member design:** properties settable in any order · no null collections · return interfaces for collections · max 3-4 parameters

**Maintainability:** no magic numbers · always braces · always a `default` case · structured log templates · never check in commented-out code · never duplicate code

**Naming:** PascalCase types and members · camelCase parameters, locals and fields · no field prefixes · no abbreviations except domain units · booleans as Is/Has/Can · verbs for methods · `Async` suffix

**Layout:** Google member ordering · `System` usings first · 4-space indent · lines under 130 characters · Blazor components have their own ordering
