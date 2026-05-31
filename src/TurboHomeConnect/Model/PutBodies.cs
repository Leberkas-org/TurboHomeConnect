using System.Text.Json;
using System.Text.Json.Serialization;

namespace TurboHomeConnect.Model;

/// <summary>Generic key/value payload used for settings, options, and command writes.</summary>
internal sealed record PutKeyValueBody(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("value")] JsonElement Value)
{
    [JsonPropertyName("unit")] public string? Unit { get; init; }
}

/// <summary>Payload for selecting or starting a program. <see cref="Options"/> is always emitted
/// — even as an empty array — because the Home Connect simulator returns
/// <c>500 SDK.Simulator.InternalError</c> if the field is missing.</summary>
internal sealed record PutProgramBody(
    [property: JsonPropertyName("key")] string Key)
{
    [JsonPropertyName("options")] public IReadOnlyList<ProgramOption> Options { get; init; } = [];
}
