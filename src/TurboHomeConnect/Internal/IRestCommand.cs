using TurboHomeConnect.Abstractions;

namespace TurboHomeConnect.Internal;

/// <summary>
/// Internal contract every REST command implements. The dispatcher uses
/// <see cref="BuildRequest"/> to construct the wire request and <see cref="MapResponseAsync"/>
/// to turn a successful HTTP response into a typed correlated response.
/// </summary>
internal interface IRestCommand : IHomeConnectCommand
{
    HttpRequestMessage BuildRequest();

    /// <summary>
    /// Called with a successful HTTP response (2xx). Reads the body as needed and returns
    /// the typed correlated response. The dispatcher handles 4xx/5xx and transport exceptions
    /// and emits <see cref="HomeConnectErrorMessage"/> instead.
    /// </summary>
    Task<ICorrelatedResponse> MapResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken);
}

/// <summary>
/// Marker for SSE-style commands — they don't have a single response; they open a long-lived
/// stream that emits many <see cref="IHomeConnectMessage"/> values.
/// </summary>
internal interface ISubscribeCommand : IHomeConnectCommand
{
    HttpRequestMessage BuildRequest();
}
