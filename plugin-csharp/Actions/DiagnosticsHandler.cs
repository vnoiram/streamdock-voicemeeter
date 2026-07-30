using StreamDockSDK;
using StreamDockSDK.Attributes;

namespace StreamDockVoicemeeter.Actions;

[SDAction(
    Uuid = "diagnostics",
    Name = "Diagnostics",
    Icon = "icons/plugin",
    Tooltip = "Show Voicemeeter discovery, login, and request diagnostics.",
    Controllers = ["Keypad"],
    PropertyInspectorPath = "property-inspector.html"
)]
[SDActionState(Image = "icons/plugin", Title = "Diag")]
public sealed class DiagnosticsHandler(
    StreamDockConnection connection,
    string context,
    Dictionary<string, object>? settings) : VoicemeeterActionHandler(connection, context, settings)
{
    public override Task OnWillAppearAsync()
    {
        return SetTitleAsync("Diag");
    }

    public override Task UpdateDisplayAsync()
    {
        return SetTitleAsync("Diag");
    }

    public override async Task OnKeyDownAsync()
    {
        await SendDiagnosticsAsync();
        await ShowOkAsync();
    }
}
