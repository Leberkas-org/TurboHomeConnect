using TurboHomeConnect.Abstractions;
using TurboHomeConnect.Internal;
using TurboHomeConnect.Model;

namespace TurboHomeConnect.Commands;

public sealed record GetAppliancesCommand : HomeConnectCommand, IRestCommand
{
    HttpRequestMessage IRestCommand.BuildRequest() => RestHelpers.Get("api/homeappliances");

    async Task<ICorrelatedResponse> IRestCommand.MapResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var data = await RestHelpers.ReadDataAsync(
            response,
            HomeConnectJsonContext.Default.DataEnvelopeHomeAppliancesList,
            cancellationToken).ConfigureAwait(false);
        return new AppliancesResponse(CorrelationId, data.HomeAppliances);
    }
}

public sealed record GetApplianceCommand(string HaId) : HomeConnectCommand, IRestCommand
{
    HttpRequestMessage IRestCommand.BuildRequest() => RestHelpers.Get($"api/homeappliances/{HaId}");

    async Task<ICorrelatedResponse> IRestCommand.MapResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
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
