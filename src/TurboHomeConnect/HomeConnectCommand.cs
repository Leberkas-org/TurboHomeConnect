using TurboHomeConnect.Abstractions;

namespace TurboHomeConnect;

/// <summary>
/// Base record for all commands. Auto-generates a per-instance correlation id so callers
/// never have to think about correlation — <see cref="IHomeConnectClient.RequestAsync"/>
/// pairs the command and its response internally.
/// </summary>
public abstract record HomeConnectCommand : IHomeConnectCommand
{
    public Guid CorrelationId { get; init; } = Guid.NewGuid();
}
