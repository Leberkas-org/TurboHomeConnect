using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Serialization;

namespace TurboHomeConnect.OAuth;

/// <summary>
/// Drives the Home Connect Authorization Code OAuth grant interactively and refreshes tokens
/// on demand. Plug <see cref="GetAccessTokenAsync"/> into
/// <see cref="HomeConnectBuilder.TokenProvider(Func{CancellationToken, Task{string}})"/>.
/// </summary>
public sealed class HomeConnectAuthorizationCodeFlow : IDisposable
{
    private readonly HomeConnectOAuthOptions _options;
    private readonly IPersistedTokenStore _store;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    public HomeConnectAuthorizationCodeFlow(HomeConnectOAuthOptions options, HttpClient? httpClient = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _store = options.TokenStore ?? new InMemoryTokenStore();
        _http = httpClient ?? new HttpClient();
        _ownsHttp = httpClient is null;
    }

    /// <summary>
    /// Returns a valid access token, refreshing when within <see cref="HomeConnectOAuthOptions.RefreshSkew"/>
    /// of expiry. If no tokens are cached, throws — call <see cref="AuthorizeInteractiveAsync"/> first.
    /// </summary>
    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        var token = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (token is not null && DateTimeOffset.UtcNow + _options.RefreshSkew < token.ExpiresAtUtc)
        {
            return token.AccessToken;
        }

        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Double-check after acquiring — another caller may have refreshed.
            token = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
            if (token is not null && DateTimeOffset.UtcNow + _options.RefreshSkew < token.ExpiresAtUtc)
            {
                return token.AccessToken;
            }

            if (token?.RefreshToken is null)
            {
                throw new InvalidOperationException(
                    "No refresh token available. Run AuthorizeInteractiveAsync() to perform the initial grant.");
            }

            var refreshed = await RefreshAsync(token.RefreshToken, cancellationToken).ConfigureAwait(false);
            await _store.SaveAsync(refreshed, cancellationToken).ConfigureAwait(false);
            return refreshed.AccessToken;
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    /// <summary>
    /// Runs the interactive Authorization Code grant: launches the system browser pointed at the
    /// Home Connect authorize endpoint, listens on <see cref="HomeConnectOAuthOptions.RedirectUri"/>
    /// for the callback, and exchanges the code for tokens. The redirect URI <b>must</b> be a
    /// localhost http URL — otherwise the embedded <see cref="HttpListener"/> can't bind it.
    /// </summary>
    public async Task<PersistedToken> AuthorizeInteractiveAsync(CancellationToken cancellationToken = default)
    {
        var receiver = _options.CallbackReceiver;

        if (receiver is null
            && !_options.BindToAllInterfaces
            && !string.Equals(_options.RedirectUri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(_options.RedirectUri.Host, "127.0.0.1", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "AuthorizeInteractiveAsync requires a localhost RedirectUri for the embedded HTTP listener, "
                + "or BindToAllInterfaces=true when running behind a port-forward (e.g. Docker).");
        }

        var state = Guid.NewGuid().ToString("N");
        var authorizeUrl = BuildAuthorizeUrl(state);

        Console.WriteLine($"Open this URL to authorize:\n  {authorizeUrl}");
        if (_options.OpenBrowser)
        {
            TryOpenBrowser(authorizeUrl);
        }

        _options.OnAuthorizeUrlReady?.Invoke(authorizeUrl);

        var result = receiver is not null
            ? await receiver(authorizeUrl, cancellationToken).ConfigureAwait(false)
            : await ListenForCallbackAsync(cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrEmpty(result.State) && !string.Equals(result.State, state, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("OAuth state mismatch — possible CSRF.");
        }
        if (string.IsNullOrEmpty(result.Code))
        {
            throw new InvalidOperationException("OAuth callback did not include a code parameter.");
        }

        var token = await ExchangeCodeAsync(result.Code, cancellationToken).ConfigureAwait(false);
        await _store.SaveAsync(token, cancellationToken).ConfigureAwait(false);
        return token;
    }

    private async Task<AuthorizationCode> ListenForCallbackAsync(CancellationToken cancellationToken)
    {
        var listenerHost = _options.BindToAllInterfaces ? "+" : _options.RedirectUri.Host;
        var listenerPrefix = $"{_options.RedirectUri.Scheme}://{listenerHost}:{_options.RedirectUri.Port}{_options.RedirectUri.AbsolutePath}";
        if (!listenerPrefix.EndsWith('/'))
        {
            listenerPrefix += "/";
        }

        using var listener = new HttpListener();
        listener.Prefixes.Add(listenerPrefix);
        listener.Start();

        try
        {
            var contextTask = listener.GetContextAsync();
            var completed = await Task.WhenAny(contextTask, Task.Delay(Timeout.Infinite, cancellationToken))
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            var context = await contextTask.ConfigureAwait(false);
            var query = context.Request.QueryString;
            var responseState = query["state"];
            var code = query["code"];
            var error = query["error"];

            await WriteCallbackResponseAsync(context, error).ConfigureAwait(false);

            if (!string.IsNullOrEmpty(error))
            {
                throw new InvalidOperationException($"OAuth authorize returned error '{error}': {query["error_description"]}");
            }

            return new AuthorizationCode(code ?? string.Empty, responseState);
        }
        finally
        {
            listener.Stop();
        }
    }

    private Uri BuildAuthorizeUrl(string state)
    {
        var query = new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = _options.ClientId,
            ["redirect_uri"] = _options.RedirectUri.ToString(),
            ["scope"] = string.Join(' ', _options.Scopes),
            ["state"] = state,
        };
        var builder = new StringBuilder(_options.AuthorizeEndpoint.ToString()).Append('?');
        var first = true;
        foreach (var (k, v) in query)
        {
            if (!first) builder.Append('&');
            builder.Append(WebUtility.UrlEncode(k)).Append('=').Append(WebUtility.UrlEncode(v));
            first = false;
        }
        return new Uri(builder.ToString());
    }

    private async Task<PersistedToken> ExchangeCodeAsync(string code, CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["client_id"] = _options.ClientId,
            ["redirect_uri"] = _options.RedirectUri.ToString(),
        };
        if (!string.IsNullOrEmpty(_options.ClientSecret))
        {
            form["client_secret"] = _options.ClientSecret;
        }
        return await PostTokenAsync(form, cancellationToken).ConfigureAwait(false);
    }

    private async Task<PersistedToken> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
        };
        if (!string.IsNullOrEmpty(_options.ClientSecret))
        {
            form["client_secret"] = _options.ClientSecret;
        }
        return await PostTokenAsync(form, cancellationToken).ConfigureAwait(false);
    }

    private async Task<PersistedToken> PostTokenAsync(Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _options.TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(form),
        };
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException(
                $"Token endpoint returned {(int)response.StatusCode} {response.ReasonPhrase}: {body}");
        }

        var payload = await response.Content
            .ReadFromJsonAsync<TokenResponse>(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("Token endpoint returned an empty body.");

        var expires = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(payload.ExpiresIn);
        return new PersistedToken(payload.AccessToken, payload.RefreshToken, expires);
    }

    private static void TryOpenBrowser(Uri url)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo(url.ToString()) { UseShellExecute = true });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", url.ToString());
            }
            else
            {
                Process.Start("xdg-open", url.ToString());
            }
        }
        catch
        {
            // No GUI / no xdg-open in container — the URL is already printed for manual launch.
        }
    }

    private static async Task WriteCallbackResponseAsync(HttpListenerContext context, string? error)
    {
        var body = string.IsNullOrEmpty(error)
            ? "<!doctype html><html><body><h2>Home Connect authorization complete.</h2>"
              + "<p>You can close this window.</p></body></html>"
            : $"<!doctype html><html><body><h2>Authorization failed</h2><pre>{WebUtility.HtmlEncode(error)}</pre></body></html>";
        var bytes = Encoding.UTF8.GetBytes(body);
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
        context.Response.Close();
    }

    public void Dispose()
    {
        _refreshGate.Dispose();
        if (_ownsHttp)
        {
            _http.Dispose();
        }
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn,
        [property: JsonPropertyName("token_type")] string? TokenType,
        [property: JsonPropertyName("scope")] string? Scope,
        [property: JsonPropertyName("id_token")] string? IdToken);
}
