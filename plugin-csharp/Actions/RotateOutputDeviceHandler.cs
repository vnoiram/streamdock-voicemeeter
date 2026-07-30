using StreamDockSDK;
using StreamDockSDK.Attributes;

namespace StreamDockVoicemeeter.Actions;

[SDAction(
    Uuid = "rotate-output-device",
    Name = "Voicemeeter Rotate Output",
    Icon = "icons/plugin",
    Tooltip = "Rotate a Voicemeeter bus to the next hardware output device.",
    Controllers = ["Keypad", "Knob"],
    PropertyInspectorPath = "property-inspector.html"
)]
[SDActionState(Image = "icons/plugin", Title = "Output")]
public sealed class RotateOutputDeviceHandler(
    StreamDockConnection connection,
    string context,
    Dictionary<string, object>? settings) : VoicemeeterActionHandler(connection, context, settings)
{
    private readonly object _lock = new();
    private int _position = -1;
    private int _pendingSteps;
    private Timer? _debounceTimer;

    public override Task OnWillAppearAsync()
    {
        return SetTitleAsync(Label);
    }

    public override Task UpdateDisplayAsync()
    {
        return SetTitleAsync(Label);
    }

    public override Task OnKeyDownAsync()
    {
        QueueStep(1);
        return Task.CompletedTask;
    }

    public override Task OnDialRotateAsync(int ticks, bool pressed)
    {
        QueueStep(VmSettings.InvertKnob ? -ticks : ticks);
        return Task.CompletedTask;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _debounceTimer?.Dispose();
        base.Dispose(disposing);
    }

    private void QueueStep(int step)
    {
        lock (_lock)
        {
            _pendingSteps += step;
            _debounceTimer?.Dispose();
            _debounceTimer = new Timer(_ => _ = ApplyPendingRotateAsync(), null, 80, Timeout.Infinite);
        }
    }

    private async Task ApplyPendingRotateAsync()
    {
        int steps;
        lock (_lock)
        {
            steps = _pendingSteps;
            _pendingSteps = 0;
        }

        if (steps == 0) return;
        await RotateAsync(steps);
    }

    private async Task RotateAsync(int step)
    {
        if (step == 0) return;
        try
        {
            var devices = await Client.GetOutputDevicesAsync(DisposeToken);
            if (devices.Count == 0)
            {
                await ShowErrorAsync("No output devices found");
                return;
            }

            _position = (_position < 0 ? 0 : _position) + step;
            _position %= devices.Count;
            if (_position < 0) _position += devices.Count;

            var device = devices[_position];
            var result = await Client.SetDeviceAsync(VmSettings.ChannelKind, VmSettings.ChannelIndex, device.DriverParamValue, device.Name, DisposeToken);
            if (!result.Success)
            {
                await ShowErrorAsync(result.ErrorSummary ?? "Voicemeeter output device update failed");
                return;
            }

            await SetTitleAsync($"{Label}\n{device.Name}");
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(ex.Message);
        }
    }
}
