using System.Threading.Channels;
using Akka.Actor;
using Akka.Streams;
using TurboHomeConnect.Abstractions;
using TurboHomeConnect.Internal;

namespace TurboHomeConnect;

/// <summary>
/// Fluent entry point. Configure the endpoint and token source, then call
/// <see cref="Build"/> with an <see cref="ActorSystem"/> to materialize the client.
/// </summary>
public sealed class HomeConnectBuilder
{
    private Uri? _baseAddress;
    private Func<CancellationToken, Task<string>>? _tokenProvider;
    private HttpClient? _externalHttpClient;
    private Action<HttpClient>? _configureHttpClient;
    private int _commandCapacity = 64;
    private int _restParallelism = 4;
    private int _maxConcurrentSubscriptions = 4;
    private TimeSpan _restTimeout = TimeSpan.FromSeconds(30);
    // Home Connect documents a ~50 req / 60 s rate limit (the simulator enforces it strictly,
    // production quotas vary per app/tier). Match it by default — consumers can override.
    private int _rateLimitElements = 50;
    private TimeSpan _rateLimitPer = TimeSpan.FromSeconds(60);
    private RestartSettings _sseRestart = RestartSettings.Create(
        minBackoff: TimeSpan.FromSeconds(1),
        maxBackoff: TimeSpan.FromMinutes(1),
        randomFactor: 0.2);

    public static HomeConnectBuilder Create() => new();

    /// <summary>Use production endpoints (<c>https://api.home-connect.com</c>).</summary>
    public HomeConnectBuilder UseProduction()
    {
        _baseAddress = HomeConnectEndpoints.Production;
        return this;
    }

    /// <summary>Use the developer simulator endpoints (<c>https://simulator.home-connect.com</c>).</summary>
    public HomeConnectBuilder UseSimulator()
    {
        _baseAddress = HomeConnectEndpoints.Simulator;
        return this;
    }

    /// <summary>Override the base address explicitly (e.g. for a test proxy).</summary>
    public HomeConnectBuilder BaseAddress(Uri uri)
    {
        _baseAddress = uri;
        return this;
    }

    /// <summary>
    /// Provide an access token on demand. The provider is invoked for every REST request and
    /// for every SSE (re)connect, so it's the right place to refresh expiring tokens.
    /// </summary>
    public HomeConnectBuilder TokenProvider(Func<CancellationToken, Task<string>> provider)
    {
        _tokenProvider = provider;
        return this;
    }

    /// <summary>Convenience overload for a static token (development only — tokens expire).</summary>
    public HomeConnectBuilder StaticAccessToken(string token)
    {
        _tokenProvider = _ => Task.FromResult(token);
        return this;
    }

    /// <summary>
    /// Bring your own configured <see cref="HttpClient"/>. When set, the builder uses it as-is and
    /// <see cref="ConfigureHttpClient"/> / <see cref="BaseAddress"/> are ignored. The caller owns
    /// the client's lifetime.
    /// </summary>
    public HomeConnectBuilder UseHttpClient(HttpClient client)
    {
        _externalHttpClient = client;
        return this;
    }

    /// <summary>
    /// Customize the internal <see cref="HttpClient"/> (timeouts, default headers, etc.).
    /// Ignored when <see cref="UseHttpClient"/> is set.
    /// </summary>
    public HomeConnectBuilder ConfigureHttpClient(Action<HttpClient> configure)
    {
        _configureHttpClient = configure;
        return this;
    }

    public HomeConnectBuilder CommandCapacity(int capacity)
    {
        _commandCapacity = capacity;
        return this;
    }

    public HomeConnectBuilder RestParallelism(int parallelism)
    {
        _restParallelism = parallelism;
        return this;
    }

    public HomeConnectBuilder MaxConcurrentSubscriptions(int max)
    {
        _maxConcurrentSubscriptions = max;
        return this;
    }

    public HomeConnectBuilder SseRestartSettings(RestartSettings settings)
    {
        _sseRestart = settings;
        return this;
    }

    /// <summary>
    /// Per-call timeout for REST requests. SSE subscriptions ignore this — they're long-running by
    /// design and rely on <see cref="SseRestartSettings"/> for failure handling. Default: 30s.
    /// </summary>
    public HomeConnectBuilder RestTimeout(TimeSpan timeout)
    {
        _restTimeout = timeout;
        return this;
    }

    /// <summary>
    /// Token-bucket rate limit applied to the REST branch of the protocol Flow. Bursts get
    /// shaped (queued + delayed) — they don't fail. Default: <c>50 requests / 60 seconds</c>,
    /// matching the Home Connect simulator's documented limit. Override for production tiers.
    /// </summary>
    public HomeConnectBuilder RateLimit(int requests, TimeSpan per)
    {
        if (requests <= 0) throw new ArgumentOutOfRangeException(nameof(requests), "Must be positive.");
        if (per <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(per), "Must be positive.");
        _rateLimitElements = requests;
        _rateLimitPer = per;
        return this;
    }

    /// <summary>Disable the built-in REST rate limit. Equivalent to <c>RateLimit(int.MaxValue, 1s)</c>.</summary>
    public HomeConnectBuilder NoRateLimit()
    {
        _rateLimitElements = int.MaxValue;
        _rateLimitPer = TimeSpan.FromSeconds(1);
        return this;
    }

    public IHomeConnectClient Build(ActorSystem system)
    {
        ArgumentNullException.ThrowIfNull(system);
        if (_tokenProvider is null)
        {
            throw new InvalidOperationException(
                "TokenProvider is required. Call .TokenProvider(...) or .StaticAccessToken(...).");
        }
        if (_externalHttpClient is null && _baseAddress is null)
        {
            throw new InvalidOperationException(
                "BaseAddress is required. Call .UseProduction(), .UseSimulator(), .BaseAddress(...) or .UseHttpClient(...).");
        }

        // The builder owns the client only if it created it. UseHttpClient(...) hands ownership
        // to the caller, so we mustn't dispose it from HomeConnectClient.Dispose().
        HttpClient http;
        IDisposable? ownedHttp = null;
        if (_externalHttpClient is not null)
        {
            http = _externalHttpClient;
        }
        else
        {
            // Infinite client timeout is required for SSE — a long idle period between events
            // would otherwise be killed by HttpClient.Timeout (default 100s). REST calls get
            // their own per-request CancellationTokenSource via _restTimeout.
            http = new HttpClient
            {
                BaseAddress = _baseAddress,
                Timeout = Timeout.InfiniteTimeSpan,
            };
            _configureHttpClient?.Invoke(http);
            ownedHttp = http;
        }

        var flow = HomeConnectFlow.Build(
            http,
            _tokenProvider,
            _restParallelism,
            _maxConcurrentSubscriptions,
            _restTimeout,
            _rateLimitElements,
            _rateLimitPer,
            _sseRestart);

        var commands = Channel.CreateBounded<HomeConnectCommand>(
            new BoundedChannelOptions(_commandCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
            });
        var responses = Channel.CreateUnbounded<IHomeConnectMessage>(
            new UnboundedChannelOptions
            {
                SingleReader = false,
                SingleWriter = true,
            });

        var owner = system.ActorOf(StreamOwnerActor.Props(commands.Reader, responses.Writer, flow));
        return new HomeConnectClient(commands.Writer, responses.Reader, owner, ownedHttp);
    }
}
