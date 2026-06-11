using TurboHomeConnect.Model;

namespace TurboHomeConnect.Commands;

public sealed record GetStatusCommand(string HaId) : RestCommand<StatusResponse>
{
    protected internal override HttpRequestMessage BuildRequest() => RestHelpers.Get($"api/homeappliances/{HaId}/status");

    protected override async Task<StatusResponse> MapResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var data = await RestHelpers.ReadDataAsync(
            response,
            HomeConnectJsonContext.Default.DataEnvelopeStatusList,
            cancellationToken).ConfigureAwait(false);
        return new StatusResponse(CorrelationId, HaId, data.Status);
    }
}

public sealed record GetSingleStatusCommand(string HaId, string StatusKey) : RestCommand<SingleStatusResponse>
{
    protected internal override HttpRequestMessage BuildRequest() => RestHelpers.Get($"api/homeappliances/{HaId}/status/{StatusKey}");

    protected override async Task<SingleStatusResponse> MapResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var data = await RestHelpers.ReadDataAsync(
            response,
            HomeConnectJsonContext.Default.DataEnvelopeStatusValue,
            cancellationToken).ConfigureAwait(false);
        return new SingleStatusResponse(CorrelationId, HaId, data);
    }
}

public sealed record StatusResponse(Guid CorrelationId, string HaId, IReadOnlyList<StatusValue> Status)
    : HomeConnectResponse(CorrelationId);

public sealed record SingleStatusResponse(Guid CorrelationId, string HaId, StatusValue Status)
    : HomeConnectResponse(CorrelationId);
