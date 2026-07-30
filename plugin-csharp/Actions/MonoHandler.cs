using StreamDockSDK;
using StreamDockSDK.Attributes;

namespace StreamDockVoicemeeter.Actions;

[SDAction(
    Uuid = "mono",
    Name = "Voicemeeter Mono",
    Icon = "icons/plugin",
    Tooltip = "Toggle mono downmix for a Voicemeeter strip or bus directly.",
    Controllers = ["Keypad"],
    PropertyInspectorPath = "property-inspector.html"
)]
[SDActionState(Image = "icons/plugin", Title = "Mono")]
public sealed class MonoHandler(
    StreamDockConnection connection,
    string context,
    Dictionary<string, object>? settings) : VoicemeeterActionHandler(connection, context, settings)
{
    public override Task OnWillAppearAsync()
    {
        return RefreshAsync();
    }

    public override Task UpdateDisplayAsync()
    {
        return RefreshAsync();
    }

    public override async Task OnKeyDownAsync()
    {
        try
        {
            var current = await Client.GetMonoAsync(VmSettings.ChannelKind, VmSettings.ChannelIndex, DisposeToken);
            var next = !current;
            var result = await Client.SetMonoAsync(VmSettings.ChannelKind, VmSettings.ChannelIndex, next, DisposeToken);
            if (!result.Success)
            {
                await ShowErrorAsync(result.ErrorSummary ?? "Voicemeeter mono update failed");
                return;
            }

            await SetStateAsync(next ? 1 : 0);
            await SetTitleAsync($"{Label}\n{(next ? "Mono On" : "Mono")}");
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(ex.Message);
        }
    }

    private async Task RefreshAsync()
    {
        try
        {
            var current = await Client.GetMonoAsync(VmSettings.ChannelKind, VmSettings.ChannelIndex, DisposeToken);
            await SetStateAsync(current ? 1 : 0);
            await SetTitleAsync($"{Label}\n{(current ? "Mono On" : "Mono")}");
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(ex.Message);
        }
    }
}
