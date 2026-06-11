using System.Text.Json;
using TurboHomeConnect.Model;

namespace TurboHomeConnect.Commands;

public sealed record GetSettingsCommand(string HaId) : RestCommand<SettingsResponse>
{
    protected internal override HttpRequestMessage BuildRequest() => RestHelpers.Get($"api/homeappliances/{HaId}/settings");

    protected override async Task<SettingsResponse> MapResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var data = await RestHelpers.ReadDataAsync(
            response,
            HomeConnectJsonContext.Default.DataEnvelopeSettingsList,
            cancellationToken).ConfigureAwait(false);
        return new SettingsResponse(CorrelationId, HaId, data.Settings);
    }
}

public sealed record GetSingleSettingCommand(string HaId, string SettingKey) : RestCommand<SingleSettingResponse>
{
    protected internal override HttpRequestMessage BuildRequest() => RestHelpers.Get($"api/homeappliances/{HaId}/settings/{SettingKey}");

    protected override async Task<SingleSettingResponse> MapResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var data = await RestHelpers.ReadDataAsync(
            response,
            HomeConnectJsonContext.Default.DataEnvelopeSettingValue,
            cancellationToken).ConfigureAwait(false);
        return new SingleSettingResponse(CorrelationId, HaId, data);
    }
}

public sealed record SetSettingCommand(string HaId, string SettingKey, JsonElement Value, string? Unit = null)
    : RestCommand<SettingUpdatedResponse>
{
    protected internal override HttpRequestMessage BuildRequest() => RestHelpers.PutJson(
        $"api/homeappliances/{HaId}/settings/{SettingKey}",
        new DataEnvelope<PutKeyValueBody>(new PutKeyValueBody(SettingKey, Value) { Unit = Unit }),
        HomeConnectJsonContext.Default.DataEnvelopePutKeyValueBody);

    protected override Task<SettingUpdatedResponse> MapResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        => Task.FromResult(new SettingUpdatedResponse(CorrelationId, HaId, SettingKey));
}

public sealed record SettingsResponse(Guid CorrelationId, string HaId, IReadOnlyList<SettingValue> Settings)
    : HomeConnectResponse(CorrelationId);

public sealed record SingleSettingResponse(Guid CorrelationId, string HaId, SettingValue Setting)
    : HomeConnectResponse(CorrelationId);

public sealed record SettingUpdatedResponse(Guid CorrelationId, string HaId, string SettingKey)
    : HomeConnectResponse(CorrelationId);
