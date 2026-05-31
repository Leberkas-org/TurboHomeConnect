namespace TurboHomeConnect.OAuth;

/// <summary>Snapshot of an OAuth token set. <see cref="ExpiresAtUtc"/> is computed locally
/// from the server's <c>expires_in</c> at the time the token was received.</summary>
public sealed record PersistedToken(
    string AccessToken,
    string? RefreshToken,
    DateTimeOffset ExpiresAtUtc);
