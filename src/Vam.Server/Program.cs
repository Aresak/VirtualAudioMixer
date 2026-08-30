using Vam.Server.Engine;
using Vam.Server.Logging;
using Vam.Server.Services;

// The process that runs a meeting. It owns the devices, the graph, the clock and the recording, and
// it keeps running whether or not anything is looking at it - G1. A console closing, crashing, or
// being killed by somebody who thought it had hung does not take the session with it.

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// gRPC needs HTTP/2, and on a plain http:// endpoint Kestrel will not negotiate up to it: it answers
// HTTP_1_1_REQUIRED and the console spends the meeting reconnecting. Said here rather than left to
// configuration, because an engine that cannot be talked to is not an engine.
//
// Cleartext is deliberate for the local case. The console and the engine are usually the same
// machine or the same room, and a self-signed certificate an operator has to trust before a meeting
// is a worse problem than the one it solves. A deployment that crosses a network configures HTTPS.
builder.WebHost.ConfigureKestrel(kestrel =>
    kestrel.ConfigureEndpointDefaults(endpoint =>
        endpoint.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2));

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

// Answers a curious HTTP/2 client. A browser will not reach it, because a browser will not speak
// cleartext HTTP/2, and that is the correct thing for it to discover about a gRPC endpoint.
app.MapGet("/", () =>
    "VAM engine. This endpoint speaks gRPC; see src/Vam.Protocol/Protos/vam.proto for what it says.");

VamEngine engine = app.Services.GetRequiredService<VamEngine>();

// Started before the transport, so a console connecting immediately finds a console rather than an
// empty one being built underneath it.
engine.Start();

app.Lifetime.ApplicationStopping.Register(engine.Stop);

app.Run();
