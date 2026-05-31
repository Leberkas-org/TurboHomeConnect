using System.Threading.Channels;
using Akka;
using Akka.Streams.Dsl;
using TurboHomeConnect.Abstractions;

namespace TurboHomeConnect.Tests.Support;

/// <summary>
/// Test double for the protocol Flow. Commands flow into <see cref="Seen"/> for assertion,
/// and the test can push arbitrary messages out through <see cref="Emit"/>.
/// </summary>
internal sealed class StubProtocolFlow
{
    private readonly Channel<IHomeConnectCommand> _seen = Channel.CreateUnbounded<IHomeConnectCommand>();
    private readonly Channel<IHomeConnectMessage> _toEmit = Channel.CreateUnbounded<IHomeConnectMessage>();

    public ChannelReader<IHomeConnectCommand> Seen => _seen.Reader;
    public ChannelWriter<IHomeConnectMessage> Emit => _toEmit.Writer;

    public Flow<IHomeConnectCommand, IHomeConnectMessage, NotUsed> AsFlow()
    {
        var sink = ChannelSink.FromWriter(_seen.Writer, isOwner: true);
        var source = ChannelSource.FromReader(_toEmit.Reader);
        // Sink and Source are independent; PoisonPill on the owning actor stops the materializer
        // and cancels both sides, so explicit coupling isn't needed for cleanup in tests.
        return Flow.FromSinkAndSource(sink, source);
    }
}
