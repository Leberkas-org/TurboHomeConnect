namespace TurboHomeConnect.Abstractions;

/// <summary>
/// A response that can be matched back to the command that produced it.
/// The dispatcher in <c>HomeConnectClient</c> intercepts these and completes the
/// pending <c>TaskCompletionSource</c> created by <see cref="IHomeConnectClient.RequestAsync"/>.
/// </summary>
public interface ICorrelatedResponse : IHomeConnectMessage
{
    Guid CorrelationId { get; }
}
