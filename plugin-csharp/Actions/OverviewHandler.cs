using StreamDockSDK;
using StreamDockSDK.Attributes;

namespace StreamDockVoicemeeter.Actions;

[SDAction(
    Uuid = "overview",
    Name = "Voicemeeter Overview",
    Icon = "icons/plugin",
    Tooltip = "Show selected Voicemeeter strip/bus gain and mute states.",
    Controllers = ["Keypad"],
    PropertyInspectorPath = "property-inspector.html"
)]
[SDActionState(Image = "icons/plugin", Title = "")]
public sealed class OverviewHandler(
    StreamDockConnection connection,
    string context,
    Dictionary<string, object>? settings) : VoicemeeterActionHandler(connection, context, settings)
{
    private readonly object _rotateLock = new();
    private Timer? _rotateTimer;
    private int _pageIndex;

    public override Task OnWillAppearAsync()
    {
        Log.Info($"Overview willAppear context={Context} targets={string.Join(",", VmSettings.OverviewTargets)}");
        _pageIndex = 0;
        RestartRotateTimer();
        return RefreshSharedStateAsync();
    }

    public override Task OnWillDisappearAsync()
    {
        StopRotateTimer();
        return Task.CompletedTask;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) StopRotateTimer();
        base.Dispose(disposing);
    }

    public override Task UpdateDisplayAsync()
    {
        return RefreshAsync(false);
    }

    public override Task OnSettingsChangedAsync(Dictionary<string, object> settings)
    {
        UpdateSettings(settings);
        _pageIndex = 0;
        RestartRotateTimer();
        return RefreshAsync(false, false);
    }

    public override async Task OnKeyDownAsync()
    {
        await RefreshSharedStateAsync();
        Log.Info($"Overview keyDown context={Context} rotateMode={VmSettings.OverviewRotateMode} " +
                 $"targetCount={VmSettings.OverviewTargets.Count} pageSize={VmSettings.OverviewPageSize} totalPages={TotalPages}");
        if (VmSettings.OverviewRotateMode == "press" && TotalPages > 1)
        {
            AdvancePage();
            await RefreshAsync(false);
        }
        else
        {
            await ShowOkAsync();
        }
    }

    private int TotalPages => Math.Max(1, (int)Math.Ceiling(VmSettings.OverviewTargets.Count / (double)VmSettings.OverviewPageSize));

    private void AdvancePage()
    {
        var totalPages = TotalPages;
        _pageIndex = totalPages <= 0 ? 0 : (_pageIndex + 1) % totalPages;
    }

    private void RestartRotateTimer()
    {
        StopRotateTimer();
        Log.Info($"Overview restartRotateTimer context={Context} rotateMode={VmSettings.OverviewRotateMode} " +
                 $"targetCount={VmSettings.OverviewTargets.Count} pageSize={VmSettings.OverviewPageSize} totalPages={TotalPages}");
        if (VmSettings.OverviewRotateMode != "time" || TotalPages <= 1) return;
        var interval = TimeSpan.FromSeconds(VmSettings.OverviewRotateSeconds);
        lock (_rotateLock)
        {
            _rotateTimer = new Timer(_ => OnRotateTick(), null, interval, interval);
        }
        Log.Info($"Overview rotate timer started context={Context} intervalSeconds={VmSettings.OverviewRotateSeconds}");
    }

    private void StopRotateTimer()
    {
        lock (_rotateLock)
        {
            _rotateTimer?.Dispose();
            _rotateTimer = null;
        }
    }

    private void OnRotateTick()
    {
        AdvancePage();
        _ = RefreshAsync(false);
    }

    private async Task RefreshAsync(bool showOk, bool useCache = true)
    {
        try
        {
            var states = useCache
                ? TryGetCachedStates() ?? await FetchLiveStatesAsync()
                : await FetchLiveStatesAsync();
            var pageSize = VmSettings.OverviewPageSize;
            var totalPages = Math.Max(1, (int)Math.Ceiling(states.Count / (double)pageSize));
            if (_pageIndex >= totalPages) _pageIndex = 0;
            var page = states.Skip(_pageIndex * pageSize).Take(pageSize).ToArray();
            await SetTitleAsync("");
            await SetImageAsync(VoicemeeterOverviewRenderer.BuildImageDataUrl(page, _pageIndex + 1, totalPages));
            if (showOk) await ShowOkAsync();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(ex.Message);
        }
    }

    private async Task<IReadOnlyList<VoicemeeterOverviewState>> FetchLiveStatesAsync()
    {
        var results = new List<VoicemeeterOverviewState>();
        foreach (var key in VmSettings.OverviewTargets)
        {
            var parsed = VoicemeeterSettings.ParseChannelKey(key);
            if (parsed == null) continue;
            var (kind, index) = parsed.Value;
            var shortLabel = VoicemeeterSettings.ShortLabelFor(kind, index);
            try
            {
                var state = await Client.GetChannelStateAsync(kind, index, DisposeToken);
                results.Add(new VoicemeeterOverviewState(key, shortLabel, state.GainDb, state.Muted, null));
            }
            catch (Exception ex)
            {
                results.Add(new VoicemeeterOverviewState(key, shortLabel, null, null, ex.Message));
            }
        }

        return results;
    }

    private IReadOnlyList<VoicemeeterOverviewState>? TryGetCachedStates()
    {
        var snapshot = VoicemeeterRuntime.State.Current;
        if (snapshot == null || snapshot.Error != null) return null;
        return VmSettings.OverviewTargets
            .Select(key => snapshot.CurrentStates.TryGetValue(key, out var state)
                ? state
                : new VoicemeeterOverviewState(key, key, null, null, "Missing from snapshot"))
            .ToArray();
    }
}
