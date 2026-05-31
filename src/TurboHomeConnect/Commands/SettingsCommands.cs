using System.Text.Json;
using TurboHomeConnect.Abstractions;
using TurboHomeConnect.Internal;
using TurboHomeConnect.Model;

namespace TurboHomeConnect.Commands;

public sealed record GetSettingsCommand(string HaId) : HomeConnectCommand, IRestCommand
{
    HttpRequestMessage IRestCommand.BuildRequest() => RestHelpers.Get($"api/homeappliances/{HaId}/settings");

    async Task<ICorrelatedResponse> IRestCommand.MapResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var data = await RestHelpers.ReadDataAsync(
            response,
            HomeConnectJsonContext.Default.DataEnvelopeSettingsList,
            cancellationToken).ConfigureAwait(false);
        return new SettingsResponse(CorrelationId, HaId, data.Settings);
    }
}

public sealed record GetSingleSettingCommand(string HaId, string SettingKey) : HomeConnectCommand, IRestCommand
{
    HttpRequestMessage IRestCommand.BuildRequest() => RestHelpers.Get($"api/homeappliances/{HaId}/settings/{SettingKey}");

    async Task<ICorrelatedResponse> IRestCommand.MapResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var data = await RestHelpers.ReadDataAsync(
            response,
            HomeConnectJsonContext.Default.DataEnvelopeSettingValue,
            cancellationToken).ConfigureAwait(false);
        return new SingleSettingResponse(CorrelationId, HaId, data);
    }
}

public sealed record SetSettingCommand(string HaId, string SettingKey, JsonElement Value, string? Unit = null)
    : HomeConnectCommand, IRestCommand
{
    HttpRequestMessage IRestCommand.BuildRequest() => RestHelpers.PutJson(
        $"api/homeappliances/{HaId}/settings/{SettingKey}",
        new DataEnvelope<PutKeyValueBody>(new PutKeyValueBody(SettingKey, Value) { Unit = Unit }),
        HomeConnectJsonContext.Default.DataEnvelopePutKeyValueBody);

    Task<ICorrelatedResponse> IRestCommand.MapResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        => Task.FromResult<ICorrelatedResponse>(new SettingUpdatedResponse(CorrelationId, HaId, SettingKey));
}

public sealed record SettingsResponse(Guid CorrelationId, string HaId, IReadOnlyList<SettingValue> Settings)
    : HomeConnectResponse(CorrelationId);

public sealed record SingleSettingResponse(Guid CorrelationId, string HaId, SettingValue Setting)
    : HomeConnectResponse(CorrelationId);

public sealed record SettingUpdatedResponse(Guid CorrelationId, string HaId, string SettingKey)
    : HomeConnectResponse(CorrelationId);
