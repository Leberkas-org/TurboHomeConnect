using Shouldly;
using TurboHomeConnect.OAuth;
using Xunit;

namespace TurboHomeConnect.Tests;

public sealed class OAuthCallbackReceiverTests
{
    private static HomeConnectOAuthOptions CreateOptions(
        Func<Uri, CancellationToken, Task<AuthorizationCode>>? callbackReceiver = null)
        => new()
        {
            ClientId = "test-client",
            RedirectUri = new Uri("http://localhost:5099/oauth/callback"),
            AuthorizeEndpoint = new Uri("https://auth.example.com/authorize"),
            TokenEndpoint = new Uri("https://auth.example.com/token"),
            OpenBrowser = false,
            CallbackReceiver = callbackReceiver,
        };

    [Fact]
    public async Task AuthorizeInteractiveAsync_uses_custom_CallbackReceiver_when_set()
    {
        Uri? capturedUrl = null;
        var options = CreateOptions(callbackReceiver: (url, ct) =>
        {
            capturedUrl = url;
            return Task.FromResult(new AuthorizationCode("fake-code", null));
        });
        using var flow = new HomeConnectAuthorizationCodeFlow(options);

        // ExchangeCodeAsync will fail because there is no real token endpoint, but
        // we can verify the receiver was called and the URL looks right.
        try
        {
            await flow.AuthorizeInteractiveAsync();
        }
        catch
        {
            // Expected — no real token endpoint to exchange the code with.
        }

        capturedUrl.ShouldNotBeNull();
        capturedUrl.ToString().ShouldContain("client_id=test-client");
    }

    [Fact]
    public async Task AuthorizeInteractiveAsync_rejects_state_mismatch()
    {
        var options = CreateOptions(callbackReceiver: (url, ct) =>
            Task.FromResult(new AuthorizationCode("code", "wrong-state")));
        using var flow = new HomeConnectAuthorizationCodeFlow(options);

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => flow.AuthorizeInteractiveAsync());
        ex.Message.ShouldContain("state mismatch");
    }

    [Fact]
    public async Task AuthorizeInteractiveAsync_rejects_empty_code()
    {
        var options = CreateOptions(callbackReceiver: (url, ct) =>
            Task.FromResult(new AuthorizationCode("", null)));
        using var flow = new HomeConnectAuthorizationCodeFlow(options);

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => flow.AuthorizeInteractiveAsync());
        ex.Message.ShouldContain("code");
    }

    [Fact]
    public async Task AuthorizeInteractiveAsync_cancellation_propagates_to_CallbackReceiver()
    {
        var options = CreateOptions(callbackReceiver: async (url, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new AuthorizationCode("unreachable", null);
        });
        using var flow = new HomeConnectAuthorizationCodeFlow(options);
        using var cts = new CancellationTokenSource();

        var task = flow.AuthorizeInteractiveAsync(cts.Token);
        cts.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(() => task);
    }
}
