# Voicemeeter Proxy Protocol

The broker is started by running `StreamDockVoicemeeter.exe --voicemeeter-proxy`.
It accepts one JSON request per named-pipe connection on `StreamDockVoicemeeter.Proxy.v1`
and returns one JSON response.

Only the broker process calls `VBVMR_Login` / `VBVMR_Logout`; clients should not load
`VoicemeeterRemote64.dll` directly when using this protocol.

## Request

```json
{"operation":"SetMute","args":{"channelKind":"strip","index":0,"muted":true}}
```

## Response

```json
{"result":{"success":true,"statusCode":0,"paramName":"Strip[0].Mute","errorSummary":null},"error":null}
```

If dispatch fails, `error` contains the failure message and `result` is `null`.

## Operations

- `EnsureConnected`
- `GetEdition`
- `GetVersion`
- `GetChannelState`: `channelKind`, `index`
- `SetGain`: `channelKind`, `index`, `gainDb`
- `SetMute`: `channelKind`, `index`, `muted`
- `GetSolo`: `stripIndex`
- `SetSolo`: `stripIndex`, `solo`
- `GetMono`: `channelKind`, `index`
- `SetMono`: `channelKind`, `index`, `mono`
- `SetDevice`: `channelKind`, `index`, `driver`, `deviceName`
- `GetDevice`: `channelKind`, `index`, `driver`
- `GetInputDevices`
- `GetOutputDevices`
- `PressMacroButton`: `index`, `pressed`
- `GetMacroButtonState`: `index`
- `IsParametersDirty`
- `IsMacroButtonDirty`
- `SetRecorder`: `recording`
- `GetRecorderState`
- `SetEq`: `busIndex`, `on`
- `GetEqState`: `busIndex`
- `TriggerCommand`: `commandName`
- `BuildDiagnostics`

The broker exits after roughly 30 seconds without requests and logs out before exit.
The Stream Dock plugin uses this broker by default unless
`STREAMDOCK_VOICEMEETER_REMOTE_MODE=direct` is set.
