using System.Text.Json;
using TurboHomeConnect.Abstractions;
using TurboHomeConnect.Internal;
using TurboHomeConnect.Model;

namespace TurboHomeConnect.Commands;

public sealed record GetCommandsCommand(string HaId) : HomeConnectCommand, IRestCommand
{
    HttpRequestMessage IRestCommand.BuildRequest() => RestHelpers.Get($"api/homeappliances/{HaId}/commands");

    async Task<ICorrelatedResponse> IRestCommand.MapResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var data = await RestHelpers.ReadDataAsync(
            response,
            HomeConnectJsonContext.Default.DataEnvelopeCommandsList,
            cancellationToken).ConfigureAwait(false);
        return new CommandsResponse(CorrelationId, HaId, data.Commands);
    }
}

public sealed record ExecuteCommandCommand(string HaId, string CommandKey, bool Value = true)
    : HomeConnectCommand, IRestCommand
{
    HttpRequestMessage IRestCommand.BuildRequest()
    {
        var value = JsonSerializer.SerializeToElement(Value);
        return RestHelpers.PutJson(
            $"api/homeappliances/{HaId}/commands/{CommandKey}",
            new DataEnvelope<PutKeyValueBody>(new PutKeyValueBody(CommandKey, value)),
            HomeConnectJsonContext.Default.DataEnvelopePutKeyValueBody);
    }

    Task<ICorrelatedResponse> IRestCommand.MapResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        => Task.FromResult<ICorrelatedResponse>(new CommandExecutedResponse(CorrelationId, HaId, CommandKey));
}

public sealed record CommandsResponse(Guid CorrelationId, string HaId, IReadOnlyList<HomeConnectCommandDescriptor> Commands)
    : HomeConnectResponse(CorrelationId);

public sealed record CommandExecutedResponse(Guid CorrelationId, string HaId, string CommandKey)
    : HomeConnectResponse(CorrelationId);
