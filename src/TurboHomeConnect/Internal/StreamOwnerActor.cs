using System.Threading.Channels;
using Akka;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHomeConnect.Abstractions;

namespace TurboHomeConnect.Internal;

/// <summary>
/// Owns the materialized stream. Actor start = stream start; actor stop = stream stop.
/// No <c>Receive</c> handlers — the stream pulls/pushes through channels, the actor is
/// purely a lifecycle anchor.
/// </summary>
internal sealed class StreamOwnerActor : ReceiveActor
{
    public StreamOwnerActor(
        ChannelReader<IHomeConnectCommand> commandReader,
        ChannelWriter<IHomeConnectMessage> responseWriter,
        Flow<IHomeConnectCommand, IHomeConnectMessage, NotUsed> flow)
    {
        ChannelSource.FromReader(commandReader)
            .Via(flow)
            .RunWith(
                ChannelSink.FromWriter(responseWriter, isOwner: true),
                Context.Materializer());
    }

    public static Props Props(
        ChannelReader<IHomeConnectCommand> commandReader,
        ChannelWriter<IHomeConnectMessage> responseWriter,
        Flow<IHomeConnectCommand, IHomeConnectMessage, NotUsed> flow)
        => Akka.Actor.Props.Create(() => new StreamOwnerActor(commandReader, responseWriter, flow));
}
