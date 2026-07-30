using StreamDockSDK;
using StreamDockSDK.Attributes;

namespace StreamDockVoicemeeter.Actions;

[SDAction(
    Uuid = "solo",
    Name = "Voicemeeter Solo",
    Icon = "icons/plugin",
    Tooltip = "Toggle solo for a Voicemeeter strip directly.",
    Controllers = ["Keypad"],
    PropertyInspectorPath = "property-inspector.html"
)]
[SDActionState(Image = "icons/plugin", Title = "Solo")]
public sealed class SoloHandler(
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
            var current = await Client.GetSoloAsync(VmSettings.ChannelIndex, DisposeToken);
            var next = !current;
            var result = await Client.SetSoloAsync(VmSettings.ChannelIndex, next, DisposeToken);
            if (!result.Success)
            {
                await ShowErrorAsync(result.ErrorSummary ?? "Voicemeeter solo update failed");
                return;
            }

            await SetStateAsync(next ? 1 : 0);
            await SetTitleAsync($"{DisplayLabel}\n{(next ? "Soloed" : "Solo")}");
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
            var current = await Client.GetSoloAsync(VmSettings.ChannelIndex, DisposeToken);
            await SetStateAsync(current ? 1 : 0);
            await SetTitleAsync($"{DisplayLabel}\n{(current ? "Soloed" : "Solo")}");
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(ex.Message);
        }
    }

    private string DisplayLabel => VmSettings.TitleLabel ?? VoicemeeterSettings.DisplayNameFor("strip", VmSettings.ChannelIndex);
}
