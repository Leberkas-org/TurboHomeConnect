using System.Threading.Channels;
using Akka;
using Akka.Actor;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHomeConnect.Abstractions;

namespace TurboHomeConnect.Internal;

internal sealed class StreamOwnerActor : ReceiveActor
{
    private readonly ILoggingAdapter _log = Context.GetLogger();

    public StreamOwnerActor(
        ChannelReader<HomeConnectCommand> commandReader,
        ChannelWriter<IHomeConnectMessage> responseWriter,
        Flow<HomeConnectCommand, IHomeConnectMessage, NotUsed> flow)
    {
        ChannelSource.FromReader(commandReader)
            .Via(flow)
            .WatchTermination((_, task) => task)
            .To(ChannelSink.FromWriter(responseWriter, isOwner: true))
            .Run(Context.Materializer())
            .PipeTo(Self,
                success: _ => StreamCompleted.Instance,
                failure: ex => new StreamFailed(ex));

        Receive<StreamCompleted>(_ => Context.Stop(Self));
        Receive<StreamFailed>(failed =>
        {
            _log.Error(failed.Cause, "Home Connect protocol stream failed.");
            Context.Stop(Self);
        });
    }

    public static Props Props(
        ChannelReader<HomeConnectCommand> commandReader,
        ChannelWriter<IHomeConnectMessage> responseWriter,
        Flow<HomeConnectCommand, IHomeConnectMessage, NotUsed> flow)
        => Akka.Actor.Props.Create(() => new StreamOwnerActor(commandReader, responseWriter, flow));

    private sealed record StreamCompleted
    {
        public static readonly StreamCompleted Instance = new();
    }

    private sealed record StreamFailed(Exception Cause);
}
