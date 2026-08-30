using Vam.Server.Engine;
using Vam.Server.Services;

// The process that runs a meeting. It owns the devices, the graph, the clock and the recording, and
// it keeps running whether or not anything is looking at it - G1. A console closing, crashing, or
// being killed by somebody who thought it had hung does not take the session with it.

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

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
