namespace TurboHomeConnect.OAuth;

public sealed record HomeConnectOAuthOptions
{
    public required string ClientId { get; init; }

    /// <summary>Required for production accounts; optional when the developer portal app has no secret.</summary>
    public string? ClientSecret { get; init; }

    /// <summary>The redirect URI registered for this app on the developer portal.</summary>
    public required Uri RedirectUri { get; init; }

    public Uri AuthorizeEndpoint { get; init; } = HomeConnectEndpoints.ProductionAuthorizeEndpoint;
    public Uri TokenEndpoint { get; init; } = HomeConnectEndpoints.ProductionTokenEndpoint;

    /// <summary>Scopes to request. Default covers identify + monitor + control + settings.</summary>
    public IReadOnlyList<string> Scopes { get; init; } =
    [
        "IdentifyAppliance",
        "Monitor",
        "Settings",
        "Control",
    ];

    /// <summary>How early before expiry to proactively refresh.</summary>
    public TimeSpan RefreshSkew { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Where to persist the token set. Defaults to in-memory (lost on process exit).</summary>
    public IPersistedTokenStore? TokenStore { get; init; }

    /// <summary>
    /// Bind the OAuth callback <see cref="System.Net.HttpListener"/> to <c>+</c> (all interfaces) instead of the
    /// host from <see cref="RedirectUri"/>. Required when running in a container where the browser is
    /// outside and Docker's port-forward routes traffic to the container interface — not loopback.
    /// </summary>
    public bool BindToAllInterfaces { get; init; }

    /// <summary>
    /// Try to launch the system browser for the authorize URL. Set to <c>false</c> for headless
    /// environments — the URL is always printed to stdout regardless.
    /// </summary>
    public bool OpenBrowser { get; init; } = true;

    /// <summary>
    /// Optional custom callback receiver. When set, the built-in <see cref="System.Net.HttpListener"/>
    /// is bypassed and the delegate is called instead to wait for the OAuth callback.
    /// This allows ASP.NET apps to reuse Kestrel for the callback endpoint.
    /// </summary>
    public Func<Uri, CancellationToken, Task<AuthorizationCode>>? CallbackReceiver { get; init; }

    /// <summary>
    /// Fired when the authorize URL is ready, just before the flow waits for the callback.
    /// Use this in UI hosts (e.g. Blazor) to navigate the user to the authorize page —
    /// stdout and <see cref="OpenBrowser"/> only work when the user has access to the
    /// server's console or desktop.
    /// </summary>
    public Action<Uri>? OnAuthorizeUrlReady { get; init; }

    /// <summary>Use the simulator's auth/token endpoints instead of production.</summary>
    public static HomeConnectOAuthOptions ForSimulator(string clientId, Uri redirectUri, string? clientSecret = null) =>
        new()
        {
            ClientId = clientId,
            ClientSecret = clientSecret,
            RedirectUri = redirectUri,
            AuthorizeEndpoint = HomeConnectEndpoints.SimulatorAuthorizeEndpoint,
            TokenEndpoint = HomeConnectEndpoints.SimulatorTokenEndpoint,
        };
}
