using log4net;

namespace StreamDockVoicemeeter;

public static class VoicemeeterRuntime
{
    public static VoicemeeterClient Client { get; } = new();
    public static VoicemeeterStateService State { get; } = new(Client);
}

public sealed class VoicemeeterStateService : IDisposable
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(VoicemeeterStateService));
    private static readonly string[] ChannelKinds = ["strip", "bus"];
    private readonly VoicemeeterClient _client;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly Timer _timer;

    public VoicemeeterStateService(VoicemeeterClient client)
    {
        _client = client;
        _timer = new Timer(_ => _ = TickAsync(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    public event Func<VoicemeeterSnapshot, Task>? StateChanged;

    public VoicemeeterSnapshot? Current { get; private set; }

    public async Task<VoicemeeterSnapshot> RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            VoicemeeterSnapshot snapshot;
            try
            {
                var edition = await _client.GetEditionAsync(cancellationToken);
                var states = new Dictionary<string, VoicemeeterOverviewState>(StringComparer.OrdinalIgnoreCase);
                foreach (var kind in ChannelKinds)
                for (var index = 0; index <= VoicemeeterSettings.MaxChannelIndex; index++)
                {
                    var key = VoicemeeterSettings.BuildChannelKey(kind, index);
                    var shortLabel = VoicemeeterSettings.ShortLabelFor(kind, index);
                    try
                    {
                        var state = await _client.GetChannelStateAsync(kind, index, cancellationToken);
                        states[key] = new VoicemeeterOverviewState(key, shortLabel, state.GainDb, state.Muted, null);
                    }
                    catch (Exception ex)
                    {
                        states[key] = new VoicemeeterOverviewState(key, shortLabel, null, null, ex.Message);
                    }
                }

                snapshot = new VoicemeeterSnapshot(DateTimeOffset.Now, edition, states, null);
            }
            catch (Exception ex)
            {
                Log.Warn($"Voicemeeter state refresh failed: {ex.Message}");
                snapshot = new VoicemeeterSnapshot(DateTimeOffset.Now, VoicemeeterEdition.Unknown,
                    new Dictionary<string, VoicemeeterOverviewState>(), ex.Message);
            }

            Current = snapshot;
            await NotifyAsync(snapshot);
            return snapshot;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public void Dispose()
    {
        _timer.Dispose();
        _refreshLock.Dispose();
    }

    private async Task TickAsync()
    {
        try
        {
            var dirty = Current == null || await _client.IsParametersDirtyAsync();
            if (dirty) await RefreshAsync();
        }
        catch (Exception ex)
        {
            Log.Warn($"Voicemeeter state tick failed: {ex.Message}");
        }
    }

    private async Task NotifyAsync(VoicemeeterSnapshot snapshot)
    {
        var handlers = StateChanged;
        if (handlers == null) return;
        foreach (Func<VoicemeeterSnapshot, Task> handler in handlers.GetInvocationList())
            try
            {
                await handler(snapshot);
            }
            catch (Exception ex)
            {
                Log.Warn($"Voicemeeter state listener failed: {ex.Message}");
            }
    }
}
