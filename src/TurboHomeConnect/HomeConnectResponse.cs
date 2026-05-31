using TurboHomeConnect.Abstractions;

namespace TurboHomeConnect;

/// <summary>Base record for correlated REST responses.</summary>
public abstract record HomeConnectResponse(Guid CorrelationId) : ICorrelatedResponse;

/// <summary>
/// Surfaced through <see cref="IHomeConnectClient.RequestAsync"/> when a REST call fails,
/// and through <see cref="IHomeConnectClient.Responses"/> for SSE-side or transport errors.
/// </summary>
/// <param name="CorrelationId">
/// Id of the originating command, or <see cref="Guid.Empty"/> if the error is not tied to one
/// (e.g. an SSE stream disconnect).
/// </param>
/// <param name="StatusCode">
/// HTTP status returned by the server, or <c>null</c> if the failure happened before a response.
/// </param>
/// <param name="Key">Home Connect error key (e.g. <c>SDK.Error.UnsupportedOperation</c>) when available.</param>
/// <param name="Description">Human-readable error description.</param>
/// <param name="Exception">Underlying exception, if the failure was an unhandled throw.</param>
public sealed record HomeConnectErrorMessage(
    Guid CorrelationId,
    int? StatusCode,
    string? Key,
    string? Description,
    Exception? Exception = null) : HomeConnectResponse(CorrelationId);
