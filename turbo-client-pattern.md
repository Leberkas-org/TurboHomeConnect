# Turbo Client Pattern

Reusable architecture for channel-based streaming clients in .NET with Akka.Streams.

Used by: TurboOpcUa, TurboEISCP, future Turbo* libraries.

---

## Core Concepts

```
Builder  →  Build(ActorSystem)  →  IOpcUaClient
                                      ├── .Responses   (ChannelReader<IOpcUaResponse>)
                                      ├── .SendAsync()  (wraps ChannelWriter<IOpcUaCommand>)
                                      └── owns StreamOwnerActor (lifecycle)
```

### Three Pieces

| Component | Responsibility |
|-----------|---------------|
| **Builder** | Configures the protocol. Builds the Akka.Streams `Flow`. Entry point: `Build(ActorSystem)` → returns client. |
| **Client** (`IOpcUaClient` / `OpcUaClient`) | User-facing API. Exposes `ChannelReader` for reading, `SendAsync` / `TrySend` for writing. Holds the `StreamOwnerActor` ref and stops it on dispose. |
| **StreamOwnerActor** | Internal. Materializes `ChannelReader → Flow → ChannelWriter` inside an actor. Actor lifecycle = stream lifecycle. No `Receive` handlers. |

### Channel Layout

```
 User code                  Client dispatch           StreamOwnerActor
 ─────────                  ───────────────           ────────────────
 client.SendAsync(cmd)
       │
       ▼
 ChannelWriter<Command> ──────────────────────────► ChannelReader<Command>
                                                         │
                                                         ▼
                                                    Flow<Command, Response>
                                                         │
                                                         ▼
                            ChannelReader<Response> ◄── ChannelWriter<Response>
                                  │
                          ┌───────┴───────┐
                          │  correlated?  │
                          └───┬───────┬───┘
                          yes │       │ no
                              ▼       ▼
                       pending TCS   user channel
                              │       │
                              ▼       ▼
                    RequestAsync()   client.Responses.ReadAllAsync()
```

Three channels total. The command channel goes straight through. The response path has a dispatch layer: correlated responses (method calls, reads) go to pending `RequestAsync` callers, everything else (subscriptions, events) flows to the user's `Responses` channel.

---

## Client Interface

```csharp
public interface IOpcUaClient : IDisposable
{
    ChannelReader<IOpcUaResponse> Responses { get; }
    bool TrySend(IOpcUaCommand command);
    ValueTask SendAsync(IOpcUaCommand command, CancellationToken ct = default);
    Task<ICorrelatedResponse> RequestAsync(IOpcUaCommand command, CancellationToken ct = default);
}
```

The interface is protocol-specific. No shared cross-protocol base — the pattern is the shape, not a type hierarchy. Each protocol defines its own interface and implementation.

- `SendAsync` = fire-and-forget. For subscriptions, one-way commands. Response arrives on `Responses`.
- `RequestAsync` = send + wait for the correlated response. For method calls, reads. The correlated response is intercepted before it hits `Responses`, so the dispatch loop never sees it. Protocols with typed commands may strengthen this to a generic `RequestAsync<TResponse>(RestCommand<TResponse>)` — TurboHomeConnect does this; the shape (send + await correlated response) is unchanged.

### Correlation

Protocols that support request-response correlation (e.g. OPC UA with `CorrelationId`) can implement `RequestAsync` using a dispatch layer between the stream output and the user-facing `Responses` channel:

```
Stream → internal channel → Dispatch task → correlated? → pending TCS
                                           → uncorrelated → user Responses channel
```

The dispatch task reads every response from the stream. If it's a correlated response with a pending `RequestAsync`, it completes the `TaskCompletionSource`. Everything else forwards to the user's `Responses` channel.

### Implementation

```csharp
internal sealed class OpcUaClient : IOpcUaClient
{
    private readonly ChannelWriter<IOpcUaCommand> _commands;
    private readonly IActorRef _streamOwner;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<ICorrelatedResponse>> _pending = new();
    private readonly Channel<IOpcUaResponse> _userChannel = Channel.CreateUnbounded<IOpcUaResponse>();

    public ChannelReader<IOpcUaResponse> Responses => _userChannel.Reader;

    internal OpcUaClient(
        ChannelWriter<IOpcUaCommand> commands,
        ChannelReader<IOpcUaResponse> streamOutput,
        IActorRef streamOwner)
    {
        _commands = commands;
        _streamOwner = streamOwner;
        _ = Task.Run(() => Dispatch(streamOutput));
    }

    public bool TrySend(IOpcUaCommand command) => _commands.TryWrite(command);

    public ValueTask SendAsync(IOpcUaCommand command, CancellationToken ct = default) =>
        _commands.WriteAsync(command, ct);

    public async Task<ICorrelatedResponse> RequestAsync(IOpcUaCommand command, CancellationToken ct = default)
    {
        var correlationId = command.Options.CorrelationId;
        var tcs = new TaskCompletionSource<ICorrelatedResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[correlationId] = tcs;

        try
        {
            await _commands.WriteAsync(command, ct);
            await using var reg = ct.Register(() => tcs.TrySetCanceled());
            return await tcs.Task;
        }
        finally
        {
            _pending.TryRemove(correlationId, out _);
        }
    }

    private async Task Dispatch(ChannelReader<IOpcUaResponse> streamOutput)
    {
        await foreach (var response in streamOutput.ReadAllAsync())
        {
            if (response is ICorrelatedResponse correlated
                && _pending.TryRemove(correlated.CorrelationId, out var tcs))
            {
                tcs.TrySetResult(correlated);
                continue;
            }

            _userChannel.Writer.TryWrite(response);
        }

        _userChannel.Writer.TryComplete();
    }

    public void Dispose()
    {
        _commands.TryComplete();
        _streamOwner.Tell(PoisonPill.Instance);
    }
}
```

The concrete class is internal — users interact through `IOpcUaClient`.

Protocols without correlation simply skip `RequestAsync` (throw `NotSupportedException` or omit from the interface).

---

## StreamOwnerActor

Each protocol has its own concrete actor. No generics — just copy the 10-line pattern.

```csharp
internal sealed class StreamOwnerActor : ReceiveActor
{
    public StreamOwnerActor(
        ChannelReader<IOpcUaCommand> commandReader,
        ChannelWriter<IOpcUaResponse> responseWriter,
        Flow<IOpcUaCommand, IOpcUaResponse, NotUsed> flow)
    {
        ChannelSource.FromReader(commandReader)
            .Via(flow)
            .RunWith(
                ChannelSink.FromWriter(responseWriter, isOwner: true),
                Context.Materializer());
    }
}
```

That's the entire actor. No `Receive` handlers. Actor start = stream start. Actor stop = stream stop. The `isOwner: true` completes the response channel when the stream ends.

---

## Builder

Each protocol has its own builder. The `Build` method wires everything:

```csharp
public sealed class OpcUaBuilder
{
    public IOpcUaClient Build(ActorSystem system, int commandCapacity = 64)
    {
        var commands = Channel.CreateBounded<IOpcUaCommand>(commandCapacity);
        var responses = Channel.CreateUnbounded<IOpcUaResponse>();

        var flow = BuildFlow();

        var actor = system.ActorOf(Props.Create(() =>
            new StreamOwnerActor(commands.Reader, responses.Writer, flow)));

        return new OpcUaClient(commands.Writer, responses.Reader, actor);
    }
}
```

---

## Generated Typed Client (optional, per protocol)

Source generators produce a typed wrapper that takes `IOpcUaClient` via composition:

```csharp
// Generated
public class WhmClient : IDisposable
{
    private readonly IOpcUaClient _client;

    public WhmClient(IOpcUaClient client) => _client = client;

    public ChannelReader<IOpcUaResponse> Responses => _client.Responses;

    // Typed send methods — fire into the channel
    public ValueTask SendStoreJobAsync(JobOrder job, CancellationToken ct = default) =>
        _client.SendAsync(new WhmJobOrderControlStoreCommand(job), ct);

    public ValueTask SendReadIdentificationAsync(CancellationToken ct = default) =>
        _client.SendAsync(WhmIdentification.Read(), ct);

    public void Dispose() => _client.Dispose();
}
```

No inheritance. Composition via `IOpcUaClient`. The generated client is injectable, testable, and a thin convenience layer for constructing typed commands.

---

## Usage Patterns

### Pattern 1: Channel Loop (recommended)

```csharp
using var client = OpcUaBuilder.Create(opc =>
{
    opc.Endpoint("opc.tcp://machine:4840");
    opc.Subscribe<WhmIdentification>();
    opc.Subscribe<WhmRunCompleteEvent>();
}).Build(system);

// Send
await client.SendAsync(new WhmJobOrderControlStoreCommand(job));

// Receive
await foreach (var msg in client.Responses.ReadAllAsync())
{
    switch (msg)
    {
        case WhmIdentification id: Console.WriteLine(id.Model); break;
        case WhmRunCompleteEvent e: Console.WriteLine(e.GoodQuantity); break;
    }
}
```

### Pattern 2: Actor Integration

```csharp
using var client = opcUa.Build(system);

// DeviceActor reads from client.Responses, sends via client
var device = system.ActorOf(DeviceActor.Props(client));
```

The actor bridges the channel reader into actor messages in `PreStart`:

```csharp
protected override void PreStart()
{
    var self = Self;
    _ = Task.Run(async () =>
    {
        await foreach (var msg in _client.Responses.ReadAllAsync())
            self.Tell(msg);
    });
}
```

### Pattern 3: Generated Typed Client

```csharp
using var whm = new WhmClient(opcUa.Build(system));

await whm.SendStoreJobAsync(job);

await foreach (var msg in whm.Responses.ReadAllAsync()) { ... }
```

---

## Design Decisions

- **Channels over observables**: `ChannelReader<T>` is the .NET-native async enumerable primitive. No Rx dependency. Works with `await foreach`.
- **Actor owns the stream**: Akka.Streams needs a materializer tied to an `ActorSystem`. The `StreamOwnerActor` provides lifecycle management — when the actor stops, the stream stops, the response channel completes.
- **Optional correlation**: `SendAsync` is fire-and-forget. `RequestAsync` provides built-in correlation for protocols that support it (e.g. OPC UA with `CorrelationId`). Correlated responses are intercepted before they reach the `Responses` channel, so the two paths don't interfere.
- **Composition over inheritance**: Generated typed clients wrap `IOpcUaClient`. No class hierarchy. Injectable. Testable.
- **Builder creates channels**: Users never touch `Channel.Create*`. The builder picks appropriate capacities and bundles everything.
- **No shared base types**: Each protocol defines its own interfaces, client, and actor. The pattern is structural, not a type hierarchy.

---

## Applying to a New Protocol

1. Define `IMyCommand` and `IMyMessage` marker interfaces
2. Build a `Flow<IMyCommand, IMyMessage, NotUsed>` (encoder → transport → decoder)
3. Write a concrete `StreamOwnerActor` (copy the 10-line pattern)
4. Write `IMyClient` / `MyClient` (interface + internal implementation)
5. Create a `MyBuilder` with `Build(ActorSystem)` that wires channels → actor → client
6. Optionally: source-generate a typed client wrapper that takes `IMyClient`
