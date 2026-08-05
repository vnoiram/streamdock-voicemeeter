# streamdock-voicemeeter

English version: [README.md](README.md)

VB-Audio Voicemeeter (Standard/Banana/Potato) の strip / bus を直接操作する Mirabox Stream Dock 用プラグイン。C# プラグインプロセスとして動作する。姉妹プラグイン [`streamdock-sonar`](../streamdock-sonar) と同じアーキテクチャを踏襲しつつ、通信層は HTTP API ではなく Voicemeeter 純正の Remote API（`VoicemeeterRemote64.dll`）を P/Invoke 経由で利用する。

## バージョン

現行バージョン: `0.1.15`。

## サポート範囲

エディション非依存設計: strip/bus のインデックスと種別（`strip`/`bus`）は Property Inspector 上で自由に設定でき、エディションごとにハードコードしていない。プラグインは `Strip[0..7]` / `Bus[0..7]`（Standard/Banana/Potato の中で最大のPotatoに合わせた範囲）を常にポーリングするため、どのエディションでも同じビルドで動作する。手元のエディションに存在しないインデックスを指定した場合は単にエラー状態として表示される。

`Recorder` と `EQ Toggle` が使用するパラメータ名（`Recorder.Record`、`Bus[i].EQ.on`）は、VB-Audio の検証済み公式仕様ではなくコミュニティ資料に基づく。実運用前に Diagnostics アクション経由で実機の Voicemeeter に対して確認すること。

## アクション

- `Voicemeeter Gain`: strip/bus の Gain を dB 単位で調整。キー押下で `Step` 分変化、ノブ回転で増減、ノブ押下でミュートtoggle。
- `Voicemeeter Mute`: strip/bus のミュートをtoggle。
- `Voicemeeter Solo`: strip の Solo をtoggle。
- `Voicemeeter Mono`: strip/bus の Mono ダウンミックスをtoggle。
- `Voicemeeter Overview`: 選択した strip/bus の Gain/Mute 状態をキー画像として表示。
- `Voicemeeter Balance Dial`: 設定した2チャンネルの Gain を逆方向に動かし ChatMix 風のバランス感を再現。ノブ押下で両方 `0dB` にリセット。
- `Voicemeeter Output Device`: bus のハードウェア出力デバイスを切り替え。
- `Voicemeeter Rotate Output`: bus の出力デバイスを列挙リストの次のデバイスへローテーション。
- `Voicemeeter Input Device`: 物理stripの入力デバイスを切り替え。
- `Voicemeeter Rotate Input`: 物理stripの入力デバイスを列挙リストの次のデバイスへローテーション。
- `Voicemeeter Macro Button`: Voicemeeter MacroButtons の論理ボタン（インデックス `0`-`79`）を押下/解放し、表示状態を反映。
- `Voicemeeter Recorder`: Voicemeeter 内蔵レコーダーをtoggle。
- `Voicemeeter EQ Toggle`: bus の EQ on/off をtoggle。
- `Voicemeeter App Control`: Voicemeeter アプリの表示・再起動・終了。
- `Diagnostics`: DLL探索パス、ログイン状態、検出したエディション/バージョン、直近の操作結果を Property Inspector に送信。

## ランタイム動作

Sonar のローカル HTTPS API と異なり、Voicemeeter にはネットワークサーバーが存在しない。プラグインは `VoicemeeterRemote64.dll` を P/Invoke でロードし、ログインセッション（`VBVMR_Login`/`VBVMR_Logout`）を管理する。

- DLL探索はまずレジストリ（`HKEY_LOCAL_MACHINE\SOFTWARE\VB:Audio\Voicemeeter`、WOW6432Node および `Voicemeter`/`Voicemeeter` の表記ゆれもフォールバック）からインストールディレクトリを取得し、取得できない場合は既定パス `C:\Program Files (x86)\VB\Voicemeeter\VoicemeeterRemote64.dll` / `C:\Program Files\VB\Voicemeeter\VoicemeeterRemote64.dll` にフォールバックする。
- Voicemeeter がインストール済みだが起動していない場合、どのエディションを自動起動すべきか推測せず、明確な「未起動」エラーを表示する。プロセス起動中に一度エディションを検出できていれば、以後の切断時にはそのエディションで `VBVMR_RunVoicemeeter` を試みてからログインを再試行する。
- 状態（全 `Strip[0..7]`/`Bus[0..7]` の gain/mute）は `VBVMR_IsParametersDirty()` を約1秒間隔でポーリングして更新し、同じチャンネルを表示する全ボタンが一斉に更新される（Sonar の共有ステートキャッシュと同じパターン）。
- Stream Dock 本体がプラグイン WebSocket を閉じた場合、またはプラグインプロセスが通常終了する場合は、共有 runtime を破棄して `VBVMR_Logout()` を呼び、Voicemeeter 側に古い Remote API セッションを残さない。
- Gain は dB 単位の `float` で `-60.0`〜`+12.0` にクランプされる。
- デバイス割り当ては Voicemeeter の文字列パラメータ（`Strip[i].device.<driver>` / `Bus[i].device.<driver>`、`<driver>` は `mme`/`wdm`/`ks`/`asio` のいずれか）を使用し、デバイス一覧は `VBVMR_Input_GetDeviceDescA`/`VBVMR_Output_GetDeviceDescA` で列挙する。
- MacroButtons はキー押下/解放時に `DEFAULT` bitmode で `VBVMR_MacroButton_SetStatus` を呼ぶ（物理ボタンのクリックと同様に press/release 両方が発火する）。表示用の on/off 状態は `STATEONLY` で読み取る。

Property Inspector はライブデータ（デバイス一覧、マクロボタン状態、診断情報）を `sendToPlugin` 経由で要求する。これらのリクエストは Property Inspector 自身の接続コンテキストを使い、保存済み設定はアクションのコンテキストを使う。`streamdock-sonar` と同じ `replyContext` の規約に従っている。

## ログ

C# プラグインは `StreamDockVoicemeeter.exe` と同じディレクトリに `streamdock-voicemeeter.log` を出力する:

```text
stream-dock-voicemeeter.sdPlugin\plugin\streamdock-voicemeeter.log
```

ログ設定前にプロセスが失敗した場合は、同じディレクトリに `startup-error.log` が出力される。

## リポジトリ構成

- `plugin-csharp/`: C# Stream Dock プラグインのソース。`VoicemeeterClient.cs`（P/Invoke ラッパー）と `Actions/`（アクションごとのハンドラ）を含む。
- `manifest.json`: `plugin/StreamDockVoicemeeter.exe` を指す Stream Dock プラグインマニフェスト。
- `property-inspector.*`: 全アクション共通の Stream Dock 設定UI。
- `icons/`: プラグインアイコン資産。
- `scripts/package-plugin.js`: `dist/plugin` から配布用 `.sdPlugin` ディレクトリを作成する。
- `scripts/release.ps1`: C# プラグインをpublishし、リリースzipを作成する。

## ビルド

このワークスペースのガードレールに従い、`.NET` のビルドはホストではなく Docker 内で実施すること。

サポートされているリリースビルド手順は Windows Docker:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\build-release-in-windows-docker.ps1
```

Linux Docker での検証手順:

```bash
npm run release:zip:linux
```

JavaScript / manifest のチェック（ホストで実行可能）:

```bash
npm run check
```

## 出力

リリース出力は以下に書き出される:

```text
dist/release/streamdock-voicemeeter-0.1.15.zip
```

パッケージ済みプラグインディレクトリ:

```text
dist/stream-dock-voicemeeter.sdPlugin
```

## インストール

パッケージ済みプラグインをローカルにインストールする（Stream Dock と Voicemeeter が動作している Windows マシン上で実行）:

```powershell
.\scripts\install-local.ps1
```

`git push` およびリリースの公開は意図的にユーザーに委ねている。
