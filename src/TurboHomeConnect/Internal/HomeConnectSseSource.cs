using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Util;
using TurboHomeConnect.Abstractions;
using TurboHomeConnect.Commands;
using TurboHomeConnect.Model;

namespace TurboHomeConnect.Internal;

internal static class HomeConnectSseSource
{
    public static Source<IHomeConnectMessage, NotUsed> Create(
        HttpClient http,
        Func<CancellationToken, Task<string>> tokenProvider,
        ISubscribeCommand command,
        RestartSettings restart)
    {
        var subscriptionHaId = (command as SubscribeEventsCommand)?.HaId;

        return RestartSource.WithBackoff(
            () => CreateOnce(http, tokenProvider, command, subscriptionHaId),
            restart);
    }

    private static Source<IHomeConnectMessage, NotUsed> CreateOnce(
        HttpClient http,
        Func<CancellationToken, Task<string>> tokenProvider,
        ISubscribeCommand command,
        string? subscriptionHaId)
    {
        return Source.UnfoldResourceAsync<ServerSentEvent, SseConnectionState>(
                create: () => OpenAsync(http, tokenProvider, command),
                read: ReadAsync,
                close: CloseAsync)
            .Select(evt => (IHomeConnectMessage)MapEvent(evt, subscriptionHaId))
            .Recover(ex => Option<IHomeConnectMessage>.Create(
                new SubscriptionDisconnectedMessage(subscriptionHaId, ex)))
            .MapMaterializedValue(_ => NotUsed.Instance);
    }

    private static async Task<SseConnectionState> OpenAsync(
        HttpClient http,
        Func<CancellationToken, Task<string>> tokenProvider,
        ISubscribeCommand command)
    {
        var token = await tokenProvider(CancellationToken.None).ConfigureAwait(false);
        var request = command.BuildRequest();
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // ResponseHeadersRead is critical — without it, HttpClient buffers the entire body before
        // returning, and an SSE stream never completes, so SendAsync would hang forever.
        var response = await http.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            CancellationToken.None).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            response.Dispose();
            throw new HttpRequestException(
                $"Home Connect SSE subscription failed with status {(int)response.StatusCode} {response.ReasonPhrase}.",
                inner: null,
                statusCode: response.StatusCode);
        }

        var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        var reader = new StreamReader(stream, Encoding.UTF8);
        return new SseConnectionState(response, stream, reader);
    }

    private static async Task<Option<ServerSentEvent>> ReadAsync(SseConnectionState state)
    {
        var evt = await SseParser.ReadEventAsync(state.Reader, CancellationToken.None).ConfigureAwait(false);
        return evt is null
            ? Option<ServerSentEvent>.None
            : Option<ServerSentEvent>.Create(evt);
    }

    private static async Task<Done> CloseAsync(SseConnectionState state)
    {
        state.Reader.Dispose();
        await state.Body.DisposeAsync().ConfigureAwait(false);
        state.Response.Dispose();
        return Done.Instance;
    }

    private static HomeConnectEventMessage MapEvent(ServerSentEvent evt, string? subscriptionHaId)
    {
        var type = ParseType(evt.EventType);

        // On the all-appliance stream, Home Connect puts the appliance id in the SSE frame's
        // `id:` field; the per-appliance stream omits it and the subscription scope is the source.
        // The JSON body's `haId` (when present) is the most specific signal, so prefer it.
        if (type == HomeConnectEventType.KeepAlive || string.IsNullOrEmpty(evt.Data))
        {
            return new HomeConnectEventMessage(type, evt.Id ?? subscriptionHaId, []);
        }

        EventEnvelope? envelope = null;
        try
        {
            envelope = JsonSerializer.Deserialize(evt.Data, HomeConnectJsonContext.Default.EventEnvelope);
        }
        catch (JsonException)
        {
            // Some events (CONNECTED/DISCONNECTED for the all-appliance stream) carry a flat haId payload.
        }

        return new HomeConnectEventMessage(
            type,
            envelope?.HaId ?? evt.Id ?? subscriptionHaId,
            envelope?.Items ?? []);
    }

    private static HomeConnectEventType ParseType(string? raw) =>
        raw?.ToUpperInvariant() switch
        {
            "NOTIFY" => HomeConnectEventType.Notify,
            "STATUS" => HomeConnectEventType.Status,
            "EVENT" => HomeConnectEventType.Event,
            "CONNECTED" => HomeConnectEventType.Connected,
            "DISCONNECTED" => HomeConnectEventType.Disconnected,
            "PAIRED" => HomeConnectEventType.Paired,
            "DEPAIRED" => HomeConnectEventType.Depaired,
            "KEEP-ALIVE" or "KEEPALIVE" => HomeConnectEventType.KeepAlive,
            _ => HomeConnectEventType.Event,
        };

    private sealed record SseConnectionState(HttpResponseMessage Response, Stream Body, StreamReader Reader);
}
