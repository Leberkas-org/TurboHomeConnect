using TurboHomeConnect.Abstractions;
using TurboHomeConnect.Internal;
using TurboHomeConnect.Model;

namespace TurboHomeConnect.Commands;

public sealed record GetImagesCommand(string HaId) : HomeConnectCommand, IRestCommand
{
    HttpRequestMessage IRestCommand.BuildRequest() => RestHelpers.Get($"api/homeappliances/{HaId}/images");

    async Task<ICorrelatedResponse> IRestCommand.MapResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var data = await RestHelpers.ReadDataAsync(
            response,
            HomeConnectJsonContext.Default.DataEnvelopeImagesList,
            cancellationToken).ConfigureAwait(false);
        return new ImagesResponse(CorrelationId, HaId, data.Images);
    }
}

public sealed record GetImageBytesCommand(string HaId, string ImageKey) : HomeConnectCommand, IRestCommand
{
    HttpRequestMessage IRestCommand.BuildRequest()
    {
        // Image endpoint returns the binary content directly, not a JSON envelope.
        var request = new HttpRequestMessage(HttpMethod.Get, $"api/homeappliances/{HaId}/images/{ImageKey}");
        return request;
    }

    async Task<ICorrelatedResponse> IRestCommand.MapResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        return new ImageBytesResponse(CorrelationId, HaId, ImageKey, contentType, bytes);
    }
}

public sealed record ImagesResponse(Guid CorrelationId, string HaId, IReadOnlyList<HomeApplianceImage> Images)
    : HomeConnectResponse(CorrelationId);

public sealed record ImageBytesResponse(Guid CorrelationId, string HaId, string ImageKey, string ContentType, byte[] Bytes)
    : HomeConnectResponse(CorrelationId);
