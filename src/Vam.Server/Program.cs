using Vam.Server.Engine;
using Vam.Server.Logging;
using Vam.Server.Services;

// The process that runs a meeting. It owns the devices, the graph, the clock and the recording, and
// it keeps running whether or not anything is looking at it - G1. A console closing, crashing, or
// being killed by somebody who thought it had hung does not take the session with it.

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// One log stream feeding a rotated file, the in-memory tail the diagnostics view reads, and Sentry
// when a key is configured. The key comes from configuration or the environment and never from
// source. I4.
EngineLogging.Configure(
    builder.Logging,
    builder.Configuration["Vam:LogDirectory"],
    builder.Configuration["Vam:SentryDsn"]);

builder.Services.AddGrpc();

builder.Services.AddSingleton(new EngineOptions());
builder.Services.AddSingleton<VamEngine>();

WebApplication app = builder.Build();

app.MapGrpcService<MixerService>();

app.MapGet("/", () =>
    "VAM engine. This endpoint speaks gRPC; see src/Vam.Protocol/Protos/vam.proto for what it says.");

VamEngine engine = app.Services.GetRequiredService<VamEngine>();

// Started before the transport, so a console connecting immediately finds a console rather than an
// empty one being built underneath it.
engine.Start();

app.Lifetime.ApplicationStopping.Register(engine.Stop);

app.Run();
