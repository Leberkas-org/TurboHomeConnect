using System.Text.Json.Serialization;

namespace TurboHomeConnect.Model;

public sealed record HomeAppliance(
    [property: JsonPropertyName("haId")] string HaId,
    [property: JsonPropertyName("vib")] string? Vib,
    [property: JsonPropertyName("brand")] string? Brand,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("enumber")] string? ENumber,
    [property: JsonPropertyName("connected")] bool Connected);

internal sealed record HomeAppliancesList(
    [property: JsonPropertyName("homeappliances")] IReadOnlyList<HomeAppliance> HomeAppliances);
