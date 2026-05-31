using System.Text.Json;
using System.Text.Json.Serialization;

namespace TurboHomeConnect.Model;

/// <summary>
/// Source-generated serialization metadata. Wiring all wire types here lets the trimmer / AOT
/// strip the reflection-based serializer.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(DataEnvelope<HomeAppliancesList>))]
[JsonSerializable(typeof(DataEnvelope<HomeAppliance>))]
[JsonSerializable(typeof(DataEnvelope<StatusList>))]
[JsonSerializable(typeof(DataEnvelope<StatusValue>))]
[JsonSerializable(typeof(DataEnvelope<SettingsList>))]
[JsonSerializable(typeof(DataEnvelope<SettingValue>))]
[JsonSerializable(typeof(DataEnvelope<Program>))]
[JsonSerializable(typeof(DataEnvelope<ProgramOption>))]
[JsonSerializable(typeof(DataEnvelope<AvailableProgramsList>))]
[JsonSerializable(typeof(DataEnvelope<CommandsList>))]
[JsonSerializable(typeof(DataEnvelope<ImagesList>))]
[JsonSerializable(typeof(EventEnvelope))]
[JsonSerializable(typeof(ErrorEnvelope))]
[JsonSerializable(typeof(DataEnvelope<PutKeyValueBody>))]
[JsonSerializable(typeof(DataEnvelope<PutProgramBody>))]
internal sealed partial class HomeConnectJsonContext : JsonSerializerContext;
