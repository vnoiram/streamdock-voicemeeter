using StreamDockSDK;
using StreamDockSDK.Attributes;

namespace StreamDockVoicemeeter.Actions;

[SDAction(
    Uuid = "macro-button",
    Name = "Voicemeeter Macro Button",
    Icon = "icons/plugin",
    Tooltip = "Trigger or toggle a Voicemeeter MacroButtons logical button.",
    Controllers = ["Keypad"],
    PropertyInspectorPath = "property-inspector.html"
)]
[SDActionState(Image = "icons/plugin", Title = "Macro")]
public sealed class MacroButtonHandler(
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
            var result = await Client.PressMacroButtonAsync(VmSettings.MacroButtonIndex, true, DisposeToken);
            if (!result.Success) await ShowErrorAsync(result.ErrorSummary ?? "Voicemeeter macro button press failed");
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(ex.Message);
        }
    }

    public override async Task OnKeyUpAsync()
    {
        try
        {
            var result = await Client.PressMacroButtonAsync(VmSettings.MacroButtonIndex, false, DisposeToken);
            if (!result.Success)
            {
                await ShowErrorAsync(result.ErrorSummary ?? "Voicemeeter macro button release failed");
                return;
            }

            await RefreshAsync();
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
            var on = await Client.GetMacroButtonStateAsync(VmSettings.MacroButtonIndex, DisposeToken);
            await SetStateAsync(on ? 1 : 0);
            await SetTitleAsync($"Macro {VmSettings.MacroButtonIndex}\n{(on ? "On" : "Off")}");
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(ex.Message);
        }
    }
}
