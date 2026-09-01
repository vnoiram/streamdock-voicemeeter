using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using log4net;

namespace StreamDockVoicemeeter;

/// <summary>
///     <see cref="IVoicemeeterClient"/> implemented over the shared voicemeeter-hub WebSocket
///     service. Every operation is a request/response round trip to the hub, which owns the single
///     Remote API login session for the machine. If the hub is not reachable, the client tries to
///     launch it (from <c>VOICEMEETER_HUB_EXE</c>, a bundled copy, or the per-user install path)
///     and reconnects.
/// </summary>
public sealed class VoicemeeterHubClient : IVoicemeeterClient
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(VoicemeeterHubClient));
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan HubStartupTimeout = TimeSpan.FromSeconds(8);

    private readonly ConcurrentDictionary<string, TaskCompletionSource<HubResponseFrame>> _pending = new();
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private ClientWebSocket? _socket;
    private CancellationTokenSource? _receiveCancellation;
    private int _disposed;
    private int _reconnectSuppressed;

    public static IVoicemeeterClient Create() => new VoicemeeterHubClient();

    public string? DllPath { get; private set; }
    public string? DiscoveryError { get; private set; }
    public VoicemeeterOperationResult? LastResult { get; private set; }

    public Task<VoicemeeterOperationResult> EnsureConnectedAsync(CancellationToken cancellationToken = default) =>
        InvokeAsync<VoicemeeterOperationResult>("EnsureConnected", null, cancellationToken);

    public void SuppressReconnect() => Interlocked.Exchange(ref _reconnectSuppressed, 1);

    public Task<VoicemeeterEdition> GetEditionAsync(CancellationToken cancellationToken = default) =>
        InvokeAsync<VoicemeeterEdition>("GetEdition", null, cancellationToken);

    public Task<string?> GetVersionAsync(CancellationToken cancellationToken = default) =>
        InvokeAsync<string?>("GetVersion", null, cancellationToken);

    public Task<VoicemeeterChannelState> GetChannelStateAsync(string channelKind, int index, CancellationToken cancellationToken = default) =>
        InvokeAsync<VoicemeeterChannelState>("GetChannelState", Args(("channelKind", channelKind), ("index", index)), cancellationToken);

    public Task<VoicemeeterOperationResult> SetGainAsync(string channelKind, int index, double gainDb, CancellationToken cancellationToken = default) =>
        InvokeOperationAsync("SetGain", Args(("channelKind", channelKind), ("index", index), ("gainDb", gainDb)), cancellationToken);

    public Task<VoicemeeterOperationResult> SetMuteAsync(string channelKind, int index, bool muted, CancellationToken cancellationToken = default) =>
        InvokeOperationAsync("SetMute", Args(("channelKind", channelKind), ("index", index), ("muted", muted)), cancellationToken);

    public Task<bool> GetSoloAsync(int stripIndex, CancellationToken cancellationToken = default) =>
        InvokeAsync<bool>("GetSolo", Args(("stripIndex", stripIndex)), cancellationToken);

    public Task<VoicemeeterOperationResult> SetSoloAsync(int stripIndex, bool solo, CancellationToken cancellationToken = default) =>
        InvokeOperationAsync("SetSolo", Args(("stripIndex", stripIndex), ("solo", solo)), cancellationToken);

    public Task<bool> GetMonoAsync(string channelKind, int index, CancellationToken cancellationToken = default) =>
        InvokeAsync<bool>("GetMono", Args(("channelKind", channelKind), ("index", index)), cancellationToken);

    public Task<VoicemeeterOperationResult> SetMonoAsync(string channelKind, int index, bool mono, CancellationToken cancellationToken = default) =>
        InvokeOperationAsync("SetMono", Args(("channelKind", channelKind), ("index", index), ("mono", mono)), cancellationToken);

    public Task<VoicemeeterOperationResult> SetDeviceAsync(string channelKind, int index, string driver, string deviceName, CancellationToken cancellationToken = default) =>
        InvokeOperationAsync("SetDevice", Args(("channelKind", channelKind), ("index", index), ("driver", driver), ("deviceName", deviceName)), cancellationToken);

    public Task<string?> GetDeviceAsync(string channelKind, int index, string driver, CancellationToken cancellationToken = default) =>
        InvokeAsync<string?>("GetDevice", Args(("channelKind", channelKind), ("index", index), ("driver", driver)), cancellationToken);

    public async Task<IReadOnlyList<VoicemeeterDeviceInfo>> GetInputDevicesAsync(CancellationToken cancellationToken = default) =>
        await InvokeAsync<List<VoicemeeterDeviceInfo>>("GetInputDevices", null, cancellationToken) ?? [];

    public async Task<IReadOnlyList<VoicemeeterDeviceInfo>> GetOutputDevicesAsync(CancellationToken cancellationToken = default) =>
        await InvokeAsync<List<VoicemeeterDeviceInfo>>("GetOutputDevices", null, cancellationToken) ?? [];

    public Task<VoicemeeterOperationResult> PressMacroButtonAsync(int index, bool pressed, CancellationToken cancellationToken = default) =>
        InvokeOperationAsync("PressMacroButton", Args(("index", index), ("pressed", pressed)), cancellationToken);

    public Task<bool> GetMacroButtonStateAsync(int index, CancellationToken cancellationToken = default) =>
        InvokeAsync<bool>("GetMacroButtonState", Args(("index", index)), cancellationToken);

    public Task<bool> IsParametersDirtyAsync(CancellationToken cancellationToken = default) =>
        InvokeAsync<bool>("IsParametersDirty", null, cancellationToken);

    public Task<bool> IsMacroButtonDirtyAsync(CancellationToken cancellationToken = default) =>
        InvokeAsync<bool>("IsMacroButtonDirty", null, cancellationToken);

    public Task<VoicemeeterOperationResult> SetRecorderAsync(bool recording, CancellationToken cancellationToken = default) =>
        InvokeOperationAsync("SetRecorder", Args(("recording", recording)), cancellationToken);

    public Task<bool> GetRecorderStateAsync(CancellationToken cancellationToken = default) =>
        InvokeAsync<bool>("GetRecorderState", null, cancellationToken);

    public Task<VoicemeeterOperationResult> SetEqAsync(int busIndex, bool on, CancellationToken cancellationToken = default) =>
        InvokeOperationAsync("SetEq", Args(("busIndex", busIndex), ("on", on)), cancellationToken);

    public Task<bool> GetEqStateAsync(int busIndex, CancellationToken cancellationToken = default) =>
        InvokeAsync<bool>("GetEqState", Args(("busIndex", busIndex)), cancellationToken);

    public Task<VoicemeeterOperationResult> TriggerCommandAsync(string commandName, CancellationToken cancellationToken = default) =>
        InvokeOperationAsync("TriggerCommand", Args(("commandName", commandName)), cancellationToken);

    public async Task<object> BuildDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        var result = await InvokeAsync<JsonElement>("BuildDiagnostics", null, cancellationToken);
        if (result.ValueKind == JsonValueKind.Object)
        {
            if (result.TryGetProperty("dllPath", out var dllPath)) DllPath = dllPath.GetString();
            if (result.TryGetProperty("discoveryError", out var discoveryError)) DiscoveryError = discoveryError.GetString();
        }

        var diagnostics = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["remoteMode"] = VoicemeeterRuntime.RemoteMode
        };
        if (result.ValueKind == JsonValueKind.Object)
            foreach (var property in result.EnumerateObject())
                diagnostics[property.Name] = property.Value.Clone();
        return diagnostics;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Interlocked.Exchange(ref _reconnectSuppressed, 1);
        TeardownSocket();
        _connectLock.Dispose();
    }

    private async Task<VoicemeeterOperationResult> InvokeOperationAsync(string op, Dictionary<string, JsonElement>? args, CancellationToken cancellationToken)
    {
        var result = await InvokeAsync<VoicemeeterOperationResult>(op, args, cancellationToken);
        LastResult = result;
        return result;
    }

    private async Task<T> InvokeAsync<T>(string op, Dictionary<string, JsonElement>? args, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var socket = await EnsureConnectedSocketAsync(attempt == 1, cancellationToken);
                return await SendAsync<T>(socket, op, args, cancellationToken);
            }
            catch (Exception ex) when (attempt == 0 && ex is WebSocketException or IOException or TimeoutException or InvalidOperationException && Volatile.Read(ref _reconnectSuppressed) == 0)
            {
                Log.Warn($"Hub request '{op}' failed ({ex.GetType().Name}: {ex.Message}); reconnecting and retrying.");
                TeardownSocket();
            }
        }

        var retrySocket = await EnsureConnectedSocketAsync(true, cancellationToken);
        return await SendAsync<T>(retrySocket, op, args, cancellationToken);
    }

    private async Task<T> SendAsync<T>(ClientWebSocket socket, string op, Dictionary<string, JsonElement>? args, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource<HubResponseFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = completion;
        try
        {
            var frame = new HubRequestFrame(id, op, args);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(frame, VoicemeeterHubProtocol.JsonOptions);
            await socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(RequestTimeout);
            await using (timeout.Token.Register(() => completion.TrySetException(new TimeoutException($"Hub request '{op}' timed out."))))
            {
                var response = await completion.Task;
                if (response.Error != null) throw new InvalidOperationException(response.Error);
                if (response.Result is not { } result || result.ValueKind == JsonValueKind.Null) return default!;
                return result.Deserialize<T>(VoicemeeterHubProtocol.JsonOptions)!;
            }
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    private async Task<ClientWebSocket> EnsureConnectedSocketAsync(bool allowStartHub, CancellationToken cancellationToken)
    {
        var existing = _socket;
        if (existing is { State: WebSocketState.Open }) return existing;

        await _connectLock.WaitAsync(cancellationToken);
        try
        {
            if (_socket is { State: WebSocketState.Open }) return _socket;
            TeardownSocket();

            if (await TryConnectAsync(cancellationToken) is { } connected) return connected;

            if (allowStartHub && Volatile.Read(ref _reconnectSuppressed) == 0)
            {
                StartHub();
                var deadline = DateTimeOffset.UtcNow + HubStartupTimeout;
                while (DateTimeOffset.UtcNow < deadline)
                {
                    if (await TryConnectAsync(cancellationToken) is { } started) return started;
                    await Task.Delay(150, cancellationToken);
                }
            }

            throw new IOException("voicemeeter-hub is not reachable and could not be started.");
        }
        finally
        {
            _connectLock.Release();
        }
    }

    private async Task<ClientWebSocket?> TryConnectAsync(CancellationToken cancellationToken)
    {
        var port = ReadEndpointPort() ?? VoicemeeterHubProtocol.ResolvePort();
        var socket = new ClientWebSocket();
        try
        {
            using var connectCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectCancellation.CancelAfter(TimeSpan.FromMilliseconds(900));
            await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), connectCancellation.Token);

            _socket = socket;
            _receiveCancellation = new CancellationTokenSource();
            _ = Task.Run(() => ReceiveLoopAsync(socket, _receiveCancellation.Token));
            return socket;
        }
        catch (Exception)
        {
            // Hub not listening yet (or connect cancelled); caller falls back to starting it.
            socket.Dispose();
            return null;
        }
    }

    private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[16384];
        var payload = new MemoryStream();
        try
        {
            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                payload.SetLength(0);
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(buffer, cancellationToken);
                    if (result.MessageType == WebSocketMessageType.Close) return;
                    payload.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                DispatchFrame(Encoding.UTF8.GetString(payload.GetBuffer(), 0, (int)payload.Length));
            }
        }
        catch (Exception)
        {
            // Socket faulted; pending requests are failed below and the next op reconnects.
        }
        finally
        {
            FailPending(new IOException("voicemeeter-hub connection closed."));
        }
    }

    private void DispatchFrame(string json)
    {
        HubResponseFrame? frame;
        try
        {
            frame = JsonSerializer.Deserialize<HubResponseFrame>(json, VoicemeeterHubProtocol.JsonOptions);
        }
        catch (JsonException)
        {
            return;
        }

        if (frame is null || frame.Type != "response" || frame.Id is null) return; // ignore hello/event
        if (_pending.TryRemove(frame.Id, out var completion)) completion.TrySetResult(frame);
    }

    private void TeardownSocket()
    {
        var socket = Interlocked.Exchange(ref _socket, null);
        var receiveCancellation = Interlocked.Exchange(ref _receiveCancellation, null);
        try { receiveCancellation?.Cancel(); } catch (ObjectDisposedException) { }
        receiveCancellation?.Dispose();
        if (socket != null)
        {
            try { socket.Abort(); } catch (Exception) { }
            socket.Dispose();
        }

        FailPending(new IOException("voicemeeter-hub connection reset."));
    }

    private void FailPending(Exception ex)
    {
        foreach (var key in _pending.Keys)
            if (_pending.TryRemove(key, out var completion))
                completion.TrySetException(ex);
    }

    private static int? ReadEndpointPort()
    {
        try
        {
            var path = VoicemeeterHubProtocol.EndpointFilePath();
            if (!File.Exists(path)) return null;
            var info = JsonSerializer.Deserialize<HubEndpointInfo>(File.ReadAllText(path), VoicemeeterHubProtocol.JsonOptions);
            return info is { Port: > 0 and < 65536 } ? info.Port : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void StartHub()
    {
        var exePath = DiscoverHubExe();
        if (exePath == null)
        {
            DiscoveryError = "voicemeeter-hub executable was not found. Set VOICEMEETER_HUB_EXE or install it.";
            Log.Warn(DiscoveryError);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory
            });
            Log.Info($"Started voicemeeter-hub: {exePath}");
        }
        catch (Exception ex)
        {
            Log.Warn($"Failed to start voicemeeter-hub '{exePath}': {ex.Message}");
        }
    }

    private static string? DiscoverHubExe()
    {
        foreach (var candidate in HubExeCandidates())
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
                return candidate;
        return null;
    }

    private static IEnumerable<string> HubExeCandidates()
    {
        yield return Environment.GetEnvironmentVariable(VoicemeeterHubProtocol.ExeEnvironmentVariable) ?? "";
        yield return Path.Combine(AppContext.BaseDirectory, "voicemeeter-hub", "VoicemeeterHub.exe");
        yield return Path.Combine(AppContext.BaseDirectory, "VoicemeeterHub.exe");
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        yield return Path.Combine(localAppData, "voicemeeter-hub", "VoicemeeterHub.exe");
    }

    private static Dictionary<string, JsonElement> Args(params (string Name, object? Value)[] values)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var (name, value) in values)
            args[name] = JsonSerializer.SerializeToElement(value, VoicemeeterHubProtocol.JsonOptions);
        return args;
    }
}
