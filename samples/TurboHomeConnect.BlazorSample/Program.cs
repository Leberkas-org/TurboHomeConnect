using TurboHomeConnect.BlazorSample.Components;
using TurboHomeConnect.BlazorSample.Services;

namespace TurboHomeConnect.BlazorSample;

// Wrapped in a namespace and named anything-but-Program so it doesn't shadow
// TurboHomeConnect.Model.Program in the rest of this project's C#/Razor.
public static class EntryPoint
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();

        builder.Services.AddSingleton<HomeConnectService>();

        var app = builder.Build();

        // MapStaticAssets serves both wwwroot AND Blazor's _framework/* (the bootstrap JS).
        app.MapStaticAssets();
        app.UseAntiforgery();

        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        _ = app.Services.GetRequiredService<HomeConnectService>().StartAsync();

        app.Run();
    }
}
