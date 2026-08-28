using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using log4net;

namespace StreamDockVoicemeeter;

internal static class VoicemeeterProxyBroker
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(VoicemeeterProxyBroker));
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(30);
    private static long _lastActivityTicks;
    private static int _activeRequests;

    public static async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        using var mutex = new Mutex(true, VoicemeeterProxyProtocol.MutexName, out var created);
        if (!created && !mutex.WaitOne(TimeSpan.Zero)) return 0;

        Volatile.Write(ref _lastActivityTicks, DateTimeOffset.UtcNow.UtcTicks);
        using var client = new VoicemeeterClient();
        using var idleCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var idleTask = WatchIdleAsync(idleCancellation);

        try
        {
            while (!idleCancellation.IsCancellationRequested)
            {
                var pipe = new NamedPipeServerStream(
                    VoicemeeterProxyProtocol.PipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await pipe.WaitForConnectionAsync(idleCancellation.Token);
                Volatile.Write(ref _lastActivityTicks, DateTimeOffset.UtcNow.UtcTicks);
                _ = Task.Run(() => HandleConnectionAsync(pipe, client, idleCancellation.Token), idleCancellation.Token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            idleCancellation.Cancel();
            try { await idleTask; } catch (OperationCanceledException) { }
            try { mutex.ReleaseMutex(); } catch (ApplicationException) { }
        }

        return 0;
    }

    private static async Task WatchIdleAsync(CancellationTokenSource idleCancellation)
    {
        var cancellationToken = idleCancellation.Token;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            var last = new DateTimeOffset(Volatile.Read(ref _lastActivityTicks), TimeSpan.Zero);
            if (Volatile.Read(ref _activeRequests) > 0) continue;
            if (DateTimeOffset.UtcNow - last < IdleTimeout) continue;
            Log.Info("Voicemeeter proxy broker idle timeout reached; logging out and exiting.");
            idleCancellation.Cancel();
            return;
        }
    }

    private static async Task HandleConnectionAsync(NamedPipeServerStream pipe, VoicemeeterClient client, CancellationToken cancellationToken)
    {
        await using (pipe.ConfigureAwait(false))
        {
            Interlocked.Increment(ref _activeRequests);
            try
            {
                VoicemeeterProxyResponse response;
                try
                {
                    using var reader = new StreamReader(pipe, Encoding.UTF8, false, leaveOpen: true);
                    var line = await reader.ReadLineAsync(cancellationToken);
                    if (string.IsNullOrWhiteSpace(line)) return;

                    var request = JsonSerializer.Deserialize<VoicemeeterProxyRequest>(line, VoicemeeterProxyProtocol.JsonOptions)
                                  ?? throw new InvalidOperationException("Invalid proxy request.");
                    Volatile.Write(ref _lastActivityTicks, DateTimeOffset.UtcNow.UtcTicks);
                    response = await DispatchAsync(client, request, cancellationToken);
                }
                catch (Exception ex)
                {
                    Log.Warn($"Voicemeeter proxy request failed: {ex.Message}");
                    response = VoicemeeterProxyResponse.Fail(ex);
                }

                var payload = JsonSerializer.Serialize(response, VoicemeeterProxyProtocol.JsonOptions);
                var bytes = Encoding.UTF8.GetBytes(payload + "\n");
                await pipe.WriteAsync(bytes, cancellationToken);
                await pipe.FlushAsync(cancellationToken);
                Volatile.Write(ref _lastActivityTicks, DateTimeOffset.UtcNow.UtcTicks);
            }
            finally
            {
                Interlocked.Decrement(ref _activeRequests);
            }
        }
    }

    private static async Task<VoicemeeterProxyResponse> DispatchAsync(
        VoicemeeterClient client,
        VoicemeeterProxyRequest request,
        CancellationToken cancellationToken)
    {
        var args = request.Args ?? new Dictionary<string, JsonElement>();
        switch (request.Operation)
        {
            case "EnsureConnected":
                return VoicemeeterProxyResponse.Ok(await client.EnsureConnectedAsync(cancellationToken));
            case "SuppressReconnect":
                return VoicemeeterProxyResponse.Ok(VoicemeeterOperationResult.Ok());
            case "GetEdition":
                return VoicemeeterProxyResponse.Ok(await client.GetEditionAsync(cancellationToken));
            case "GetVersion":
                return VoicemeeterProxyResponse.Ok(await client.GetVersionAsync(cancellationToken));
            case "GetChannelState":
                return VoicemeeterProxyResponse.Ok(await client.GetChannelStateAsync(Arg<string>(args, "channelKind"), Arg<int>(args, "index"), cancellationToken));
            case "SetGain":
                return VoicemeeterProxyResponse.Ok(await client.SetGainAsync(Arg<string>(args, "channelKind"), Arg<int>(args, "index"), Arg<double>(args, "gainDb"), cancellationToken));
            case "SetMute":
                return VoicemeeterProxyResponse.Ok(await client.SetMuteAsync(Arg<string>(args, "channelKind"), Arg<int>(args, "index"), Arg<bool>(args, "muted"), cancellationToken));
            case "GetSolo":
                return VoicemeeterProxyResponse.Ok(await client.GetSoloAsync(Arg<int>(args, "stripIndex"), cancellationToken));
            case "SetSolo":
                return VoicemeeterProxyResponse.Ok(await client.SetSoloAsync(Arg<int>(args, "stripIndex"), Arg<bool>(args, "solo"), cancellationToken));
            case "GetMono":
                return VoicemeeterProxyResponse.Ok(await client.GetMonoAsync(Arg<string>(args, "channelKind"), Arg<int>(args, "index"), cancellationToken));
            case "SetMono":
                return VoicemeeterProxyResponse.Ok(await client.SetMonoAsync(Arg<string>(args, "channelKind"), Arg<int>(args, "index"), Arg<bool>(args, "mono"), cancellationToken));
            case "SetDevice":
                return VoicemeeterProxyResponse.Ok(await client.SetDeviceAsync(Arg<string>(args, "channelKind"), Arg<int>(args, "index"), Arg<string>(args, "driver"), Arg<string>(args, "deviceName"), cancellationToken));
            case "GetDevice":
                return VoicemeeterProxyResponse.Ok(await client.GetDeviceAsync(Arg<string>(args, "channelKind"), Arg<int>(args, "index"), Arg<string>(args, "driver"), cancellationToken));
            case "GetInputDevices":
                return VoicemeeterProxyResponse.Ok(await client.GetInputDevicesAsync(cancellationToken));
            case "GetOutputDevices":
                return VoicemeeterProxyResponse.Ok(await client.GetOutputDevicesAsync(cancellationToken));
            case "PressMacroButton":
                return VoicemeeterProxyResponse.Ok(await client.PressMacroButtonAsync(Arg<int>(args, "index"), Arg<bool>(args, "pressed"), cancellationToken));
            case "GetMacroButtonState":
                return VoicemeeterProxyResponse.Ok(await client.GetMacroButtonStateAsync(Arg<int>(args, "index"), cancellationToken));
            case "IsParametersDirty":
                return VoicemeeterProxyResponse.Ok(await client.IsParametersDirtyAsync(cancellationToken));
            case "IsMacroButtonDirty":
                return VoicemeeterProxyResponse.Ok(await client.IsMacroButtonDirtyAsync(cancellationToken));
            case "SetRecorder":
                return VoicemeeterProxyResponse.Ok(await client.SetRecorderAsync(Arg<bool>(args, "recording"), cancellationToken));
            case "GetRecorderState":
                return VoicemeeterProxyResponse.Ok(await client.GetRecorderStateAsync(cancellationToken));
            case "SetEq":
                return VoicemeeterProxyResponse.Ok(await client.SetEqAsync(Arg<int>(args, "busIndex"), Arg<bool>(args, "on"), cancellationToken));
            case "GetEqState":
                return VoicemeeterProxyResponse.Ok(await client.GetEqStateAsync(Arg<int>(args, "busIndex"), cancellationToken));
            case "TriggerCommand":
                return VoicemeeterProxyResponse.Ok(await client.TriggerCommandAsync(Arg<string>(args, "commandName"), cancellationToken));
            case "BuildDiagnostics":
                return VoicemeeterProxyResponse.Ok(await client.BuildDiagnosticsAsync(cancellationToken));
            default:
                throw new InvalidOperationException($"Unknown Voicemeeter proxy operation '{request.Operation}'.");
        }
    }

    private static T Arg<T>(IReadOnlyDictionary<string, JsonElement> args, string name)
    {
        if (!args.TryGetValue(name, out var value)) throw new InvalidOperationException($"Missing proxy argument '{name}'.");
        return value.Deserialize<T>(VoicemeeterProxyProtocol.JsonOptions)!;
    }
}
