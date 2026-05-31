using System.Text.Json.Serialization;

namespace TurboHomeConnect.Model;

internal sealed record ErrorEnvelope(
    [property: JsonPropertyName("error")] ErrorBody? Error);

public sealed record ErrorBody(
    [property: JsonPropertyName("key")] string? Key,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("value")] string? Value);
