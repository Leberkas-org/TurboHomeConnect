using TurboHomeConnect;
using TurboHomeConnect.BlazorSample.Components;
using TurboHomeConnect.BlazorSample.Services;
using TurboHomeConnect.OAuth;

namespace TurboHomeConnect.BlazorSample;

public static class EntryPoint
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();

        var oauthOptions = BuildOAuthOptions();
        builder.Services.AddSingleton(oauthOptions);
        builder.Services.AddSingleton<HomeConnectService>();
        builder.Services.AddHostedService<HomeConnectStartup>();

        var app = builder.Build();

        app.MapStaticAssets();
        app.UseAntiforgery();

        var callbackReceiver = app.MapHomeConnectOAuthCallback();
        app.Services.GetRequiredService<HomeConnectService>().SetCallbackReceiver(callbackReceiver);

        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        app.Run();
    }

    private static HomeConnectOAuthOptions BuildOAuthOptions()
    {
        var clientId = RequireEnv("HOMECONNECT_CLIENT_ID");
        var clientSecret = Environment.GetEnvironmentVariable("HOMECONNECT_CLIENT_SECRET");
        var tokenFile = Environment.GetEnvironmentVariable("HOMECONNECT_TOKEN_FILE")
                        ?? Path.Combine(AppContext.BaseDirectory, "token.json");
        var redirectUri = new Uri("http://localhost:5099/oauth/callback");
        var useSimulator = (Environment.GetEnvironmentVariable("HOMECONNECT_USE_SIMULATOR") ?? "1")
                           is "1" or "true" or "True" or "TRUE" or "yes";
        var openBrowser = BoolEnv("HOMECONNECT_OPEN_BROWSER", defaultValue: true);

        var baseOptions = useSimulator
            ? HomeConnectOAuthOptions.ForSimulator(clientId, redirectUri, clientSecret)
            : new HomeConnectOAuthOptions
            {
                ClientId = clientId,
                ClientSecret = clientSecret,
                RedirectUri = redirectUri,
            };

        return baseOptions with
        {
            TokenStore = new FileTokenStore(tokenFile),
            OpenBrowser = openBrowser,
        };
    }

    private static string RequireEnv(string name) =>
        Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException($"Required environment variable {name} is not set.");

    private static bool BoolEnv(string name, bool defaultValue)
    {
        var v = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrEmpty(v)
            ? defaultValue
            : v is "1" or "true" or "True" or "TRUE" or "yes";
    }
}

internal sealed class HomeConnectStartup(HomeConnectService service, ILogger<HomeConnectStartup> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await service.StartAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Home Connect startup failed.");
        }
    }
}
