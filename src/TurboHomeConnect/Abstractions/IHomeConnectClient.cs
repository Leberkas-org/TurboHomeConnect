using System.Threading.Channels;

namespace TurboHomeConnect.Abstractions;

/// <summary>
/// User-facing client surface. Read uncorrelated messages (SSE events, connection notifications,
/// errors that arrive without a matching command) from <see cref="Responses"/>. Push commands via
/// <see cref="SendAsync"/> or <see cref="TrySend"/>; await REST-style commands via
/// <see cref="RequestAsync"/>.
/// </summary>
public interface IHomeConnectClient : IDisposable
{
    ChannelReader<IHomeConnectMessage> Responses { get; }

    bool TrySend(IHomeConnectCommand command);

    ValueTask SendAsync(IHomeConnectCommand command, CancellationToken cancellationToken = default);

    Task<ICorrelatedResponse> RequestAsync(IHomeConnectCommand command, CancellationToken cancellationToken = default);
}
