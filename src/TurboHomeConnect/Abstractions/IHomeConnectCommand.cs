namespace TurboHomeConnect.Abstractions;

/// <summary>
/// Marker for any command pushed into the protocol Flow.
/// Every command carries a <see cref="CorrelationId"/> so that REST-style commands
/// can be awaited via <see cref="IHomeConnectClient.RequestAsync"/>.
/// </summary>
public interface IHomeConnectCommand
{
    Guid CorrelationId { get; }
}
