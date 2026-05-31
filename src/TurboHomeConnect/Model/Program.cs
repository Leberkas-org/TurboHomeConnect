using System.Text.Json;
using System.Text.Json.Serialization;

namespace TurboHomeConnect.Model;

public sealed record Program(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("options")] IReadOnlyList<ProgramOption>? Options,
    [property: JsonPropertyName("constraints")] ProgramConstraints? Constraints);

public sealed record ProgramOption(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("value")] JsonElement Value)
{
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("displayvalue")] public string? DisplayValue { get; init; }
    [JsonPropertyName("unit")] public string? Unit { get; init; }
    [JsonPropertyName("type")] public string? Type { get; init; }
    [JsonPropertyName("constraints")] public ValueConstraints? Constraints { get; init; }
    [JsonPropertyName("liveupdate")] public bool? LiveUpdate { get; init; }
}

public sealed record ProgramConstraints
{
    [JsonPropertyName("access")] public string? Access { get; init; }
    [JsonPropertyName("available")] public bool? Available { get; init; }
    [JsonPropertyName("execution")] public string? Execution { get; init; }
}

internal sealed record AvailableProgramsList(
    [property: JsonPropertyName("programs")] IReadOnlyList<Program> Programs);
