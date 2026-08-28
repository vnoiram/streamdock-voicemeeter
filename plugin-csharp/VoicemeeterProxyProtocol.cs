using System.Text.Json;
using System.Text.Json.Serialization;

namespace StreamDockVoicemeeter;

internal static class VoicemeeterProxyProtocol
{
    public const string BrokerArgument = "--voicemeeter-proxy";
    public const string PipeName = "StreamDockVoicemeeter.Proxy.v1";
    public const string MutexName = @"Local\StreamDockVoicemeeter.Proxy.v1";

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
}

internal sealed record VoicemeeterProxyRequest(string Operation, Dictionary<string, JsonElement>? Args = null);

internal sealed record VoicemeeterProxyResponse(JsonElement? Result, string? Error)
{
    public static VoicemeeterProxyResponse Ok<T>(T value)
    {
        return new VoicemeeterProxyResponse(JsonSerializer.SerializeToElement(value, VoicemeeterProxyProtocol.JsonOptions), null);
    }

    public static VoicemeeterProxyResponse Fail(Exception ex)
    {
        return new VoicemeeterProxyResponse(null, ex.Message);
    }
}
