using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using TurboHomeConnect.Abstractions;
using TurboHomeConnect.Model;

namespace TurboHomeConnect.Internal;

internal static class RestHelpers
{
    public const string JsonMediaType = "application/vnd.bsh.sdk.v1+json";

    public static HttpRequestMessage Get(string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(JsonMediaType));
        return request;
    }

    public static HttpRequestMessage Delete(string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(JsonMediaType));
        return request;
    }

    public static HttpRequestMessage PutJson<T>(string path, T body, JsonTypeInfo<T> typeInfo)
    {
        var json = JsonSerializer.Serialize(body, typeInfo);
        var request = new HttpRequestMessage(HttpMethod.Put, path)
        {
            Content = new StringContent(json, Encoding.UTF8, JsonMediaType),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(JsonMediaType));
        return request;
    }

    public static async Task<T> ReadDataAsync<T>(
        HttpResponseMessage response,
        JsonTypeInfo<DataEnvelope<T>> typeInfo,
        CancellationToken cancellationToken)
        where T : class
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var envelope = await JsonSerializer.DeserializeAsync(stream, typeInfo, cancellationToken).ConfigureAwait(false);
        if (envelope is null || envelope.Data is null)
        {
            throw new InvalidOperationException("Home Connect response did not contain a 'data' payload.");
        }

        return envelope.Data;
    }

    public static async Task<HomeConnectErrorMessage> ReadErrorAsync(
        HttpResponseMessage response,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        ErrorBody? error = null;
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var envelope = await JsonSerializer.DeserializeAsync(
                stream,
                HomeConnectJsonContext.Default.ErrorEnvelope,
                cancellationToken).ConfigureAwait(false);
            error = envelope?.Error;
        }
        catch (JsonException)
        {
            // Some failures (gateway HTML, etc.) won't parse as the BSH error envelope.
        }

        return new HomeConnectErrorMessage(
            correlationId,
            (int)response.StatusCode,
            error?.Key,
            error?.Description ?? response.ReasonPhrase);
    }
}
