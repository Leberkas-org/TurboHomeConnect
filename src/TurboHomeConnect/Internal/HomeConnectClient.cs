using System.Collections.Concurrent;
using System.Threading.Channels;
using Akka.Actor;
using TurboHomeConnect.Abstractions;

namespace TurboHomeConnect.Internal;

internal sealed class HomeConnectClient : IHomeConnectClient
{
    private readonly ChannelWriter<IHomeConnectCommand> _commands;
    private readonly IActorRef _streamOwner;
    private readonly IDisposable? _ownedResources;
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<ICorrelatedResponse>> _pending = new();
    private readonly Channel<IHomeConnectMessage> _userChannel = Channel.CreateUnbounded<IHomeConnectMessage>(
        new UnboundedChannelOptions { SingleReader = false, SingleWriter = true });

    private int _disposed;

    public ChannelReader<IHomeConnectMessage> Responses => _userChannel.Reader;

    internal HomeConnectClient(
        ChannelWriter<IHomeConnectCommand> commands,
        ChannelReader<IHomeConnectMessage> streamOutput,
        IActorRef streamOwner,
        IDisposable? ownedResources = null)
    {
        _commands = commands;
        _streamOwner = streamOwner;
        _ownedResources = ownedResources;
        _ = Task.Run(() => DispatchAsync(streamOutput));
    }

    public bool TrySend(IHomeConnectCommand command) => _commands.TryWrite(command);

    public ValueTask SendAsync(IHomeConnectCommand command, CancellationToken cancellationToken = default)
        => _commands.WriteAsync(command, cancellationToken);

    public async Task<ICorrelatedResponse> RequestAsync(IHomeConnectCommand command, CancellationToken cancellationToken = default)
    {
        if (command is not IRestCommand)
        {
            throw new InvalidOperationException(
                $"{command.GetType().Name} is not a request/response command — use SendAsync and read from Responses.");
        }

        var tcs = new TaskCompletionSource<ICorrelatedResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(command.CorrelationId, tcs))
        {
            throw new InvalidOperationException(
                $"A command with CorrelationId {command.CorrelationId} is already in flight.");
        }

        using var registration = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
        try
        {
            await _commands.WriteAsync(command, cancellationToken).ConfigureAwait(false);
            return await tcs.Task.ConfigureAwait(false);
        }
        catch
        {
            _pending.TryRemove(command.CorrelationId, out _);
            throw;
        }
    }

    private async Task DispatchAsync(ChannelReader<IHomeConnectMessage> streamOutput)
    {
        Exception? failure = null;
        try
        {
            await foreach (var message in streamOutput.ReadAllAsync().ConfigureAwait(false))
            {
                if (message is ICorrelatedResponse correlated
                    && _pending.TryRemove(correlated.CorrelationId, out var tcs))
                {
                    tcs.TrySetResult(correlated);
                    continue;
                }

                _userChannel.Writer.TryWrite(message);
            }
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            _userChannel.Writer.TryComplete(failure);
            FailAllPending(failure ?? new InvalidOperationException(
                "Home Connect stream completed before response arrived."));
        }
    }

    private void FailAllPending(Exception exception)
    {
        foreach (var (id, tcs) in _pending)
        {
            if (_pending.TryRemove(id, out _))
            {
                tcs.TrySetException(exception);
            }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        _commands.TryComplete();
        _streamOwner.Tell(PoisonPill.Instance);
        _ownedResources?.Dispose();
    }
}
