using System.Text.Json;
using System.Text.Json.Serialization;

namespace TurboHomeConnect.Model;

/// <summary>
/// Status and Setting share the same wire shape: an arbitrary <see cref="Value"/> (bool / number /
/// string / enum key) plus metadata. The raw element is kept as <see cref="JsonElement"/> so callers
/// can cast to whatever shape the specific key uses.
/// </summary>
public abstract record KeyedValue(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("value")] JsonElement Value)
{
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("displayvalue")] public string? DisplayValue { get; init; }
    [JsonPropertyName("unit")] public string? Unit { get; init; }
    [JsonPropertyName("type")] public string? Type { get; init; }
    [JsonPropertyName("constraints")] public ValueConstraints? Constraints { get; init; }
}

public sealed record StatusValue(string Key, JsonElement Value) : KeyedValue(Key, Value);

public sealed record SettingValue(string Key, JsonElement Value) : KeyedValue(Key, Value);

public sealed record ValueConstraints
{
    [JsonPropertyName("min")] public double? Min { get; init; }
    [JsonPropertyName("max")] public double? Max { get; init; }
    [JsonPropertyName("stepsize")] public double? StepSize { get; init; }
    [JsonPropertyName("allowedvalues")] public IReadOnlyList<string>? AllowedValues { get; init; }
    [JsonPropertyName("displayvalues")] public IReadOnlyList<string>? DisplayValues { get; init; }
    [JsonPropertyName("default")] public JsonElement? Default { get; init; }
    [JsonPropertyName("access")] public string? Access { get; init; }
    [JsonPropertyName("execution")] public string? Execution { get; init; }
}

internal sealed record StatusList(
    [property: JsonPropertyName("status")] IReadOnlyList<StatusValue> Status);

internal sealed record SettingsList(
    [property: JsonPropertyName("settings")] IReadOnlyList<SettingValue> Settings);

internal sealed record CommandsList(
    [property: JsonPropertyName("commands")] IReadOnlyList<HomeConnectCommandDescriptor> Commands);

public sealed record HomeConnectCommandDescriptor(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("name")] string? Name);
