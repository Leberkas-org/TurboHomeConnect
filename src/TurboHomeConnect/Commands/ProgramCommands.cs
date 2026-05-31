using TurboHomeConnect.Abstractions;
using TurboHomeConnect.Internal;
using TurboHomeConnect.Model;

namespace TurboHomeConnect.Commands;

public sealed record GetActiveProgramCommand(string HaId) : HomeConnectCommand, IRestCommand
{
    HttpRequestMessage IRestCommand.BuildRequest() => RestHelpers.Get($"api/homeappliances/{HaId}/programs/active");

    async Task<ICorrelatedResponse> IRestCommand.MapResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var data = await RestHelpers.ReadDataAsync(
            response,
            HomeConnectJsonContext.Default.DataEnvelopeProgram,
            cancellationToken).ConfigureAwait(false);
        return new ActiveProgramResponse(CorrelationId, HaId, data);
    }
}

public sealed record GetSelectedProgramCommand(string HaId) : HomeConnectCommand, IRestCommand
{
    HttpRequestMessage IRestCommand.BuildRequest() => RestHelpers.Get($"api/homeappliances/{HaId}/programs/selected");

    async Task<ICorrelatedResponse> IRestCommand.MapResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var data = await RestHelpers.ReadDataAsync(
            response,
            HomeConnectJsonContext.Default.DataEnvelopeProgram,
            cancellationToken).ConfigureAwait(false);
        return new SelectedProgramResponse(CorrelationId, HaId, data);
    }
}

public sealed record GetAvailableProgramsCommand(string HaId) : HomeConnectCommand, IRestCommand
{
    HttpRequestMessage IRestCommand.BuildRequest() => RestHelpers.Get($"api/homeappliances/{HaId}/programs/available");

    async Task<ICorrelatedResponse> IRestCommand.MapResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var data = await RestHelpers.ReadDataAsync(
            response,
            HomeConnectJsonContext.Default.DataEnvelopeAvailableProgramsList,
            cancellationToken).ConfigureAwait(false);
        return new AvailableProgramsResponse(CorrelationId, HaId, data.Programs);
    }
}

public sealed record GetAvailableProgramCommand(string HaId, string ProgramKey) : HomeConnectCommand, IRestCommand
{
    HttpRequestMessage IRestCommand.BuildRequest()
        => RestHelpers.Get($"api/homeappliances/{HaId}/programs/available/{ProgramKey}");

    async Task<ICorrelatedResponse> IRestCommand.MapResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var data = await RestHelpers.ReadDataAsync(
            response,
            HomeConnectJsonContext.Default.DataEnvelopeProgram,
            cancellationToken).ConfigureAwait(false);
        return new AvailableProgramResponse(CorrelationId, HaId, data);
    }
}

public sealed record StartProgramCommand(string HaId, string ProgramKey, IReadOnlyList<ProgramOption>? Options = null)
    : HomeConnectCommand, IRestCommand
{
    HttpRequestMessage IRestCommand.BuildRequest() => RestHelpers.PutJson(
        $"api/homeappliances/{HaId}/programs/active",
        new DataEnvelope<PutProgramBody>(new PutProgramBody(ProgramKey) { Options = Options ?? [] }),
        HomeConnectJsonContext.Default.DataEnvelopePutProgramBody);

    Task<ICorrelatedResponse> IRestCommand.MapResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        => Task.FromResult<ICorrelatedResponse>(new ProgramStartedResponse(CorrelationId, HaId, ProgramKey));
}

public sealed record SelectProgramCommand(string HaId, string ProgramKey, IReadOnlyList<ProgramOption>? Options = null)
    : HomeConnectCommand, IRestCommand
{
    HttpRequestMessage IRestCommand.BuildRequest() => RestHelpers.PutJson(
        $"api/homeappliances/{HaId}/programs/selected",
        new DataEnvelope<PutProgramBody>(new PutProgramBody(ProgramKey) { Options = Options ?? [] }),
        HomeConnectJsonContext.Default.DataEnvelopePutProgramBody);

    Task<ICorrelatedResponse> IRestCommand.MapResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        => Task.FromResult<ICorrelatedResponse>(new ProgramSelectedResponse(CorrelationId, HaId, ProgramKey));
}

public sealed record StopActiveProgramCommand(string HaId) : HomeConnectCommand, IRestCommand
{
    HttpRequestMessage IRestCommand.BuildRequest() => RestHelpers.Delete($"api/homeappliances/{HaId}/programs/active");

    Task<ICorrelatedResponse> IRestCommand.MapResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        => Task.FromResult<ICorrelatedResponse>(new ProgramStoppedResponse(CorrelationId, HaId));
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
