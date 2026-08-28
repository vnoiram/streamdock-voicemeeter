using log4net;

namespace StreamDockVoicemeeter;

public static class VoicemeeterRuntime
{
    public const string RemoteModeEnvironmentVariable = "STREAMDOCK_VOICEMEETER_REMOTE_MODE";

    public static IVoicemeeterClient Client { get; } = CreateClient();
    public static VoicemeeterStateService State { get; } = new(Client);
    public static string RemoteMode { get; } = Client is VoicemeeterClient ? "direct" : "proxy";

    private static int _disposed;

    public static void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Client.SuppressReconnect();
        State.Dispose();
        Client.Dispose();
    }

    private static IVoicemeeterClient CreateClient()
    {
        var mode = Environment.GetEnvironmentVariable(RemoteModeEnvironmentVariable);
        return string.Equals(mode, "direct", StringComparison.OrdinalIgnoreCase)
            ? new VoicemeeterClient()
            : VoicemeeterProxyClient.Create();
    }
}

public sealed class VoicemeeterStateService : IDisposable
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(VoicemeeterStateService));
    private static readonly string[] ChannelKinds = ["strip", "bus"];
    private readonly IVoicemeeterClient _client;
    private readonly CancellationTokenSource _disposeCancellation = new();
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly Timer _timer;
    private int _disposed;

    public VoicemeeterStateService(IVoicemeeterClient client)
    {
        _client = client;
        _timer = new Timer(_ => _ = TickAsync(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    public event Func<VoicemeeterSnapshot, Task>? StateChanged;

    public VoicemeeterSnapshot? Current { get; private set; }

    public async Task<VoicemeeterSnapshot> RefreshAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        using var linkedCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposeCancellation.Token);
        var token = linkedCancellation.Token;

        await _refreshLock.WaitAsync(token);
        try
        {
            ThrowIfDisposed();
            VoicemeeterSnapshot snapshot;
            try
            {
                var edition = await _client.GetEditionAsync(token);
                var states = new Dictionary<string, VoicemeeterOverviewState>(StringComparer.OrdinalIgnoreCase);
                foreach (var kind in ChannelKinds)
                for (var index = 0; index <= VoicemeeterSettings.MaxChannelIndex; index++)
                {
                    var key = VoicemeeterSettings.BuildChannelKey(kind, index);
                    var shortLabel = VoicemeeterSettings.AbbreviatedLabelFor(kind, index, edition);
                    try
                    {
                        var state = await _client.GetChannelStateAsync(kind, index, token);
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
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        _disposeCancellation.Cancel();
        using var timerDisposed = new ManualResetEvent(false);
        if (_timer.Dispose(timerDisposed)) timerDisposed.WaitOne(TimeSpan.FromSeconds(2));
        _disposeCancellation.Dispose();
        _refreshLock.Dispose();
    }

    private async Task TickAsync()
    {
        try
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            var dirty = Current == null || await _client.IsParametersDirtyAsync();
            if (dirty && Volatile.Read(ref _disposed) == 0) await RefreshAsync(_disposeCancellation.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
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

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }
}
