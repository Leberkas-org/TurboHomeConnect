using System.Text.Json;
using TurboHomeConnect.Abstractions;
using TurboHomeConnect.Internal;
using TurboHomeConnect.Model;

namespace TurboHomeConnect.Commands;

public sealed record GetActiveProgramOptionsCommand(string HaId) : HomeConnectCommand, IRestCommand
{
    HttpRequestMessage IRestCommand.BuildRequest()
        => RestHelpers.Get($"api/homeappliances/{HaId}/programs/active/options");

    async Task<ICorrelatedResponse> IRestCommand.MapResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var data = await RestHelpers.ReadDataAsync(
            response,
            HomeConnectJsonContext.Default.DataEnvelopeProgram,
            cancellationToken).ConfigureAwait(false);
        return new ProgramOptionsResponse(CorrelationId, HaId, ProgramScope.Active, data.Options ?? []);
    }
}

public sealed record GetActiveProgramOptionCommand(string HaId, string OptionKey) : HomeConnectCommand, IRestCommand
{
    HttpRequestMessage IRestCommand.BuildRequest()
        => RestHelpers.Get($"api/homeappliances/{HaId}/programs/active/options/{OptionKey}");

    async Task<ICorrelatedResponse> IRestCommand.MapResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var data = await RestHelpers.ReadDataAsync(
            response,
            HomeConnectJsonContext.Default.DataEnvelopeProgramOption,
            cancellationToken).ConfigureAwait(false);
        return new ProgramOptionResponse(CorrelationId, HaId, ProgramScope.Active, data);
    }
}

public sealed record SetActiveProgramOptionCommand(string HaId, string OptionKey, JsonElement Value, string? Unit = null)
    : HomeConnectCommand, IRestCommand
{
    HttpRequestMessage IRestCommand.BuildRequest() => RestHelpers.PutJson(
        $"api/homeappliances/{HaId}/programs/active/options/{OptionKey}",
        new DataEnvelope<PutKeyValueBody>(new PutKeyValueBody(OptionKey, Value) { Unit = Unit }),
        HomeConnectJsonContext.Default.DataEnvelopePutKeyValueBody);

    Task<ICorrelatedResponse> IRestCommand.MapResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        => Task.FromResult<ICorrelatedResponse>(new ProgramOptionUpdatedResponse(CorrelationId, HaId, ProgramScope.Active, OptionKey));
}

public sealed record GetSelectedProgramOptionsCommand(string HaId) : HomeConnectCommand, IRestCommand
{
    HttpRequestMessage IRestCommand.BuildRequest()
        => RestHelpers.Get($"api/homeappliances/{HaId}/programs/selected/options");

    async Task<ICorrelatedResponse> IRestCommand.MapResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var data = await RestHelpers.ReadDataAsync(
            response,
            HomeConnectJsonContext.Default.DataEnvelopeProgram,
            cancellationToken).ConfigureAwait(false);
        return new ProgramOptionsResponse(CorrelationId, HaId, ProgramScope.Selected, data.Options ?? []);
    }
}

public sealed record GetSelectedProgramOptionCommand(string HaId, string OptionKey) : HomeConnectCommand, IRestCommand
{
    HttpRequestMessage IRestCommand.BuildRequest()
        => RestHelpers.Get($"api/homeappliances/{HaId}/programs/selected/options/{OptionKey}");

    async Task<ICorrelatedResponse> IRestCommand.MapResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var data = await RestHelpers.ReadDataAsync(
            response,
            HomeConnectJsonContext.Default.DataEnvelopeProgramOption,
            cancellationToken).ConfigureAwait(false);
        return new ProgramOptionResponse(CorrelationId, HaId, ProgramScope.Selected, data);
    }
}

public sealed record SetSelectedProgramOptionCommand(string HaId, string OptionKey, JsonElement Value, string? Unit = null)
    : HomeConnectCommand, IRestCommand
{
    HttpRequestMessage IRestCommand.BuildRequest() => RestHelpers.PutJson(
        $"api/homeappliances/{HaId}/programs/selected/options/{OptionKey}",
        new DataEnvelope<PutKeyValueBody>(new PutKeyValueBody(OptionKey, Value) { Unit = Unit }),
        HomeConnectJsonContext.Default.DataEnvelopePutKeyValueBody);

    Task<ICorrelatedResponse> IRestCommand.MapResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        => Task.FromResult<ICorrelatedResponse>(new ProgramOptionUpdatedResponse(CorrelationId, HaId, ProgramScope.Selected, OptionKey));
}

public enum ProgramScope
{
    Active,
    Selected,
}

public sealed record ProgramOptionsResponse(Guid CorrelationId, string HaId, ProgramScope Scope, IReadOnlyList<ProgramOption> Options)
    : HomeConnectResponse(CorrelationId);

public sealed record ProgramOptionResponse(Guid CorrelationId, string HaId, ProgramScope Scope, ProgramOption Option)
    : HomeConnectResponse(CorrelationId);

public sealed record ProgramOptionUpdatedResponse(Guid CorrelationId, string HaId, ProgramScope Scope, string OptionKey)
    : HomeConnectResponse(CorrelationId);
