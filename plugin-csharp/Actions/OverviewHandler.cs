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
    public override Task OnWillAppearAsync()
    {
        Log.Info($"Overview willAppear context={Context} targets={string.Join(",", VmSettings.OverviewTargets)}");
        return RefreshSharedStateAsync();
    }

    public override Task UpdateDisplayAsync()
    {
        return RefreshAsync(false);
    }

    public override Task OnSettingsChangedAsync(Dictionary<string, object> settings)
    {
        UpdateSettings(settings);
        return RefreshAsync(false, false);
    }

    public override async Task OnKeyDownAsync()
    {
        await RefreshSharedStateAsync();
        await ShowOkAsync();
    }

    private async Task RefreshAsync(bool showOk, bool useCache = true)
    {
        try
        {
            var states = useCache
                ? TryGetCachedStates() ?? await FetchLiveStatesAsync()
                : await FetchLiveStatesAsync();
            await SetTitleAsync("");
            await SetImageAsync(VoicemeeterOverviewRenderer.BuildImageDataUrl(states));
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
