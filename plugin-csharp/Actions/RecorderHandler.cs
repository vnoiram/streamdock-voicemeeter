using StreamDockSDK;
using StreamDockSDK.Attributes;

namespace StreamDockVoicemeeter.Actions;

[SDAction(
    Uuid = "recorder",
    Name = "Voicemeeter Recorder",
    Icon = "icons/plugin",
    Tooltip = "Toggle Voicemeeter's built-in recorder.",
    Controllers = ["Keypad"],
    PropertyInspectorPath = "property-inspector.html"
)]
[SDActionState(Image = "icons/plugin", Title = "Record")]
public sealed class RecorderHandler(
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
            var current = await Client.GetRecorderStateAsync(DisposeToken);
            var next = !current;
            var result = await Client.SetRecorderAsync(next, DisposeToken);
            if (!result.Success)
            {
                await ShowErrorAsync(result.ErrorSummary ?? "Voicemeeter recorder update failed");
                return;
            }

            await SetStateAsync(next ? 1 : 0);
            await SetTitleAsync(next ? "Recording" : "Record");
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
            var current = await Client.GetRecorderStateAsync(DisposeToken);
            await SetStateAsync(current ? 1 : 0);
            await SetTitleAsync(current ? "Recording" : "Record");
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(ex.Message);
        }
    }
}
