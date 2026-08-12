using System.Runtime.CompilerServices;
using System.Text.Json;
using log4net;
using StreamDockSDK;
using StreamDockSDK.Actions;

namespace StreamDockVoicemeeter.Actions;

public abstract class VoicemeeterActionHandler : ActionHandler
{
    protected readonly ILog Log = LogManager.GetLogger(typeof(VoicemeeterActionHandler));
    protected readonly VoicemeeterClient Client = VoicemeeterRuntime.Client;
    protected VoicemeeterSettings VmSettings { get; private set; }

    protected VoicemeeterActionHandler(StreamDockConnection connection, string context, Dictionary<string, object>? settings)
        : base(connection, context, settings)
    {
        VmSettings = VoicemeeterSettings.FromDictionary(settings);
        VoicemeeterRuntime.State.StateChanged += OnRuntimeStateChangedAsync;
    }

    public override void UpdateSettings(Dictionary<string, object>? settings)
    {
        base.UpdateSettings(settings);
        VmSettings = VoicemeeterSettings.FromDictionary(settings);
    }

    public override async Task OnSettingsChangedAsync(Dictionary<string, object> settings)
    {
        UpdateSettings(settings);
        Log.Info($"Settings changed context={Context} channelKey={VmSettings.ChannelKey} step={VmSettings.Step} invert={VmSettings.InvertKnob}");
        await UpdateDisplayAsync();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) VoicemeeterRuntime.State.StateChanged -= OnRuntimeStateChangedAsync;
        base.Dispose(disposing);
    }

    protected virtual Task OnRuntimeStateChangedAsync(VoicemeeterSnapshot snapshot)
    {
        return UpdateDisplayAsync();
    }

    public override async Task OnSendToPluginAsync(JsonElement payload)
    {
        var replyContext = ReadReplyContext(payload);
        if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty("command", out var commandElement) ||
            commandElement.ValueKind != JsonValueKind.String)
            return;

        var command = commandElement.GetString();
        Log.Info($"Action sendToPlugin context={Context} command={command} replyContext={replyContext}");
        switch (command)
        {
            case "diagnostics":
                await SendDiagnosticsAsync(replyContext);
                break;
            case "devices":
                var dataFlow = ReadString(payload, "dataFlow") ?? "render";
                await SendDevicesAsync(dataFlow, replyContext);
                break;
            case "macroStatus":
                var index = ReadInt(payload, "macroButtonIndex") ?? VmSettings.MacroButtonIndex;
                await SendMacroStatusAsync(index, replyContext);
                break;
        }
    }

    protected string Label => VmSettings.DisplayName;

    protected async Task ShowStateAsync(VoicemeeterChannelState state)
    {
        var rounded = Math.Round(state.GainDb);
        var gainText = rounded > 0 ? $"+{rounded:0}dB" : $"{rounded:0}dB";
        var display = state.Muted ? "Muted" : gainText;
        await SetTitleAsync($"{Label}\n{display}");
        await Connection.SetFeedbackAsync(Context, new Dictionary<string, object>
        {
            ["title"] = Label,
            ["value"] = rounded,
            ["indicator"] = state.Muted ? -60 : rounded,
            ["muted"] = state.Muted
        });
    }

    protected async Task ShowErrorAsync(string message)
    {
        Log.Warn($"Action error context={Context} channelKey={VmSettings.ChannelKey}: {message}");
        await SetTitleAsync($"{Label}\nError");
        await ShowAlertAsync();
        await Connection.SendToPropertyInspectorAsync(Context, new
        {
            type = "error",
            message
        });
    }

    /// <summary>
    ///     Same as <see cref="ShowErrorAsync(string)" />, but logs the full exception (type,
    ///     message, stack trace) instead of just its message, so failures caught in a catch
    ///     block can actually be diagnosed from streamdock-voicemeeter.log instead of only
    ///     showing a one-line summary on the key/property inspector.
    /// </summary>
    protected Task ShowErrorAsync(Exception ex, [CallerMemberName] string? memberName = null)
    {
        Log.Warn($"Action exception context={Context} channelKey={VmSettings.ChannelKey} member={memberName}: {ex}");
        return ShowErrorAsync(ex.Message);
    }

    protected async Task SendDiagnosticsAsync(string? replyContext = null)
    {
        var diagnostics = await Client.BuildDiagnosticsAsync(DisposeToken);
        await Connection.SendToPropertyInspectorAsync(replyContext ?? Context, new
        {
            type = "diagnostics",
            diagnostics,
            settings = new
            {
                VmSettings.ChannelKind,
                VmSettings.ChannelIndex,
                VmSettings.Step,
                VmSettings.TitleLabel,
                VmSettings.InvertKnob
            }
        });
    }

    protected async Task SendDevicesAsync(string dataFlow, string replyContext)
    {
        try
        {
            var devices = string.Equals(dataFlow, "capture", StringComparison.OrdinalIgnoreCase)
                ? await Client.GetInputDevicesAsync(DisposeToken)
                : await Client.GetOutputDevicesAsync(DisposeToken);
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
            Log.Warn($"Action devices request failed context={Context} replyContext={replyContext}: {ex}");
            await Connection.SendToPropertyInspectorAsync(replyContext, new
            {
                type = "error",
                source = "devices",
                message = ex.Message
            });
        }
    }

    protected async Task SendMacroStatusAsync(int index, string replyContext)
    {
        try
        {
            var on = await Client.GetMacroButtonStateAsync(index, DisposeToken);
            await Connection.SendToPropertyInspectorAsync(replyContext, new
            {
                type = "macroStatus",
                macroButtonIndex = index,
                on
            });
        }
        catch (Exception ex)
        {
            Log.Warn($"Action macroStatus request failed context={Context} replyContext={replyContext}: {ex}");
            await Connection.SendToPropertyInspectorAsync(replyContext, new
            {
                type = "error",
                source = "macroStatus",
                message = ex.Message
            });
        }
    }

    protected Task RefreshSharedStateAsync()
    {
        return VoicemeeterRuntime.State.RefreshAsync(DisposeToken);
    }

    private string ReadReplyContext(JsonElement payload)
    {
        var value = ReadString(payload, "replyContext");
        return string.IsNullOrWhiteSpace(value) ? Context : value;
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
