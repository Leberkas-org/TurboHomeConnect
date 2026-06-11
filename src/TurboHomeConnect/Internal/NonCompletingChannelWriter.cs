using System.Threading.Channels;

namespace TurboHomeConnect.Internal;

internal sealed class NonCompletingChannelWriter<T>(ChannelWriter<T> inner) : ChannelWriter<T>
{
    public override bool TryWrite(T item) => inner.TryWrite(item);

    public override ValueTask<bool> WaitToWriteAsync(CancellationToken cancellationToken = default)
        => inner.WaitToWriteAsync(cancellationToken);

    public override ValueTask WriteAsync(T item, CancellationToken cancellationToken = default)
        => inner.WriteAsync(item, cancellationToken);

    public override bool TryComplete(Exception? error = null)
        => throw new InvalidOperationException(
            "The command channel is owned by the client. Dispose the IHomeConnectClient instead of completing its writer.");
}
