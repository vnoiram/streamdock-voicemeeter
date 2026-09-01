using System.Text.Json;
using System.Text.Json.Serialization;

namespace StreamDockVoicemeeter;

/// <summary>
///     Client-side view of the voicemeeter-hub WebSocket protocol (v1). Kept in sync with the
///     hub repository's <c>docs/protocol.md</c>. The plugin connects to the shared hub instead of
///     loading <c>VoicemeeterRemote64.dll</c> in its own process.
/// </summary>
internal static class VoicemeeterHubProtocol
{
    public const int Version = 1;
    public const int DefaultPort = 50505;
    public const string PortEnvironmentVariable = "VOICEMEETER_HUB_PORT";
    public const string ExeEnvironmentVariable = "VOICEMEETER_HUB_EXE";

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string EndpointFilePath()
    {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(baseDir, "voicemeeter-hub", "endpoint.json");
    }

    public static int ResolvePort()
    {
        var raw = Environment.GetEnvironmentVariable(PortEnvironmentVariable);
        return int.TryParse(raw, out var port) && port is > 0 and < 65536 ? port : DefaultPort;
    }
}

internal sealed record HubRequestFrame(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("op")] string Op,
    [property: JsonPropertyName("args")] Dictionary<string, JsonElement>? Args = null);

internal sealed record HubResponseFrame(
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("result")] JsonElement? Result,
    [property: JsonPropertyName("error")] string? Error);

internal sealed record HubEndpointInfo(
    [property: JsonPropertyName("port")] int Port,
    [property: JsonPropertyName("pid")] int Pid,
    [property: JsonPropertyName("protocol")] int Protocol);
