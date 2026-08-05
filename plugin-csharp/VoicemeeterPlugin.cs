using System.Reflection;
using System.Text.Json;
using log4net;
using StreamDockSDK;
using StreamDockSDK.Attributes;
using StreamDockSDK.Events;

namespace StreamDockVoicemeeter;

[SDPlugin(
    PackageId = "local.streamdock.voicemeeter",
    SdkVersion = 1,
    Name = "Stream Dock Voicemeeter",
    Version = "0.1.15",
    Author = "local",
    Description = "Control VB-Audio Voicemeeter (Standard/Banana/Potato) strips and buses directly.",
    Category = "Voicemeeter",
    CategoryIcon = "icons/plugin",
    Icon = "icons/plugin",
    CodePath = "plugin/StreamDockVoicemeeter.exe",
    CodePathWin = "plugin/StreamDockVoicemeeter.exe",
    PropertyInspectorPath = "property-inspector.html"
)]
[SDPluginOS(Platform = "windows", MinimumVersion = "10")]
public sealed class VoicemeeterPlugin : StreamDockPlugin
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(VoicemeeterPlugin));

    public override void Dispose()
    {
        try
        {
            base.Dispose();
        }
        finally
        {
            VoicemeeterRuntime.Dispose();
        }
    }

    public override void RegisterEventHandlers()
    {
        base.RegisterEventHandlers();
        Connection.Connected += (_, _) =>
        {
            Log.Info("Connected to Stream Dock; discovering Voicemeeter action handlers");
            HandlerManager.DiscoverHandlers(Assembly.GetExecutingAssembly());
        };
        Connection.SendToPlugin += async (_, e) => await OnFallbackSendToPluginAsync(e);
        Connection.Disconnected += (_, _) => Log.Warn("Disconnected from Stream Dock");
    }

    private async Task OnFallbackSendToPluginAsync(SendToPluginEventArgs e)
    {
        if (e.Payload.ValueKind != JsonValueKind.Object ||
            !e.Payload.TryGetProperty("command", out var commandElement) ||
            commandElement.ValueKind != JsonValueKind.String)
            return;

        var command = commandElement.GetString();
        var replyContext = ReadReplyContext(e.Payload, e.Context);
        Log.Info($"Fallback sendToPlugin command={command} action={e.Action} context={e.Context} replyContext={replyContext}");
        try
        {
            switch (command)
            {
                case "devices":
                    await SendDevicesAsync(e, replyContext);
                    break;
                case "macroStatus":
                    await SendMacroStatusAsync(e, replyContext);
                    break;
                case "diagnostics":
                    await SendDiagnosticsAsync(replyContext);
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Fallback sendToPlugin failed command={command}: {ex.Message}");
        }
    }

    private async Task SendDevicesAsync(SendToPluginEventArgs e, string replyContext)
    {
        try
        {
            var dataFlow = ReadString(e.Payload, "dataFlow") ?? "render";
            var devices = string.Equals(dataFlow, "capture", StringComparison.OrdinalIgnoreCase)
                ? await VoicemeeterRuntime.Client.GetInputDevicesAsync()
                : await VoicemeeterRuntime.Client.GetOutputDevicesAsync();
            Log.Info($"Fallback devices response dataFlow={dataFlow} count={devices.Count} replyContext={replyContext}");
            await Connection.SendToPropertyInspectorAsync(replyContext, new
            {
                type = "devices",
                dataFlow = string.Equals(dataFlow, "capture", StringComparison.OrdinalIgnoreCase) ? "capture" : "render",
                devices = devices.Select(device => new
                {
                    id = device.CompositeId,
                    name = device.Name,
                    driver = device.DriverParamValue,
                    hardwareId = device.HardwareId
                }).ToArray()
            });
        }
        catch (Exception ex)
        {
            Log.Warn($"Fallback devices request failed replyContext={replyContext}: {ex.Message}");
            await Connection.SendToPropertyInspectorAsync(replyContext, new
            {
                type = "error",
                source = "devices",
                message = ex.Message
            });
        }
    }

    private async Task SendMacroStatusAsync(SendToPluginEventArgs e, string replyContext)
    {
        try
        {
            var index = ReadInt(e.Payload, "macroButtonIndex") ?? 0;
            var on = await VoicemeeterRuntime.Client.GetMacroButtonStateAsync(index);
            await Connection.SendToPropertyInspectorAsync(replyContext, new
            {
                type = "macroStatus",
                macroButtonIndex = index,
                on
            });
        }
        catch (Exception ex)
        {
            Log.Warn($"Fallback macroStatus request failed replyContext={replyContext}: {ex.Message}");
            await Connection.SendToPropertyInspectorAsync(replyContext, new
            {
                type = "error",
                source = "macroStatus",
                message = ex.Message
            });
        }
    }

    private async Task SendDiagnosticsAsync(string replyContext)
    {
        var diagnostics = await VoicemeeterRuntime.Client.BuildDiagnosticsAsync();
        await Connection.SendToPropertyInspectorAsync(replyContext, new
        {
            type = "diagnostics",
            diagnostics
        });
    }

    private static string ReadReplyContext(JsonElement payload, string fallback)
    {
        return ReadString(payload, "replyContext") ?? fallback;
    }

    private static string? ReadString(JsonElement payload, string propertyName)
    {
        return payload.ValueKind == JsonValueKind.Object &&
               payload.TryGetProperty(propertyName, out var value) &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static int? ReadInt(JsonElement payload, string propertyName)
    {
        if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty(propertyName, out var value))
            return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) return number;
        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out var parsed)) return parsed;
        return null;
    }
}
