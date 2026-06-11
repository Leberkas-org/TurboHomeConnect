using System.Diagnostics;
using System.Net.Http.Headers;
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHomeConnect.Abstractions;

namespace TurboHomeConnect.Internal;

internal static class HomeConnectFlow
{
    public static Flow<HomeConnectCommand, IHomeConnectMessage, NotUsed> Build(
        HttpClient http,
        Func<CancellationToken, Task<string>> tokenProvider,
        int restParallelism,
        int maxConcurrentSubscriptions,
        TimeSpan restTimeout,
        int rateLimitElements,
        TimeSpan rateLimitPer,
        RestartSettings sseRestart)
    {
        var restFlow = BuildRestFlow(http, tokenProvider, restParallelism, restTimeout, rateLimitElements, rateLimitPer);
        var sseFlow = BuildSseFlow(http, tokenProvider, maxConcurrentSubscriptions, sseRestart);

        return Flow.FromGraph(GraphDsl.Create(builder =>
        {
            var partition = builder.Add(new Partition<HomeConnectCommand>(
                outputPorts: 2,
                partitioner: cmd => cmd switch
                {
                    SubscribeCommand => 1,
                    RestCommandBase => 0,
                    _ => throw new UnreachableException($"Unknown command kind: {cmd.GetType().Name}"),
                }));
            var merge = builder.Add(new Merge<IHomeConnectMessage>(inputPorts: 2));

            builder.From(partition.Out(0)).Via(builder.Add(restFlow)).To(merge.In(0));
            builder.From(partition.Out(1)).Via(builder.Add(sseFlow)).To(merge.In(1));

            return new FlowShape<HomeConnectCommand, IHomeConnectMessage>(partition.In, merge.Out);
        }));
    }

    private static Flow<HomeConnectCommand, IHomeConnectMessage, NotUsed> BuildRestFlow(
        HttpClient http,
        Func<CancellationToken, Task<string>> tokenProvider,
        int parallelism,
        TimeSpan restTimeout,
        int rateLimitElements,
        TimeSpan rateLimitPer)
    {
        // Token-bucket shaping: burst up to `rateLimitElements`, refill at that rate per `rateLimitPer`.
        // Excess elements are queued; never failed.
        return Flow.Create<HomeConnectCommand>()
            .Throttle(
                elements: rateLimitElements,
                per: rateLimitPer,
                maximumBurst: rateLimitElements,
                mode: ThrottleMode.Shaping)
            .SelectAsyncUnordered(parallelism, async cmd =>
                (IHomeConnectMessage)await DispatchRestAsync(http, tokenProvider, (RestCommandBase)cmd, restTimeout).ConfigureAwait(false));
    }

    private static async Task<ICorrelatedResponse> DispatchRestAsync(
        HttpClient http,
        Func<CancellationToken, Task<string>> tokenProvider,
        RestCommandBase command,
        TimeSpan restTimeout)
    {
        using var cts = new CancellationTokenSource(restTimeout);
        try
        {
            using var request = command.BuildRequest();
            var token = await tokenProvider(cts.Token).ConfigureAwait(false);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await http.SendAsync(request, cts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return await RestHelpers.ReadErrorAsync(response, command.CorrelationId, cts.Token)
                    .ConfigureAwait(false);
            }

            return await command.MapResponseCoreAsync(response, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            return new HomeConnectErrorMessage(command.CorrelationId, null, "Timeout",
                $"REST call did not complete within {restTimeout}.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new HomeConnectErrorMessage(command.CorrelationId, null, null, ex.Message, ex);
        }
    }

    private static Flow<HomeConnectCommand, IHomeConnectMessage, NotUsed> BuildSseFlow(
        HttpClient http,
        Func<CancellationToken, Task<string>> tokenProvider,
        int maxConcurrentSubscriptions,
        RestartSettings sseRestart)
    {
        return Flow.Create<HomeConnectCommand>()
            .MergeMany(maxConcurrentSubscriptions,
                cmd => HomeConnectSseSource.Create(http, tokenProvider, (SubscribeCommand)cmd, sseRestart));
    }
}
