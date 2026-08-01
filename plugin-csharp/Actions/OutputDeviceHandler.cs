using StreamDockSDK;
using StreamDockSDK.Attributes;

namespace StreamDockVoicemeeter.Actions;

[SDAction(
    Uuid = "output-device",
    Name = "Voicemeeter Output Device",
    Icon = "icons/plugin",
    Tooltip = "Switch a Voicemeeter bus hardware output device.",
    Controllers = ["Keypad"],
    PropertyInspectorPath = "property-inspector.html"
)]
[SDActionState(Image = "icons/plugin", Title = "Output")]
public sealed class OutputDeviceHandler(
    StreamDockConnection connection,
    string context,
    Dictionary<string, object>? settings) : VoicemeeterActionHandler(connection, context, settings)
{
    public override Task OnWillAppearAsync()
    {
        Log.Info($"OutputDevice willAppear context={Context} channelKey={VmSettings.ChannelKey} deviceId={VmSettings.DeviceId}");
        return SetTitleAsync(Label);
    }

    public override Task UpdateDisplayAsync()
    {
        return SetTitleAsync(Label);
    }

    public override async Task OnKeyDownAsync()
    {
        var parsed = ParseDeviceId(VmSettings.DeviceId);
        if (parsed == null)
        {
            await ShowErrorAsync("No output device configured");
            return;
        }

        try
        {
            var result = await Client.SetDeviceAsync(VmSettings.ChannelKind, VmSettings.ChannelIndex, parsed.Value.Driver, parsed.Value.Name, DisposeToken);
            if (!result.Success)
            {
                await ShowErrorAsync(result.ErrorSummary ?? "Voicemeeter output device update failed");
                return;
            }

            await ShowOkAsync();
            await SetTitleAsync($"{Label}\n{parsed.Value.Name}");
            Log.Info($"OutputDevice set context={Context} channelKey={VmSettings.ChannelKey} device={parsed.Value.Name}");
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(ex);
        }
    }

    private static (string Driver, string Name)? ParseDeviceId(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) return null;
        var separatorIndex = deviceId.IndexOf(':');
        if (separatorIndex <= 0 || separatorIndex == deviceId.Length - 1) return null;
        return (deviceId[..separatorIndex], deviceId[(separatorIndex + 1)..]);
    }
}
