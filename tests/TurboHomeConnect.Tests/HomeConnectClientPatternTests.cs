using Akka.TestKit.Xunit2;
using Shouldly;
using TurboHomeConnect.Abstractions;
using TurboHomeConnect.Internal;
using TurboHomeConnect.Tests.Support;
using Xunit;

namespace TurboHomeConnect.Tests;

public sealed class HomeConnectClientPatternTests : TestKit
{
    [Fact]
    public async Task RequestAsync_completes_with_matching_correlated_response()
    {
        using var harness = new PatternHarness(Sys);

        using var cts = new CancellationTokenSource(TestKitSettings.DefaultTimeout);

        var command = new TestCommand();
        var requestTask = harness.Client.RequestAsync(command, cts.Token);

        // Stub flow received it.
        var seen = await harness.Stub.Seen.ReadAsync(cts.Token);
        seen.ShouldBeSameAs(command);

        // Push a matching correlated response back.
        var response = new TestResponse(command.CorrelationId, "ok");
        await harness.Stub.Emit.WriteAsync(response, cts.Token);

        var got = await requestTask.WaitAsync(TestKitSettings.DefaultTimeout, cts.Token);
        got.ShouldBe(response);
    }

    [Fact]
    public async Task Uncorrelated_messages_arrive_on_Responses_channel()
    {
        using var harness = new PatternHarness(Sys);

        var notification = new TestNotification("hello");
        await harness.Stub.Emit.WriteAsync(notification);

        using var cts = new CancellationTokenSource(TestKitSettings.DefaultTimeout);
        var got = await harness.Client.Responses.ReadAsync(cts.Token);
        got.ShouldBe(notification);
    }

    [Fact]
    public async Task RequestAsync_with_canceled_token_fails_pending_call()
    {
        using var harness = new PatternHarness(Sys);

        using var cts = new CancellationTokenSource();
        var requestTask = harness.Client.RequestAsync(new TestCommand(), cts.Token);

        cts.Cancel();

        await Should.ThrowAsync<TaskCanceledException>(async () => await requestTask);
    }

    [Fact]
    public void TrySend_returns_false_after_dispose()
    {
        var harness = new PatternHarness(Sys);
        harness.Client.Dispose();

        harness.Client.TrySend(new TestCommand()).ShouldBeFalse();
    }

    // Stand-in REST command for the pattern tests — the stub flow doesn't dispatch HTTP,
    // so BuildRequest/MapResponseAsync are never invoked.
    private sealed record TestCommand : HomeConnectCommand, IRestCommand
    {
        HttpRequestMessage IRestCommand.BuildRequest() => new(HttpMethod.Get, "test");
        Task<ICorrelatedResponse> IRestCommand.MapResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
            => Task.FromResult<ICorrelatedResponse>(new TestResponse(CorrelationId, "unused"));
    }

    private sealed record TestResponse(Guid CorrelationId, string Payload)
        : HomeConnectResponse(CorrelationId);

    private sealed record TestNotification(string Text) : IHomeConnectMessage;
}
