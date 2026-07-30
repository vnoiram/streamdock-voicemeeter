using StreamDockSDK;
using StreamDockSDK.Attributes;

namespace StreamDockVoicemeeter.Actions;

[SDAction(
    Uuid = "app-control",
    Name = "Voicemeeter App Control",
    Icon = "icons/plugin",
    Tooltip = "Show, restart, or shut down the Voicemeeter application.",
    Controllers = ["Keypad"],
    PropertyInspectorPath = "property-inspector.html"
)]
[SDActionState(Image = "icons/plugin", Title = "App")]
public sealed class AppControlHandler(
    StreamDockConnection connection,
    string context,
    Dictionary<string, object>? settings) : VoicemeeterActionHandler(connection, context, settings)
{
    public override Task OnWillAppearAsync()
    {
        return SetTitleAsync(TitleFor(VmSettings.AppCommand));
    }

    public override Task UpdateDisplayAsync()
    {
        return SetTitleAsync(TitleFor(VmSettings.AppCommand));
    }

    public override async Task OnKeyDownAsync()
    {
        try
        {
            var commandName = VmSettings.AppCommand switch
            {
                "restart" => "Restart",
                "shutdown" => "Shutdown",
                _ => "Show"
            };
            var result = await Client.TriggerCommandAsync(commandName, DisposeToken);
            if (!result.Success)
            {
                await ShowErrorAsync(result.ErrorSummary ?? "Voicemeeter app command failed");
                return;
            }

            await ShowOkAsync();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(ex.Message);
        }
    }

    private static string TitleFor(string command)
    {
        return command switch
        {
            "restart" => "Restart",
            "shutdown" => "Shutdown",
            _ => "Show"
        };
    }
}
