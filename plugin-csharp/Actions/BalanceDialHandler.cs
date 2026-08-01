using StreamDockSDK;
using StreamDockSDK.Attributes;

namespace StreamDockVoicemeeter.Actions;

[SDAction(
    Uuid = "balance-dial",
    Name = "Voicemeeter Balance Dial",
    Icon = "icons/plugin",
    Tooltip = "Simulate a ChatMix-style balance by moving two Voicemeeter channel gains in opposite directions.",
    Controllers = ["Knob"],
    PropertyInspectorPath = "property-inspector.html"
)]
[SDActionState(Image = "icons/plugin", Title = "Balance")]
public sealed class BalanceDialHandler(
    StreamDockConnection connection,
    string context,
    Dictionary<string, object>? settings) : VoicemeeterActionHandler(connection, context, settings)
{
    private readonly object _lock = new();
    private int _pendingTicks;
    private Timer? _debounceTimer;

    public override Task OnWillAppearAsync()
    {
        Log.Info($"BalanceDial willAppear context={Context} primary={VmSettings.BalancePrimaryKind}:{VmSettings.BalancePrimaryIndex} " +
                 $"secondary={VmSettings.BalanceSecondaryKind}:{VmSettings.BalanceSecondaryIndex}");
        return SetTitleAsync(TitleLabel);
    }

    public override Task UpdateDisplayAsync()
    {
        return SetTitleAsync(TitleLabel);
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

    public override Task OnDialDownAsync()
    {
        return ResetAsync();
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
        var delta = ticks * VmSettings.BalanceStep;
        try
        {
            var primary = await Client.GetChannelStateAsync(VmSettings.BalancePrimaryKind, VmSettings.BalancePrimaryIndex, DisposeToken);
            var secondary = await Client.GetChannelStateAsync(VmSettings.BalanceSecondaryKind, VmSettings.BalanceSecondaryIndex, DisposeToken);
            var nextPrimary = Math.Clamp(primary.GainDb + delta, -60.0, 12.0);
            var nextSecondary = Math.Clamp(secondary.GainDb - delta, -60.0, 12.0);
            var resultA = await Client.SetGainAsync(VmSettings.BalancePrimaryKind, VmSettings.BalancePrimaryIndex, nextPrimary, DisposeToken);
            var resultB = await Client.SetGainAsync(VmSettings.BalanceSecondaryKind, VmSettings.BalanceSecondaryIndex, nextSecondary, DisposeToken);
            if (!resultA.Success || !resultB.Success)
            {
                await ShowErrorAsync(resultA.ErrorSummary ?? resultB.ErrorSummary ?? "Voicemeeter balance update failed");
                return;
            }

            await SetTitleAsync($"{TitleLabel}\n{FormatDb(nextPrimary)}/{FormatDb(nextSecondary)}");
            await RefreshSharedStateAsync();
            Log.Info($"BalanceDial applied context={Context} delta={delta} primary={nextPrimary} secondary={nextSecondary}");
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(ex);
        }
    }

    private async Task ResetAsync()
    {
        try
        {
            var resultA = await Client.SetGainAsync(VmSettings.BalancePrimaryKind, VmSettings.BalancePrimaryIndex, 0.0, DisposeToken);
            var resultB = await Client.SetGainAsync(VmSettings.BalanceSecondaryKind, VmSettings.BalanceSecondaryIndex, 0.0, DisposeToken);
            if (!resultA.Success || !resultB.Success)
            {
                await ShowErrorAsync(resultA.ErrorSummary ?? resultB.ErrorSummary ?? "Voicemeeter balance reset failed");
                return;
            }

            await SetTitleAsync($"{TitleLabel}\n0/0dB");
            await RefreshSharedStateAsync();
            Log.Info($"BalanceDial reset context={Context}");
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(ex);
        }
    }

    private string TitleLabel => VmSettings.TitleLabel ?? "Balance";

    private static string FormatDb(double value)
    {
        var rounded = Math.Round(value);
        return rounded > 0 ? $"+{rounded:0}" : $"{rounded:0}";
    }
}
