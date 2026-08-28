using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace StreamDockVoicemeeter;

public sealed class VoicemeeterProxyClient : IVoicemeeterClient
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromMilliseconds(900);
    private static readonly TimeSpan BrokerStartupTimeout = TimeSpan.FromSeconds(6);
    private int _disposed;

    private VoicemeeterProxyClient()
    {
    }

    public string? DllPath { get; private set; }
    public string? DiscoveryError { get; private set; }
    public VoicemeeterOperationResult? LastResult { get; private set; }

    public static IVoicemeeterClient Create()
    {
        return new VoicemeeterProxyClient();
    }

    public Task<VoicemeeterOperationResult> EnsureConnectedAsync(CancellationToken cancellationToken = default)
    {
        return InvokeAsync<VoicemeeterOperationResult>("EnsureConnected", null, cancellationToken);
    }

    public void SuppressReconnect()
    {
        Interlocked.Exchange(ref _disposed, 1);
    }

    public Task<VoicemeeterEdition> GetEditionAsync(CancellationToken cancellationToken = default)
    {
        return InvokeAsync<VoicemeeterEdition>("GetEdition", null, cancellationToken);
    }

    public Task<string?> GetVersionAsync(CancellationToken cancellationToken = default)
    {
        return InvokeAsync<string?>("GetVersion", null, cancellationToken);
    }

    public Task<VoicemeeterChannelState> GetChannelStateAsync(string channelKind, int index, CancellationToken cancellationToken = default)
    {
        return InvokeAsync<VoicemeeterChannelState>("GetChannelState", Args(("channelKind", channelKind), ("index", index)), cancellationToken);
    }

    public Task<VoicemeeterOperationResult> SetGainAsync(string channelKind, int index, double gainDb, CancellationToken cancellationToken = default)
    {
        return InvokeOperationAsync("SetGain", Args(("channelKind", channelKind), ("index", index), ("gainDb", gainDb)), cancellationToken);
    }

    public Task<VoicemeeterOperationResult> SetMuteAsync(string channelKind, int index, bool muted, CancellationToken cancellationToken = default)
    {
        return InvokeOperationAsync("SetMute", Args(("channelKind", channelKind), ("index", index), ("muted", muted)), cancellationToken);
    }

    public Task<bool> GetSoloAsync(int stripIndex, CancellationToken cancellationToken = default)
    {
        return InvokeAsync<bool>("GetSolo", Args(("stripIndex", stripIndex)), cancellationToken);
    }

    public Task<VoicemeeterOperationResult> SetSoloAsync(int stripIndex, bool solo, CancellationToken cancellationToken = default)
    {
        return InvokeOperationAsync("SetSolo", Args(("stripIndex", stripIndex), ("solo", solo)), cancellationToken);
    }

    public Task<bool> GetMonoAsync(string channelKind, int index, CancellationToken cancellationToken = default)
    {
        return InvokeAsync<bool>("GetMono", Args(("channelKind", channelKind), ("index", index)), cancellationToken);
    }

    public Task<VoicemeeterOperationResult> SetMonoAsync(string channelKind, int index, bool mono, CancellationToken cancellationToken = default)
    {
        return InvokeOperationAsync("SetMono", Args(("channelKind", channelKind), ("index", index), ("mono", mono)), cancellationToken);
    }

    public Task<VoicemeeterOperationResult> SetDeviceAsync(string channelKind, int index, string driver, string deviceName, CancellationToken cancellationToken = default)
    {
        return InvokeOperationAsync("SetDevice", Args(("channelKind", channelKind), ("index", index), ("driver", driver), ("deviceName", deviceName)), cancellationToken);
    }

    public Task<string?> GetDeviceAsync(string channelKind, int index, string driver, CancellationToken cancellationToken = default)
    {
        return InvokeAsync<string?>("GetDevice", Args(("channelKind", channelKind), ("index", index), ("driver", driver)), cancellationToken);
    }

    public async Task<IReadOnlyList<VoicemeeterDeviceInfo>> GetInputDevicesAsync(CancellationToken cancellationToken = default)
    {
        return await InvokeAsync<List<VoicemeeterDeviceInfo>>("GetInputDevices", null, cancellationToken);
    }

    public async Task<IReadOnlyList<VoicemeeterDeviceInfo>> GetOutputDevicesAsync(CancellationToken cancellationToken = default)
    {
        return await InvokeAsync<List<VoicemeeterDeviceInfo>>("GetOutputDevices", null, cancellationToken);
    }

    public Task<VoicemeeterOperationResult> PressMacroButtonAsync(int index, bool pressed, CancellationToken cancellationToken = default)
    {
        return InvokeOperationAsync("PressMacroButton", Args(("index", index), ("pressed", pressed)), cancellationToken);
    }

    public Task<bool> GetMacroButtonStateAsync(int index, CancellationToken cancellationToken = default)
    {
        return InvokeAsync<bool>("GetMacroButtonState", Args(("index", index)), cancellationToken);
    }

    public Task<bool> IsParametersDirtyAsync(CancellationToken cancellationToken = default)
    {
        return InvokeAsync<bool>("IsParametersDirty", null, cancellationToken);
    }

    public Task<bool> IsMacroButtonDirtyAsync(CancellationToken cancellationToken = default)
    {
        return InvokeAsync<bool>("IsMacroButtonDirty", null, cancellationToken);
    }

    public Task<VoicemeeterOperationResult> SetRecorderAsync(bool recording, CancellationToken cancellationToken = default)
    {
        return InvokeOperationAsync("SetRecorder", Args(("recording", recording)), cancellationToken);
    }

    public Task<bool> GetRecorderStateAsync(CancellationToken cancellationToken = default)
    {
        return InvokeAsync<bool>("GetRecorderState", null, cancellationToken);
    }

    public Task<VoicemeeterOperationResult> SetEqAsync(int busIndex, bool on, CancellationToken cancellationToken = default)
    {
        return InvokeOperationAsync("SetEq", Args(("busIndex", busIndex), ("on", on)), cancellationToken);
    }

    public Task<bool> GetEqStateAsync(int busIndex, CancellationToken cancellationToken = default)
    {
        return InvokeAsync<bool>("GetEqState", Args(("busIndex", busIndex)), cancellationToken);
    }

    public Task<VoicemeeterOperationResult> TriggerCommandAsync(string commandName, CancellationToken cancellationToken = default)
    {
        return InvokeOperationAsync("TriggerCommand", Args(("commandName", commandName)), cancellationToken);
    }

    public async Task<object> BuildDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        var result = await InvokeAsync<JsonElement>("BuildDiagnostics", null, cancellationToken);
        if (result.TryGetProperty("dllPath", out var dllPath)) DllPath = dllPath.GetString();
        if (result.TryGetProperty("discoveryError", out var discoveryError)) DiscoveryError = discoveryError.GetString();
        var diagnostics = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["remoteMode"] = VoicemeeterRuntime.RemoteMode
        };
        foreach (var property in result.EnumerateObject())
            diagnostics[property.Name] = property.Value.Clone();
        return diagnostics;
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _disposed, 1);
    }

    private async Task<VoicemeeterOperationResult> InvokeOperationAsync(
        string operation,
        Dictionary<string, JsonElement>? args,
        CancellationToken cancellationToken)
    {
        var result = await InvokeAsync<VoicemeeterOperationResult>(operation, args, cancellationToken);
        LastResult = result;
        return result;
    }

    private async Task<T> InvokeAsync<T>(
        string operation,
        Dictionary<string, JsonElement>? args,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var request = new VoicemeeterProxyRequest(operation, args);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                return await SendAsync<T>(request, cancellationToken);
            }
            catch (TimeoutException) when (attempt == 0)
            {
                StartBroker();
            }
            catch (IOException) when (attempt == 0)
            {
                StartBroker();
            }
            catch (OperationCanceledException) when (attempt == 0 && !cancellationToken.IsCancellationRequested)
            {
                StartBroker();
            }
        }

        return await SendAsync<T>(request, cancellationToken);
    }

    private static async Task<T> SendAsync<T>(VoicemeeterProxyRequest request, CancellationToken cancellationToken)
    {
        await using var pipe = new NamedPipeClientStream(".", VoicemeeterProxyProtocol.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        using var connectCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        connectCancellation.CancelAfter(ConnectTimeout);
        await pipe.ConnectAsync(connectCancellation.Token);

        await JsonSerializer.SerializeAsync(pipe, request, VoicemeeterProxyProtocol.JsonOptions, cancellationToken);
        await pipe.WriteAsync("\n"u8.ToArray(), cancellationToken);
        await pipe.FlushAsync(cancellationToken);

        using var reader = new StreamReader(pipe, Encoding.UTF8, false, leaveOpen: true);
        var line = await reader.ReadLineAsync(cancellationToken);
        if (line == null) throw new IOException("Voicemeeter proxy closed the response pipe.");

        var response = JsonSerializer.Deserialize<VoicemeeterProxyResponse>(line, VoicemeeterProxyProtocol.JsonOptions)
                       ?? throw new IOException("Voicemeeter proxy returned an invalid response.");
        if (response.Error != null) throw new InvalidOperationException(response.Error);
        if (response.Result == null) return default!;
        return response.Result.Value.Deserialize<T>(VoicemeeterProxyProtocol.JsonOptions)!;
    }

    private static Dictionary<string, JsonElement> Args(params (string Name, object? Value)[] values)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var (name, value) in values)
            args[name] = JsonSerializer.SerializeToElement(value, VoicemeeterProxyProtocol.JsonOptions);
        return args;
    }

    private static void StartBroker()
    {
        using var process = Process.GetCurrentProcess();
        var exePath = Environment.ProcessPath ?? process.MainModule?.FileName;
        if (string.IsNullOrWhiteSpace(exePath)) throw new InvalidOperationException("Current executable path is unavailable.");

        Process.Start(new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = VoicemeeterProxyProtocol.BrokerArgument,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = AppContext.BaseDirectory
        });

        var deadline = DateTimeOffset.UtcNow + BrokerStartupTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                using var pipe = new NamedPipeClientStream(".", VoicemeeterProxyProtocol.PipeName, PipeDirection.InOut);
                pipe.Connect(120);
                return;
            }
            catch (TimeoutException)
            {
                Thread.Sleep(80);
            }
            catch (IOException)
            {
                Thread.Sleep(80);
            }
        }
    }
}
