using System.Threading.Channels;

namespace TurboHomeConnect.Abstractions;

public interface IHomeConnectClient : IDisposable
{
    ChannelReader<IHomeConnectMessage> Responses { get; }
    ChannelWriter<HomeConnectCommand> Commands { get; }
    bool TrySend(HomeConnectCommand command);
    ValueTask SendAsync(HomeConnectCommand command, CancellationToken cancellationToken = default);
    Task<TResponse> RequestAsync<TResponse>(
        RestCommand<TResponse> command, CancellationToken cancellationToken = default)
        where TResponse : HomeConnectResponse;
}
