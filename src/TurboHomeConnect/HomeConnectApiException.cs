using System.Net;

namespace TurboHomeConnect;

public sealed class HomeConnectApiException : Exception
{
    public HomeConnectApiException(HomeConnectErrorMessage error)
        : base(error.Description ?? error.Key ?? "Home Connect request failed.", error.Exception)
    {
        StatusCode = error.StatusCode is { } code ? (HttpStatusCode)code : null;
        ErrorKey = error.Key;
        CorrelationId = error.CorrelationId;
        Error = error;
    }

    public HttpStatusCode? StatusCode { get; }
    public string? ErrorKey { get; }
    public Guid CorrelationId { get; }
    public HomeConnectErrorMessage Error { get; }
}
