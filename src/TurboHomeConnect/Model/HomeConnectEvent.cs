using System.Text.Json;
using System.Text.Json.Serialization;

namespace TurboHomeConnect.Model;

/// <summary>
/// SSE event types delivered by the Home Connect API on the <c>events</c> stream.
/// </summary>
public enum HomeConnectEventType
{
    /// <summary>One or more values were modified — items array carries the diff.</summary>
    Notify,
    /// <summary>Initial / refreshed status snapshot.</summary>
    Status,
    /// <summary>A discrete event (program finished, door opened, etc.).</summary>
    Event,
    /// <summary>The appliance came online.</summary>
    Connected,
    /// <summary>The appliance went offline.</summary>
    Disconnected,
    /// <summary>An appliance was paired to this account.</summary>
    Paired,
    /// <summary>An appliance was unpaired from this account.</summary>
    Depaired,
    /// <summary>Periodic keep-alive frame — no payload.</summary>
    KeepAlive,
}

public sealed record EventItem(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("value")] JsonElement Value)
{
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("displayvalue")] public string? DisplayValue { get; init; }
    [JsonPropertyName("unit")] public string? Unit { get; init; }
    [JsonPropertyName("level")] public string? Level { get; init; }
    [JsonPropertyName("handling")] public string? Handling { get; init; }
    [JsonPropertyName("timestamp")] public long? Timestamp { get; init; }
}

internal sealed record EventEnvelope(
    [property: JsonPropertyName("haId")] string? HaId,
    [property: JsonPropertyName("items")] IReadOnlyList<EventItem>? Items);
