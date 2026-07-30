using StreamDockSDK;
using StreamDockSDK.Attributes;

namespace StreamDockVoicemeeter.Actions;

[SDAction(
    Uuid = "gain",
    Name = "Voicemeeter Gain",
    Icon = "icons/plugin",
    Tooltip = "Adjust a Voicemeeter strip or bus gain directly.",
    Controllers = ["Keypad", "Knob"],
    PropertyInspectorPath = "property-inspector.html"
)]
[SDActionState(Image = "icons/plugin", Title = "Gain")]
public sealed class GainHandler(
    StreamDockConnection connection,
    string context,
    Dictionary<string, object>? settings) : VoicemeeterActionHandler(connection, context, settings)
{
    private readonly object _lock = new();
    private int _pendingTicks;
    private Timer? _debounceTimer;

    public override Task OnWillAppearAsync()
    {
        Log.Info($"Gain willAppear context={Context} channelKey={VmSettings.ChannelKey}");
        return RefreshAsync();
    }

    public override Task UpdateDisplayAsync()
    {
        return RefreshAsync();
    }

    public override Task OnDialRotateAsync(int ticks, bool pressed)
    {
        lock (_lock)
        {
            _pendingTicks += VmSettings.InvertKnob ? -ticks : ticks;
            _debounceTimer?.Dispose();
            _debounceTimer = new Timer(_ => _ = ApplyPendingRotateAsync(), null, 80, Timeout.Infinite);
        }

        return Task.CompletedTask;
    }

    public override Task OnKeyDownAsync()
    {
        return ApplyDeltaAsync(VmSettings.Step);
    }

    public override Task OnDialDownAsync()
    {
        return ToggleMuteAsync();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _debounceTimer?.Dispose();
        base.Dispose(disposing);
    }

    private async Task ApplyPendingRotateAsync()
    {
        int ticks;
        lock (_lock)
        {
            ticks = _pendingTicks;
            _pendingTicks = 0;
        }

        if (ticks == 0) return;
        await ApplyDeltaAsync(ticks * VmSettings.Step);
    }

    private async Task ApplyDeltaAsync(double delta)
    {
        try
        {
            var state = await Client.GetChannelStateAsync(VmSettings.ChannelKind, VmSettings.ChannelIndex, DisposeToken);
            var next = Math.Clamp(state.GainDb + delta, -60.0, 12.0);
            var result = await Client.SetGainAsync(VmSettings.ChannelKind, VmSettings.ChannelIndex, next, DisposeToken);
            if (!result.Success)
            {
                await ShowErrorAsync(result.ErrorSummary ?? "Voicemeeter gain update failed");
                return;
            }

            await ShowStateAsync(new VoicemeeterChannelState(next, state.Muted));
            await RefreshSharedStateAsync();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(ex.Message);
        }
    }

    private async Task ToggleMuteAsync()
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

            await ShowStateAsync(state with { Muted = nextMuted });
            await RefreshSharedStateAsync();
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
            var state = TryGetCachedState() ?? await Client.GetChannelStateAsync(VmSettings.ChannelKind, VmSettings.ChannelIndex, DisposeToken);
            await ShowStateAsync(state);
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(ex.Message);
        }
    }

    private VoicemeeterChannelState? TryGetCachedState()
    {
        if (VoicemeeterRuntime.State.Current?.TryGetState(VmSettings.ChannelKey, out var state) == true && state.Ok)
            return new VoicemeeterChannelState(state.GainDb ?? 0, state.Muted ?? false);
        return null;
    }
}
