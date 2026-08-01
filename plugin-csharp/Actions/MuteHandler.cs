using StreamDockSDK;
using StreamDockSDK.Attributes;

namespace StreamDockVoicemeeter.Actions;

[SDAction(
    Uuid = "mute",
    Name = "Voicemeeter Mute",
    Icon = "icons/plugin",
    Tooltip = "Toggle mute for a Voicemeeter strip or bus directly.",
    Controllers = ["Keypad"],
    PropertyInspectorPath = "property-inspector.html"
)]
[SDActionState(Image = "icons/plugin", Title = "Mute")]
public sealed class MuteHandler(
    StreamDockConnection connection,
    string context,
    Dictionary<string, object>? settings) : VoicemeeterActionHandler(connection, context, settings)
{
    public override Task OnWillAppearAsync()
    {
        Log.Info($"Mute willAppear context={Context} channelKey={VmSettings.ChannelKey}");
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
            var state = await Client.GetChannelStateAsync(VmSettings.ChannelKind, VmSettings.ChannelIndex, DisposeToken);
            var nextMuted = !state.Muted;
            var result = await Client.SetMuteAsync(VmSettings.ChannelKind, VmSettings.ChannelIndex, nextMuted, DisposeToken);
            if (!result.Success)
            {
                await ShowErrorAsync(result.ErrorSummary ?? "Voicemeeter mute update failed");
                return;
            }

            await SetStateAsync(nextMuted ? 1 : 0);
            await ShowStateAsync(state with { Muted = nextMuted });
            await RefreshSharedStateAsync();
            Log.Info($"Mute toggled context={Context} channelKey={VmSettings.ChannelKey} muted={nextMuted}");
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(ex);
        }
    }

    private async Task RefreshAsync()
    {
        try
        {
            var state = TryGetCachedState() ?? await Client.GetChannelStateAsync(VmSettings.ChannelKind, VmSettings.ChannelIndex, DisposeToken);
            await SetStateAsync(state.Muted ? 1 : 0);
            await ShowStateAsync(state);
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(ex);
        }
    }

    private VoicemeeterChannelState? TryGetCachedState()
    {
        if (VoicemeeterRuntime.State.Current?.TryGetState(VmSettings.ChannelKey, out var state) == true && state.Ok)
            return new VoicemeeterChannelState(state.GainDb ?? 0, state.Muted ?? false);
        return null;
    }
}
