using Vam.Ui.Extensions;
using Vam.WebClient;
using Vam.WebClient.Components;

// A console in a browser. It owns no audio and no devices: it connects to an engine over gRPC and
// draws what it says, exactly as the desktop client does, over exactly the same code.

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();

builder.Services.AddVamUi<WebPlatformServices>(options =>
    options.Address = builder.Configuration["Vam:Engine"] ?? options.Address);

WebApplication app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/");
    app.UseHsts();
}

app.UseAntiforgery();

// MapStaticAssets rather than UseStaticFiles: the console's stylesheet and its meter module are
// static web assets of the Vam.Ui library, and this is what serves them under _content.
app.MapStaticAssets();

app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();
