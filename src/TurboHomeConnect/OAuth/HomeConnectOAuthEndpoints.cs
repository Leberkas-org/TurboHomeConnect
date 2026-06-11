using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace TurboHomeConnect.OAuth;

public static class HomeConnectOAuthEndpoints
{
    public static Func<Uri, CancellationToken, Task<AuthorizationCode>> MapHomeConnectOAuthCallback(
        this IEndpointRouteBuilder endpoints,
        string callbackPath = "/oauth/callback")
    {
        var gate = new object();
        TaskCompletionSource<AuthorizationCode>? pending = null;

        endpoints.MapGet(callbackPath, (HttpContext ctx) =>
        {
            var code = ctx.Request.Query["code"].ToString();
            var state = ctx.Request.Query["state"].ToString();
            var error = ctx.Request.Query["error"].ToString();

            if (!string.IsNullOrEmpty(error))
            {
                var desc = ctx.Request.Query["error_description"].ToString();
                lock (gate)
                {
                    pending?.TrySetException(
                        new InvalidOperationException($"OAuth authorize returned error '{error}': {desc}"));
                    pending = null;
                }

                var errorHtml = "<!doctype html><html><body>"
                    + $"<h2>Authorization failed</h2><pre>{WebUtility.HtmlEncode(error)}</pre>"
                    + "</body></html>";
                return Results.Content(errorHtml, "text/html");
            }

            lock (gate)
            {
                pending?.TrySetResult(new AuthorizationCode(code, state));
                pending = null;
            }

            var html = "<!doctype html><html><body>"
                + "<h2>Home Connect authorization complete.</h2>"
                + "<p>You can close this window.</p>"
                + "</body></html>";
            return Results.Content(html, "text/html");
        });

        return (Uri authorizeUrl, CancellationToken ct) =>
        {
            var tcs = new TaskCompletionSource<AuthorizationCode>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (gate)
            {
                pending = tcs;
            }

            ct.Register(() =>
            {
                lock (gate)
                {
                    if (ReferenceEquals(pending, tcs))
                    {
                        tcs.TrySetCanceled(ct);
                        pending = null;
                    }
                }
            });

            return tcs.Task;
        };
    }
}
