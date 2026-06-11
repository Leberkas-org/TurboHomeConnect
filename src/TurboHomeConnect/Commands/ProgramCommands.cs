using TurboHomeConnect.Model;

namespace TurboHomeConnect.Commands;

public sealed record GetActiveProgramCommand(string HaId) : RestCommand<ActiveProgramResponse>
{
    protected internal override HttpRequestMessage BuildRequest() => RestHelpers.Get($"api/homeappliances/{HaId}/programs/active");

    protected override async Task<ActiveProgramResponse> MapResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var data = await RestHelpers.ReadDataAsync(
            response,
            HomeConnectJsonContext.Default.DataEnvelopeProgram,
            cancellationToken).ConfigureAwait(false);
        return new ActiveProgramResponse(CorrelationId, HaId, data);
    }
}

public sealed record GetSelectedProgramCommand(string HaId) : RestCommand<SelectedProgramResponse>
{
    protected internal override HttpRequestMessage BuildRequest() => RestHelpers.Get($"api/homeappliances/{HaId}/programs/selected");

    protected override async Task<SelectedProgramResponse> MapResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var data = await RestHelpers.ReadDataAsync(
            response,
            HomeConnectJsonContext.Default.DataEnvelopeProgram,
            cancellationToken).ConfigureAwait(false);
        return new SelectedProgramResponse(CorrelationId, HaId, data);
    }
}

public sealed record GetAvailableProgramsCommand(string HaId) : RestCommand<AvailableProgramsResponse>
{
    protected internal override HttpRequestMessage BuildRequest() => RestHelpers.Get($"api/homeappliances/{HaId}/programs/available");

    protected override async Task<AvailableProgramsResponse> MapResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var data = await RestHelpers.ReadDataAsync(
            response,
            HomeConnectJsonContext.Default.DataEnvelopeAvailableProgramsList,
            cancellationToken).ConfigureAwait(false);
        return new AvailableProgramsResponse(CorrelationId, HaId, data.Programs);
    }
}

public sealed record GetAvailableProgramCommand(string HaId, string ProgramKey) : RestCommand<AvailableProgramResponse>
{
    protected internal override HttpRequestMessage BuildRequest()
        => RestHelpers.Get($"api/homeappliances/{HaId}/programs/available/{ProgramKey}");

    protected override async Task<AvailableProgramResponse> MapResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var data = await RestHelpers.ReadDataAsync(
            response,
            HomeConnectJsonContext.Default.DataEnvelopeProgram,
            cancellationToken).ConfigureAwait(false);
        return new AvailableProgramResponse(CorrelationId, HaId, data);
    }
}

public sealed record StartProgramCommand(string HaId, string ProgramKey, IReadOnlyList<ProgramOption>? Options = null)
    : RestCommand<ProgramStartedResponse>
{
    protected internal override HttpRequestMessage BuildRequest() => RestHelpers.PutJson(
        $"api/homeappliances/{HaId}/programs/active",
        new DataEnvelope<PutProgramBody>(new PutProgramBody(ProgramKey) { Options = Options ?? [] }),
        HomeConnectJsonContext.Default.DataEnvelopePutProgramBody);

    protected override Task<ProgramStartedResponse> MapResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        => Task.FromResult(new ProgramStartedResponse(CorrelationId, HaId, ProgramKey));
}

public sealed record SelectProgramCommand(string HaId, string ProgramKey, IReadOnlyList<ProgramOption>? Options = null)
    : RestCommand<ProgramSelectedResponse>
{
    protected internal override HttpRequestMessage BuildRequest() => RestHelpers.PutJson(
        $"api/homeappliances/{HaId}/programs/selected",
        new DataEnvelope<PutProgramBody>(new PutProgramBody(ProgramKey) { Options = Options ?? [] }),
        HomeConnectJsonContext.Default.DataEnvelopePutProgramBody);

    protected override Task<ProgramSelectedResponse> MapResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        => Task.FromResult(new ProgramSelectedResponse(CorrelationId, HaId, ProgramKey));
}

public sealed record StopActiveProgramCommand(string HaId) : RestCommand<ProgramStoppedResponse>
{
    protected internal override HttpRequestMessage BuildRequest() => RestHelpers.Delete($"api/homeappliances/{HaId}/programs/active");

    protected override Task<ProgramStoppedResponse> MapResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        => Task.FromResult(new ProgramStoppedResponse(CorrelationId, HaId));
}

public sealed record ActiveProgramResponse(Guid CorrelationId, string HaId, Program Program)
    : HomeConnectResponse(CorrelationId);

public sealed record SelectedProgramResponse(Guid CorrelationId, string HaId, Program Program)
    : HomeConnectResponse(CorrelationId);

public sealed record AvailableProgramsResponse(Guid CorrelationId, string HaId, IReadOnlyList<Program> Programs)
    : HomeConnectResponse(CorrelationId);

public sealed record AvailableProgramResponse(Guid CorrelationId, string HaId, Program Program)
    : HomeConnectResponse(CorrelationId);

public sealed record ProgramStartedResponse(Guid CorrelationId, string HaId, string ProgramKey)
    : HomeConnectResponse(CorrelationId);

public sealed record ProgramSelectedResponse(Guid CorrelationId, string HaId, string ProgramKey)
    : HomeConnectResponse(CorrelationId);

public sealed record ProgramStoppedResponse(Guid CorrelationId, string HaId)
    : HomeConnectResponse(CorrelationId);
