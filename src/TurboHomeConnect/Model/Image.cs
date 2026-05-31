using System.Text.Json.Serialization;

namespace TurboHomeConnect.Model;

public sealed record HomeApplianceImage(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("imagekey")] string? ImageKey,
    [property: JsonPropertyName("previewimage")] string? PreviewImage,
    [property: JsonPropertyName("timestamp")] long? Timestamp,
    [property: JsonPropertyName("quality")] string? Quality);

internal sealed record ImagesList(
    [property: JsonPropertyName("images")] IReadOnlyList<HomeApplianceImage> Images);
