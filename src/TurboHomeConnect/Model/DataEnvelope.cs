using System.Text.Json.Serialization;

namespace TurboHomeConnect.Model;

/// <summary>
/// Most Home Connect REST responses wrap their payload in <c>{ "data": {...} }</c>.
/// This generic envelope lets one method unwrap any of them.
/// </summary>
public sealed record DataEnvelope<T>(
    [property: JsonPropertyName("data")] T? Data);
