using System.Threading.Channels;
using Akka.Actor;
using TurboHomeConnect;
using TurboHomeConnect.Abstractions;
using TurboHomeConnect.Internal;

namespace TurboHomeConnect.Tests.Support;

/// <summary>
/// Builds a <see cref="IHomeConnectClient"/> wired up to a <see cref="StubProtocolFlow"/>,
/// exposing both for assertion in tests.
/// </summary>
internal sealed class PatternHarness : IDisposable
{
    public PatternHarness(ActorSystem system, int commandCapacity = 8)
    {
        Stub = new StubProtocolFlow();

        var commands = Channel.CreateBounded<HomeConnectCommand>(commandCapacity);
        var responses = Channel.CreateUnbounded<IHomeConnectMessage>();

        var owner = system.ActorOf(StreamOwnerActor.Props(commands.Reader, responses.Writer, Stub.AsFlow()));
        Client = new HomeConnectClient(commands.Writer, responses.Reader, owner);
    }

    public StubProtocolFlow Stub { get; }
    public IHomeConnectClient Client { get; }

    public void Dispose() => Client.Dispose();
}
