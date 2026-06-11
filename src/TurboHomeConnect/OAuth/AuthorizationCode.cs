namespace TurboHomeConnect.OAuth;

public sealed record AuthorizationCode(string Code, string? State);
