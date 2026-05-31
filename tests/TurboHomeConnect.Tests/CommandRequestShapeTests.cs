using System.Text.Json;
using Shouldly;
using TurboHomeConnect.Commands;
using TurboHomeConnect.Internal;
using TurboHomeConnect.Model;
using Xunit;

namespace TurboHomeConnect.Tests;

public sealed class CommandRequestShapeTests
{
    [Fact]
    public void GetAppliances_is_GET_with_BSH_accept()
    {
        IRestCommand cmd = new GetAppliancesCommand();
        using var req = cmd.BuildRequest();

        req.Method.ShouldBe(HttpMethod.Get);
        req.RequestUri!.ToString().ShouldBe("api/homeappliances");
        req.Headers.Accept.ShouldContain(h => h.MediaType == "application/vnd.bsh.sdk.v1+json");
    }

    [Fact]
    public void GetSingleStatus_includes_haId_and_key_in_path()
    {
        IRestCommand cmd = new GetSingleStatusCommand("BSH-HCS01-1234", "BSH.Common.Status.OperationState");
        using var req = cmd.BuildRequest();

        req.RequestUri!.ToString()
            .ShouldBe("api/homeappliances/BSH-HCS01-1234/status/BSH.Common.Status.OperationState");
    }

    [Fact]
    public async Task SetSetting_PUT_carries_data_envelope_body()
    {
        var value = JsonSerializer.SerializeToElement(true);
        IRestCommand cmd = new SetSettingCommand("haId-1", "BSH.Common.Setting.PowerState", value);

        using var req = cmd.BuildRequest();
        req.Method.ShouldBe(HttpMethod.Put);
        req.Content.ShouldNotBeNull();
        req.Content!.Headers.ContentType!.MediaType.ShouldBe("application/vnd.bsh.sdk.v1+json");

        var body = await req.Content.ReadAsStringAsync(CancellationToken.None);
        var parsed = JsonDocument.Parse(body);
        parsed.RootElement.GetProperty("data").GetProperty("key").GetString()
            .ShouldBe("BSH.Common.Setting.PowerState");
        parsed.RootElement.GetProperty("data").GetProperty("value").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task StartProgram_PUT_serializes_options_array()
    {
        var temperature = new ProgramOption("LaundryCare.Washer.Option.Temperature",
            JsonSerializer.SerializeToElement("LaundryCare.Washer.EnumType.Temperature.GC40"));
        IRestCommand cmd = new StartProgramCommand("haId-1", "LaundryCare.Washer.Program.Cotton", [temperature]);

        using var req = cmd.BuildRequest();
        req.Method.ShouldBe(HttpMethod.Put);
        req.RequestUri!.ToString().ShouldBe("api/homeappliances/haId-1/programs/active");

        var body = await req.Content!.ReadAsStringAsync(CancellationToken.None);
        using var doc = JsonDocument.Parse(body);
        var data = doc.RootElement.GetProperty("data");
        data.GetProperty("key").GetString().ShouldBe("LaundryCare.Washer.Program.Cotton");
        data.GetProperty("options").GetArrayLength().ShouldBe(1);
        data.GetProperty("options")[0].GetProperty("key").GetString()
            .ShouldBe("LaundryCare.Washer.Option.Temperature");
    }

    [Fact]
    public void StopActiveProgram_is_DELETE()
    {
        IRestCommand cmd = new StopActiveProgramCommand("haId-1");
        using var req = cmd.BuildRequest();
        req.Method.ShouldBe(HttpMethod.Delete);
        req.RequestUri!.ToString().ShouldBe("api/homeappliances/haId-1/programs/active");
    }

    [Fact]
    public void SubscribeEvents_uses_event_stream_accept_and_correct_path()
    {
        ISubscribeCommand all = new SubscribeEventsCommand();
        using var reqAll = all.BuildRequest();
        reqAll.RequestUri!.ToString().ShouldBe("api/homeappliances/events");
        reqAll.Headers.Accept.ShouldContain(h => h.MediaType == "text/event-stream");

        ISubscribeCommand scoped = new SubscribeEventsCommand("haId-1");
        using var reqScoped = scoped.BuildRequest();
        reqScoped.RequestUri!.ToString().ShouldBe("api/homeappliances/haId-1/events");
    }
}
