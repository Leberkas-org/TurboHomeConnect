using System.Net.Http.Headers;
using TurboHomeConnect.Abstractions;
using TurboHomeConnect.Model;

namespace TurboHomeConnect.Commands;

/// <summary>
/// Opens the SSE event stream. Pass <see cref="HaId"/> to scope to a single appliance,
/// or leave null to receive events for every appliance on the account.
/// </summary>
public sealed record SubscribeEventsCommand(string? HaId = null) : SubscribeCommand
{
    protected internal override HttpRequestMessage BuildRequest()
    {
        var path = HaId is null
            ? "api/homeappliances/events"
            : $"api/homeappliances/{HaId}/events";

        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
        return request;
    }
}

/// <summary>
/// A single parsed event from the SSE stream. <see cref="Items"/> is empty for connection-level
/// notifications (CONNECTED, DISCONNECTED, PAIRED, DEPAIRED, KEEP-ALIVE).
/// </summary>
public sealed record HomeConnectEventMessage(
    HomeConnectEventType Type,
    string? HaId,
    IReadOnlyList<EventItem> Items) : IHomeConnectMessage;

/// <summary>
/// Surfaced on <see cref="IHomeConnectClient.Responses"/> when the SSE stream disconnects.
/// The Flow reconnects automatically with backoff, but this lets observers react.
/// </summary>
public sealed record SubscriptionDisconnectedMessage(string? HaId, Exception? Reason) : IHomeConnectMessage;
