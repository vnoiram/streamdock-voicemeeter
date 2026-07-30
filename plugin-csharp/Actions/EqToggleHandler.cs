using StreamDockSDK;
using StreamDockSDK.Attributes;

namespace StreamDockVoicemeeter.Actions;

[SDAction(
    Uuid = "eq-toggle",
    Name = "Voicemeeter EQ Toggle",
    Icon = "icons/plugin",
    Tooltip = "Toggle a Voicemeeter bus EQ on or off.",
    Controllers = ["Keypad"],
    PropertyInspectorPath = "property-inspector.html"
)]
[SDActionState(Image = "icons/plugin", Title = "EQ")]
public sealed class EqToggleHandler(
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
            var current = await Client.GetEqStateAsync(VmSettings.ChannelIndex, DisposeToken);
            var next = !current;
            var result = await Client.SetEqAsync(VmSettings.ChannelIndex, next, DisposeToken);
            if (!result.Success)
            {
                await ShowErrorAsync(result.ErrorSummary ?? "Voicemeeter EQ update failed");
                return;
            }

            await SetStateAsync(next ? 1 : 0);
            await SetTitleAsync($"{DisplayLabel}\n{(next ? "EQ On" : "EQ Off")}");
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
            var current = await Client.GetEqStateAsync(VmSettings.ChannelIndex, DisposeToken);
            await SetStateAsync(current ? 1 : 0);
            await SetTitleAsync($"{DisplayLabel}\n{(current ? "EQ On" : "EQ Off")}");
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(ex.Message);
        }
    }

    private string DisplayLabel => VmSettings.TitleLabel ?? VoicemeeterSettings.DisplayNameFor("bus", VmSettings.ChannelIndex);
}
