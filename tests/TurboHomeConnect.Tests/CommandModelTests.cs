using System.Net;
using System.Threading.Channels;
using Akka.TestKit.Xunit2;
using Shouldly;
using TurboHomeConnect;
using TurboHomeConnect.Abstractions;
using TurboHomeConnect.Internal;
using TurboHomeConnect.Tests.Support;
using Xunit;

namespace TurboHomeConnect.Tests;

public sealed class CommandModelTests : TestKit
{
    [Fact]
    public async Task RequestAsync_returns_the_concrete_typed_response()
    {
        using var harness = new PatternHarness(Sys);
        using var cts = new CancellationTokenSource(TestKitSettings.DefaultTimeout);

        var command = new TestCommand();
        Task<TestResponse> requestTask = harness.Client.RequestAsync(command, cts.Token);

        await harness.Stub.Seen.ReadAsync(cts.Token);
        await harness.Stub.Emit.WriteAsync(new TestResponse(command.CorrelationId, "typed"), cts.Token);

        var got = await requestTask.WaitAsync(TestKitSettings.DefaultTimeout, cts.Token);
        got.Payload.ShouldBe("typed");
    }

    [Fact]
    public async Task RequestAsync_throws_HomeConnectApiException_when_flow_yields_error()
    {
        using var harness = new PatternHarness(Sys);
        using var cts = new CancellationTokenSource(TestKitSettings.DefaultTimeout);

        var command = new TestCommand();
        var requestTask = harness.Client.RequestAsync(command, cts.Token);
        await harness.Stub.Seen.ReadAsync(cts.Token);

        await harness.Stub.Emit.WriteAsync(
            new HomeConnectErrorMessage(command.CorrelationId, 409, "SDK.Error.WrongOperationState", "wrong state"),
            cts.Token);

        var ex = await Should.ThrowAsync<HomeConnectApiException>(async () => await requestTask);
        ex.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        ex.ErrorKey.ShouldBe("SDK.Error.WrongOperationState");
        ex.CorrelationId.ShouldBe(command.CorrelationId);
    }

    [Fact]
    public async Task FireAndForget_rest_response_arrives_on_Responses()
    {
        using var harness = new PatternHarness(Sys);
        using var cts = new CancellationTokenSource(TestKitSettings.DefaultTimeout);

        var command = new TestCommand();
        await harness.Client.SendAsync(command, cts.Token);
        await harness.Stub.Seen.ReadAsync(cts.Token);

        var response = new TestResponse(command.CorrelationId, "uncorrelated");
        await harness.Stub.Emit.WriteAsync(response, cts.Token);

        var got = await harness.Client.Responses.ReadAsync(cts.Token);
        got.ShouldBe(response);
    }

    [Fact]
    public async Task Commands_writer_reaches_flow_like_SendAsync()
    {
        using var harness = new PatternHarness(Sys);
        using var cts = new CancellationTokenSource(TestKitSettings.DefaultTimeout);

        var command = new TestCommand();
        harness.Client.Commands.TryWrite(command).ShouldBeTrue();

        var seen = await harness.Stub.Seen.ReadAsync(cts.Token);
        seen.ShouldBeSameAs(command);
    }

    [Fact]
    public void Commands_writer_cannot_be_completed_and_client_stays_functional()
    {
        using var harness = new PatternHarness(Sys);

        Should.Throw<InvalidOperationException>(() => harness.Client.Commands.TryComplete());
        Should.Throw<InvalidOperationException>(() => harness.Client.Commands.Complete());

        harness.Client.TrySend(new TestCommand()).ShouldBeTrue();
    }

    [Fact]
    public void StreamOwner_stops_when_the_stream_terminates()
    {
        var commands = Channel.CreateBounded<HomeConnectCommand>(8);
        var responses = Channel.CreateUnbounded<IHomeConnectMessage>();
        var stub = new StubProtocolFlow();

        var owner = Sys.ActorOf(StreamOwnerActor.Props(commands.Reader, responses.Writer, stub.AsFlow()));
        Watch(owner);

        stub.Emit.TryComplete();
        commands.Writer.TryComplete();

        ExpectTerminated(owner, TimeSpan.FromSeconds(5));
    }

    private sealed record TestCommand : RestCommand<TestResponse>
    {
        protected internal override HttpRequestMessage BuildRequest() => new(HttpMethod.Get, "test");

        protected override Task<TestResponse> MapResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
            => Task.FromResult(new TestResponse(CorrelationId, "unused"));
    }

    private sealed record TestResponse(Guid CorrelationId, string Payload)
        : HomeConnectResponse(CorrelationId);
}
