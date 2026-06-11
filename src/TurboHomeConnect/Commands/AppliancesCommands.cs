using TurboHomeConnect.Model;

namespace TurboHomeConnect.Commands;

public sealed record GetAppliancesCommand : RestCommand<AppliancesResponse>
{
    protected internal override HttpRequestMessage BuildRequest() => RestHelpers.Get("api/homeappliances");

    protected override async Task<AppliancesResponse> MapResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var data = await RestHelpers.ReadDataAsync(
            response,
            HomeConnectJsonContext.Default.DataEnvelopeHomeAppliancesList,
            cancellationToken).ConfigureAwait(false);
        return new AppliancesResponse(CorrelationId, data.HomeAppliances);
    }
}

public sealed record GetApplianceCommand(string HaId) : RestCommand<ApplianceResponse>
{
    protected internal override HttpRequestMessage BuildRequest() => RestHelpers.Get($"api/homeappliances/{HaId}");

    protected override async Task<ApplianceResponse> MapResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var data = await RestHelpers.ReadDataAsync(
            response,
            HomeConnectJsonContext.Default.DataEnvelopeHomeAppliance,
            cancellationToken).ConfigureAwait(false);
        return new ApplianceResponse(CorrelationId, data);
    }
}

public sealed record AppliancesResponse(Guid CorrelationId, IReadOnlyList<HomeAppliance> Appliances)
    : HomeConnectResponse(CorrelationId);

public sealed record ApplianceResponse(Guid CorrelationId, HomeAppliance Appliance)
    : HomeConnectResponse(CorrelationId);
