# streamdock-voicemeeter

日本語版はこちら: [README.ja.md](README.ja.md)

Mirabox Stream Dock plugin for controlling VB-Audio Voicemeeter (Standard/Banana/Potato) strips and buses directly from a C# plugin process. Modeled on the sibling [`streamdock-sonar`](../streamdock-sonar) plugin's architecture, adapted to Voicemeeter's native Remote API (`VoicemeeterRemote64.dll`) instead of an HTTP API.

## Version

Current version: `0.1.19`.

## Support Scope

Edition-agnostic by design: strip/bus index and kind (`strip`/`bus`) are freely configurable in the Property Inspector rather than hardcoded per edition. The plugin polls `Strip[0..7]` and `Bus[0..7]` (the maximum range across Standard/Banana/Potato) so the same build works regardless of which edition is installed; indices beyond what your edition actually has will simply show an error state.

`Recorder` and `EQ Toggle` use parameter names (`Recorder.Record`, `Bus[i].EQ.on`) sourced from community documentation of the Voicemeeter Remote API rather than a verified official specification. Verify these against your own Voicemeeter install (see Diagnostics below) before relying on them.

## Actions

- `Voicemeeter Gain`: adjusts one strip or bus gain in dB. Key press changes gain by `Step`; knob rotation adjusts up/down, knob press toggles mute.
- `Voicemeeter Mute`: toggles mute for one strip or bus.
- `Voicemeeter Solo`: toggles solo for one strip.
- `Voicemeeter Mono`: toggles mono downmix for one strip or bus.
- `Voicemeeter Overview`: shows selected strip/bus gain and mute states on the Stream Dock key image.
- `Voicemeeter Balance Dial`: simulates a ChatMix-style balance by moving two configured channels' gain in opposite directions; dial press resets both to `0dB`.
- `Voicemeeter Output Device`: switches a bus's hardware output device.
- `Voicemeeter Rotate Output`: rotates a bus to the next enumerated output device.
- `Voicemeeter Input Device`: switches a hardware strip's input device.
- `Voicemeeter Rotate Input`: rotates a hardware strip to the next enumerated input device.
- `Voicemeeter Macro Button`: presses/releases a Voicemeeter MacroButtons logical button (index `0`-`79`) and reflects its displayed state.
- `Voicemeeter Recorder`: toggles Voicemeeter's built-in recorder.
- `Voicemeeter EQ Toggle`: toggles a bus's EQ on/off.
- `Voicemeeter App Control`: shows, restarts, or shuts down the Voicemeeter application.
- `Diagnostics`: sends DLL discovery path, login state, detected edition/version, and the last operation result to the Property Inspector.

## Runtime Behavior

Unlike Sonar's local HTTPS API, Voicemeeter has no network server. The plugin loads `VoicemeeterRemote64.dll` via P/Invoke and manages a login session (`VBVMR_Login`/`VBVMR_Logout`):

- DLL discovery checks the registry (`HKEY_LOCAL_MACHINE\SOFTWARE\VB:Audio\Voicemeeter`, with WOW6432Node and `Voicemeter`/`Voicemeeter` spelling fallbacks) for the install directory, then falls back to the default `C:\Program Files (x86)\VB\Voicemeeter\VoicemeeterRemote64.dll` / `C:\Program Files\VB\Voicemeeter\VoicemeeterRemote64.dll` paths.
- If Voicemeeter is installed but not running, the plugin shows a clear "not running" error rather than guessing which edition to auto-launch. Once an edition has been detected in the current process lifetime, a later disconnect will attempt `VBVMR_RunVoicemeeter` with that same edition and continue the existing Remote API session.
- If a Remote API call reports `-2` after a PC sleep, reboot, or Voicemeeter restart, the plugin logs out the stale session, logs in again, and retries that same call once before surfacing an error.
- State (gain/mute for all `Strip[0..7]`/`Bus[0..7]`) is refreshed via `VBVMR_IsParametersDirty()` polling roughly once per second; all buttons sharing the same channel update together, mirroring Sonar's shared-state-cache pattern.
- On plugin process startup, a new instance asks any already-running Voicemeeter plugin instance to shut down, then terminates any remaining process with the same executable path before connecting. This lets a cooperative old process call `VBVMR_Logout()` first, and still cleans up stale older builds that cannot receive the shutdown request.
- When the Stream Dock host closes the plugin WebSocket, the plugin stops shared-state polling, prevents any further `VBVMR_Login()` calls, calls `VBVMR_Logout()` on its dedicated Voicemeeter API thread before waiting on the Stream Dock WebSocket cleanup, and exits the plugin process so Voicemeeter does not retain stale Remote API sessions.
- Gain is a `float` in dB, clamped to `-60.0`..`+12.0`.
- Device assignment uses Voicemeeter's string parameters (`Strip[i].device.<driver>` / `Bus[i].device.<driver>`) where `<driver>` is one of `mme`, `wdm`, `ks`, `asio`; device lists are enumerated via `VBVMR_Input_GetDeviceDescA`/`VBVMR_Output_GetDeviceDescA`.
- MacroButtons use `VBVMR_MacroButton_SetStatus` with the `DEFAULT` bitmode on key-down/key-up (so both press and release fire, matching a physical button click) and `STATEONLY` reads for the displayed on/off icon state.

The Property Inspector requests live data (device lists, macro button status, diagnostics) over `sendToPlugin`; those requests use the Property Inspector connection context while saved settings use the action context, following the same `replyContext` convention as `streamdock-sonar`. After saving settings, the Property Inspector also notifies the running action handler directly so channel changes such as `Voicemeeter Mute` take effect immediately without restarting the Stream Dock host.

## Logs

The C# plugin writes `streamdock-voicemeeter.log` next to `StreamDockVoicemeeter.exe`:

```text
stream-dock-voicemeeter.sdPlugin\plugin\streamdock-voicemeeter.log
```

If the process fails before logging is configured, `startup-error.log` is written in the same directory.

## Repository Layout

- `plugin-csharp/`: C# Stream Dock plugin source, including `VoicemeeterClient.cs` (P/Invoke wrapper) and `Actions/` (one handler per action).
- `manifest.json`: Stream Dock plugin manifest pointing to `plugin/StreamDockVoicemeeter.exe`.
- `property-inspector.*`: Stream Dock settings UI, shared across all actions.
- `icons/`: plugin icon assets.
- `scripts/package-plugin.js`: creates a distributable `.sdPlugin` directory from `dist/plugin`.
- `scripts/release.ps1`: publishes the C# plugin and creates the release zip.

## Build

Per this workspace's guardrails, `.NET` builds must run inside Docker, not on the host.

The supported release build path uses Windows Docker:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\build-release-in-windows-docker.ps1
```

Linux Docker verification path:

```bash
npm run release:zip:linux
```

JavaScript and manifest checks (safe to run on the host):

```bash
npm run check
```

## Output

Release output is written to:

```text
dist/release/streamdock-voicemeeter-0.1.19.zip
```

The packaged plugin directory is:

```text
dist/stream-dock-voicemeeter.sdPlugin
```

## Install

Install the packaged plugin locally (on the Windows machine running Stream Dock and Voicemeeter):

```powershell
.\scripts\install-local.ps1
```

`git push` and release publishing are intentionally left to the user.
