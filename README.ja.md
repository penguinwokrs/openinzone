# OpenInzone

[English](README.md) · 日本語

INZONE Hub のデバイス制御を独自に実装しなおした、非公式のオープンソース実装です。Windows 向けの
コマンドラインツールと、ホットキー常駐プログラムが入っています。

> **ソニーとは無関係です。** OpenInzone は独立したプロジェクトであり、ソニーグループ株式会社およ
> びその関連会社から承認・後援・推奨を受けたものではなく、関連もありません。「Sony」「INZONE」
> 「INZONE Hub」はソニーグループ株式会社またはその関連会社の商標であり、本プロジェクトが対象と
> するハードウェアおよびベンダーアプリケーションを指し示す目的でのみ使用しています。

INZONE Hub を起動せずに、ソニーの INZONE ヘッドセットをコマンドラインや物理キーから操作できます。

INZONE Hub でもヘッドホン音量とゲーム/チャットバランスは変えられますが、操作は Hub のウィンドウ
からだけです。本ツールはドングルと同じ HID チャンネルで直接やり取りするので、同じ設定をキーに
割り当てたり、スクリプトから叩いたり、ステータスバーに読み出したりできます。

**INZONE Buds**（`VID_054C` / `PID_0EC2`）で実装・動作確認しています。プロトコルは INZONE
シリーズで共通なので他のモデルでも動く見込みですが、検証済みなのは INZONE Buds だけです。

## できること

- ゲーム/チャットバランス 0–100
- ヘッドホン音量 0–30、およびミュート
- マイクのミュートとレベル
- 左右のイヤホンとケースのバッテリー残量
- 他の経路で加えられた変更の購読（イヤホン本体の操作を含む）

## 動作環境

- Windows 10 1809 以降、x64
- INZONE の USB ドングル（接続済み）と、ケースから出して接続済みのイヤホン

これ以外に必要なものはありません。配布物は単独で動く形式なので、.NET ランタイムを先に入れる必要
もありません。

対応するのは Windows だけです。Linux では [zoneout](https://github.com/marcinjakubowski/zoneout)
が同じデバイスを、より広くカバーしています。[関連プロジェクト](#関連プロジェクト)を参照して
ください。

INZONE Hub を閉じる必要はありません。制御インターフェースは共有を許可して開くので、両方を同時に
接続できます。Hub のスライダーが追従して動くのが見えるので、試すときはむしろ開いておくと便利です。

## インストール

### 1. ダウンロード

[最新リリース](https://github.com/penguinwokrs/openinzone/releases/latest)から
`OpenInzone-win-x64.zip` を取得します。中身は 2 つのプログラムです。

| | |
|---|---|
| `inzone.exe` | コマンドラインツール。設定の読み出しと変更 |
| `inzoned.exe` | ホットキー常駐プログラム。設定をキーに割り当てる |

### 2. 展開して置く

Windows ターミナルか PowerShell を開いて（スタートボタンを右クリック →**ターミナル**）、次を実行
します。

```powershell
$dir = "$env:LOCALAPPDATA\OpenInzone"
Expand-Archive "$env:USERPROFILE\Downloads\OpenInzone-win-x64.zip" -DestinationPath $dir -Force
Get-ChildItem $dir -Recurse | Unblock-File
```

`Unblock-File` は、インターネットから取得したファイルに Windows が付ける印を外します。これをしな
いと初回起動時に**「WindowsによってPCが保護されました」**が出ます。コード署名はしていないので、
それでも SmartScreen が出る場合は**詳細情報 → 実行**を選んでください。

### 3. PATH を通す

そのフォルダ以外からも `inzone` で呼べるようにします。

```powershell
[Environment]::SetEnvironmentVariable(
    "Path",
    [Environment]::GetEnvironmentVariable("Path", "User") + ";$env:LOCALAPPDATA\OpenInzone",
    "User")
```

反映にはターミナルを閉じて開きなおす必要があります。

この手順は任意です。省いた場合は、以下の例で `inzone` と書いてあるところを、フォルダ内で
`.\inzone.exe` と読み替えてください。

### 4. ヘッドセットが見つかるか確認する

ドングルを挿し、イヤホンをケースから出した状態で実行します。

```console
PS> inzone status
Device       INZONE Buds
Serial       L 3015430 / R 3015430 / dongle 3015430
Battery      L 97%  R 97%  case 34%
Balance      50 (0.0)
Volume       15/30
Microphone   unmuted, level 100%
Sidetone     0
```

ここで数値が出れば、以降の操作はすべて動きます。エラーが出た場合は[困ったときは](#困ったときは)
を参照してください。

## 使い方

### 何か変えてみる

ターミナルの隣に INZONE Hub を開いて、スライダーが動くのを見ながら実行してみてください。

```console
PS> inzone balance +10
60 (+1.0)

PS> inzone balance +10
70 (+2.0)

PS> inzone balance centre
50 (0.0)
```

括弧の中は INZONE Hub が表示する -5.0 〜 +5.0 のスケールです。

ヘッドホン音量とマイクも同じ要領です。

```console
PS> inzone volume 20
20/30

PS> inzone volume -1
19/30

PS> inzone mic toggle
muted
```

### 変更をリアルタイムに見る

次を起動したまま、INZONE Hub かイヤホン本体からバランスを変えてみてください。

```console
PS> inzone watch
Watching INZONE Buds. Press Ctrl+C to stop.
01:20:20  GameChatMixBalance     60 (+1.0)
01:20:21  HeadphoneVolume        16/30
01:20:23  BatteryInfo            L 94%  R 94%  case 34%
01:20:24  MicVolume              muted
```

ヘッドセットからの応答はインターフェースを開いているすべてのプログラムに届くので、`watch` には
イヤホン本体での変更だけでなく、INZONE Hub や本ツールの別プロセスが起こした通信も出ます。1 回の
変更で同じ行が複数出るのは正常です。ヘッドセットは要求に応答したうえで、新しい値を改めて通知する
ためです。

ステータスバーや配信オーバーレイに出せるのはこれのおかげです。パイプで受けて読むだけで済みます。

### キーに割り当てる

`inzoned.exe` は常駐して接続を開いたまま保持し、グローバルホットキーを待ち受けます。引数なしで
起動すると `%APPDATA%\openinzone\hotkeys.json` を使い、初回はそのファイルを既定値で書き出します。

```console
PS> inzoned
Ctrl+Alt+Up          balance +10
Ctrl+Alt+Down        balance -10
Ctrl+Alt+Home        balance = 50
Ctrl+Alt+Right       volume +1
Ctrl+Alt+Left        volume -1
Ctrl+Alt+Shift+M     mic-mute
Ctrl+Alt+PageUp      mic-level +5
Ctrl+Alt+PageDown    mic-level -5

Listening. Press Ctrl+C to stop.
Connected to INZONE Buds - battery L 98%  R 97%  case 34%
```

`Ctrl+Alt+Up` を押せば、どのアプリケーションからでも——フルスクリーンのゲーム中でも——バランスが
動きます。変更はコンソールに出るので、「押したが何も起きなかったホットキー」と「そもそも届いて
いないホットキー」を区別できます。

```
  balance  60 (+1.0)
  mic      level 95%
```

デバイスを開いたまま現在値をキャッシュするので、キーを押しっぱなしにしても 1 回の押下につき
読み出し＋書き込みではなく書き込み 1 回で済み、連打に対する反応が鈍りません。

### 割り当てを編集する

`%APPDATA%\openinzone\hotkeys.json` を開きます（`notepad $env:APPDATA\openinzone\hotkeys.json`）。
別の場所を使いたい場合は `inzoned C:\path\to\keys.json` のようにパスを渡します。

```json
{
  "bindings": [
    { "keys": "Ctrl+Alt+Up",    "action": "balance",   "delta": 10 },
    { "keys": "Ctrl+Alt+Down",  "action": "balance",   "delta": -10 },
    { "keys": "Ctrl+Alt+Home",  "action": "balance",   "value": 50 },
    { "keys": "Ctrl+Alt+Right", "action": "volume",    "delta": 1 },
    { "keys": "Ctrl+Alt+Left",  "action": "volume",    "delta": -1 },
    { "keys": "Ctrl+Alt+Shift+M",  "action": "mic-mute" },
    { "keys": "Ctrl+Alt+PageUp",   "action": "mic-level", "delta": 5 },
    { "keys": "Ctrl+Alt+PageDown", "action": "mic-level", "delta": -5 }
  ]
}
```

**アクション**: `balance`、`volume`、`mic-level`、`volume-mute`、`mic-mute`。
はじめの 3 つは、ステップで動かす `delta` か、値に飛ぶ `value` のどちらかを取ります。

**キー**: 修飾キー `Ctrl`、`Alt`、`Shift`、`Win` と、通常キー 1 つの組み合わせ。使えるのは英数字、
`F1`–`F24`、方向キー、`Home`、`End`、`PageUp`、`PageDown`、`Insert`、`Delete`、`Space`、`Enter`、
`Tab`、`Escape`、`Backspace`、テンキーの演算子キー、およびメディアキー `VolumeUp`、`VolumeDown`、
`VolumeMute`、`MediaNext`、`MediaPrev`、`MediaStop`、`MediaPlayPause` です。

他のアプリケーションが既に取得している組み合わせは、報告したうえで読み飛ばします。残りのバイン
ドは通常どおり登録されます。

編集したらデーモンを起動しなおしてください。

### Windows の起動時に立ち上げる

`Win+R` で `shell:startup` を開き、そのフォルダに `inzoned.exe` のショートカットを置きます。手早く
作るなら次のとおりです。

```powershell
$link = (New-Object -ComObject WScript.Shell).CreateShortcut(
    "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\Startup\OpenInzone.lnk")
$link.TargetPath = "$env:LOCALAPPDATA\OpenInzone\inzoned.exe"
$link.Save()
```

実行中はコンソールウィンドウが残ります。最小化しておけば十分ですが、完全に消すにはビルドが必要
です。[コンソールウィンドウを消す](#コンソールウィンドウを消す)を参照してください。

## コマンド一覧

```
inzone status                 すべてまとめて表示
inzone devices                見つかった制御インターフェースを列挙

inzone balance                ゲーム/チャットバランスを表示
inzone balance 70             設定する（0 = チャットのみ、100 = ゲームのみ）
inzone balance +10 | -10      1 ステップ動かす
inzone balance centre         中央に戻す

inzone volume                 ヘッドホン音量を表示
inzone volume 20              設定する（0-30）
inzone volume +1 | -1         1 ステップ動かす
inzone volume mute | unmute | toggle

inzone mic                    マイクの状態を表示
inzone mic mute | unmute | toggle
inzone mic 50                 レベルを設定する（0-100）
inzone mic +5 | -5            レベルを 1 ステップ動かす

inzone battery                残量を表示
inzone watch                  変更が起きるたびに出力
```

`inzone --help` でも同じ一覧が出ます。

### どのボリュームがどれか

名前から受ける印象と実体がずれているので、はっきり書いておきます。

| コマンド | 何を動かすか |
|---|---|
| `inzone volume` | **ヘッドセット自身**の音量 0–30。INZONE Hub が見せているスライダーと同じもの |
| `inzone mic` のレベル | ヘッドセットに対応する **Windows のキャプチャ端点** 0–100 |
| `inzone mic mute` | **ヘッドセット自身**のマイクミュート |

`inzone volume` は Windows の再生音量には触れません。INZONE Hub も触れていません。マイクだけは
INZONE Hub が 2 つの世界にまたがって扱っており、本ツールもそれに合わせています。

## 困ったときは

**`No INZONE dongle found.` と出る**
ドングルが挿さっていないか、本ツールが知らないプロダクト ID で認識されています。`inzone devices`
を実行すると、見つかったものが並びます。

```console
PS> inzone devices
VID_054C&PID_0EC2 UsagePage=0xFF04 Usage=0x0001 In=64 Out=64 "Hid Interface"
  \\?\hid#vid_054c&pid_0ec2&mi_05&col03#8&29ddaaec&0&0002#{4d1e55b2-f16f-11cf-88cb-001111000030}
```

これも空なら、ベンダー `054C`・ユーセージページ `0xFF04` のデバイスが存在するか確認してください。
探索は固定のプロダクト ID ではなく能力で照合するので、別の INZONE モデルでも見つかるはずです。

**`The headset did not answer ... within 1500 ms.` と出る**
ドングルはあるがイヤホンに届いていません。ケースに入ったまま、通信範囲外、または電源が切れていま
す。ケースから出して `inzone status` をもう一度試してください。

**「WindowsによってPCが保護されました」が出る、exe が起動しない**
ダウンロード時の印が残っています。
`Get-ChildItem $env:LOCALAPPDATA\OpenInzone -Recurse | Unblock-File` を実行してください。コード
署名はしていないので、それでも SmartScreen が出る場合は**詳細情報 → 実行**が必要です。

**`inzone` がコマンドとして認識されない**
PATH の手順を飛ばしたか、ターミナルがそれより前から開いています。新しいターミナルを開くか、パスを
指定して実行してください: `& "$env:LOCALAPPDATA\OpenInzone\inzone.exe" status`

**ホットキーが「既に取得済み」と報告される**
別の何かがその組み合わせを先に登録しています。グラフィックスドライバやチャットアプリがよくある
原因です。設定で別の組み合わせを選んでください。残りのバインドはそのまま動きます。

**`inzone mic` でミュート状態は出るがレベルが出ない**
Windows がそのヘッドセットのキャプチャ端点を公開していません。ミュートフラグはヘッドセット側にあ
るので動き続けますが、レベルは Windows 側の設定なのでその端点が必要です。

**デーモンの出力をパイプすると何も出てこない**
プロセスを kill すると、シェルがバッファしていた分は捨てられます。デーモンは 1 行ごとにフラッシュ
しているので、`inzoned | tee log.txt` ならリアルタイムに出力が見えます。

---

## 開発者向け

ここから先は、ソースからビルドして開発するための説明です。配布物を使うだけなら必要ありません。

### 必要なもの

- .NET 8 SDK
- プロトコルのテスト以外を触るなら、ドングルとイヤホン本体
- 実行には Windows。ビルド自体は Linux や WSL からもできます

プロジェクトのターゲットは `net8.0` で、Windows には P/Invoke と COM を通してしか触れないため、
SDK が動く環境ならどこでもコンパイルできます。Windows 専用なのは、できあがった `.exe` だけです。

### Windows でビルドする

```powershell
winget install Microsoft.DotNet.SDK.8
git clone https://github.com/penguinwokrs/openinzone.git
cd openinzone

dotnet publish src\OpenInzone.Cli    -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
dotnet publish src\OpenInzone.Daemon -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

`publish\` に `inzone.exe` と `inzoned.exe` ができます。実行する PC には何もインストールしなくて
構いません（リリースの zip と同じものです）。.NET 8 ランタイムが既に入っているなら
`--self-contained true` を外すとバイナリはずっと小さくなります。

```console
PS> .\publish\inzone.exe status
```

publish せずに素早く回すなら `dotnet run --project src\OpenInzone.Cli -- status` でも動きます。

### WSL からビルドする

追加の設定なしに WSL からクロスビルドできます。SDK をパッケージマネージャではなく
`dotnet-install.sh` で入れた場合は、先にパスを通してください。

```sh
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$HOME/.dotnet:$PATH"
```

```sh
dotnet publish src/OpenInzone.Cli    -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
dotnet publish src/OpenInzone.Daemon -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

できあがった `.exe` は WSL のパスからそのまま相互運用で実行できるので、リポジトリのディレクトリで
`./publish/inzone.exe status` が通ります。ドングルには Windows 側を経由して届くので、USB の
パススルー設定などは要りません。

例外はデーモンです。グローバルホットキーは Windows のセッションに対して登録されるため、WSL は
動作確認の場所として向いていません。起動はしますが、Windows のターミナルから試してください。

### テスト

プロトコル層には単体テストがあります。実機を使わない純粋な managed コードなので、SDK が動く環境
ならどこでも、WSL でも実行できます。

```sh
dotnet test
```

期待値は `docs/PROTOCOL.md` の実機キャプチャに基づく worked example から取っています。フレーミン
グ、アドレスのニブル、リトルエンディアンのトランザクション ID、そして各チェックサムの開始位置を
固定します。最後のものはコマンドとイベントで異なり、間違いが再び混入しやすい箇所です。

デバイス探索、レポート I/O、Windows のオーディオ端点は実機が必要なため対象外です。

### 構成

```
src/OpenInzone.Core       プロトコルとトランスポート
  Native/                 P/Invoke と COM の宣言
  Hid/                    デバイス探索とレポート I/O
  Protocol/               パケットのコーデックと要求/応答セッション
  Audio/                  ヘッドセットの Windows キャプチャ端点
  Model/                  各設定の型付きの値
src/OpenInzone.Cli        inzone.exe
src/OpenInzone.Daemon     inzoned.exe
tests/OpenInzone.Core.Tests
  Protocol/               docs/PROTOCOL.md と突き合わせたパケットコーデックのテスト
docs/PROTOCOL.md          解析したワイヤフォーマット
config/                   ホットキー設定の例
```

Visual Studio や Rider 用に、4 つのプロジェクトを `OpenInzone.sln` がまとめています。

### コンソールウィンドウを消す

デーモンのコンソールウィンドウを出したくない場合は、
`src/OpenInzone.Daemon/OpenInzone.Daemon.csproj` に `<OutputType>WinExe</OutputType>` を足して
ビルドしなおします。ただしこれをすると起動時の一覧も変更時のエコーも見えなくなるので、先にバイン
ドが動くことを確認してからにしてください。

### ライブラリとして使う

`OpenInzone.Core` はフレームワーク以外に依存がありません。ライセンスは GPL-3.0-only なので、これ
にリンクして配布するものは同じく GPL-3.0 になります。

```csharp
using var device = InzoneDevice.Open();

Console.WriteLine(device.GetModelInfo().Name);   // INZONE Buds
device.AdjustMixBalance(+10);
device.ToggleMicMute();
device.SetMicLevel(80);

device.SettingChanged += (_, e) => Console.WriteLine($"{e.EventId} changed");
```

このラッパーがまだモデル化していない設定には、`device.Session` から任意のイベント ID に対して生の
`Get` / `Set` を投げられます。判明している範囲は `docs/PROTOCOL.md` にまとめてあります。

### 仕組み

ドングルはゲーム音声とチャット音声を 2 つの独立した USB オーディオ端点として公開し、ハードウェア
側でミックスしています。バランス、ヘッドホン音量、各ミュートフラグはヘッドセット側の設定であり、
ユーセージページ `0xFF04` のベンダー HID コレクション越しに、ソニー独自のパケット形式でやり取り
します。

`docs/PROTOCOL.md` にワイヤフォーマットの全容を、各項目を INZONE Hub のどこから読み取ったか、
どの部分を実機で確認したかも含めてまとめてあります。

## 関連プロジェクト

同じハードウェアに取り組んでいる人は他にもいます。重なる範囲について、それぞれの役割は次のとおり
です。

| プロジェクト | 対応 OS | 内容 |
|---|---|---|
| [HeadsetControl](https://github.com/Sapd/HeadsetControl) | Windows / macOS / Linux | 多数のゲーミングヘッドセットを 1 つの CLI から操作する。INZONE H5 はサイドトーン・チャットミックス・マイク音量に対応、INZONE Buds はバッテリーのみ（受動読み取り） |
| [zoneout](https://github.com/marcinjakubowski/zoneout) | Linux | INZONE H9 II と INZONE Buds 向けの CLI・Qt GUI・Python ライブラリ。本プロジェクトより深くデバイスに触れており、ノイズキャンセリング、アンビエントサウンド、自動電源オフ、音声ガイド言語、起動時既定に対応 |
| [LINZONE Hub](https://github.com/patyhank/linzone-hub) | Linux | GUI と CLI に加え、INZONE のバッテリーを `power_supply` として公開する DKMS モジュールを持つ。UPower やデスクトップシェルから見えるようになる |
| [inzone-linux](https://github.com/smartinio/inzone-linux) | Linux | INZONE Buds のバッテリーを読む。トレイアイコンも任意で使える |

Windows 側はバッテリー表示が中心です。
[takamachi66](https://github.com/takamachi66/inzone-buds-battery-tray) と
[kinako19](https://github.com/kinako19/inzone-buds-battery-tray) はどちらも INZONE Buds の残量を
通知領域に出し、[InzoneBudsBattery](https://github.com/zxe-ll/InzoneBudsBattery) はファイナル
ファンタジー XIV の中に出します。いずれも読むだけで、書き込みはしません。**Windows でスクリプト
やキーから設定を変える**——本プロジェクトが足しているのはそこです。

Linux なら zoneout から始めるほうが良いです。デバイスのより広い範囲をカバーしており、音量・
バランス・ミュート以外の機能については本プロジェクトも zoneout に要望を出す側になります。

### プロトコルは二度発見された

zoneout の `SPECS.md` は同じワイヤフォーマットを、独立に、しかも逆方向から——ベンダーアプリでは
なくキャプチャから——文書化しています。両者は一致します。キー ID `96 C3`、イベント ID の `0x21`
音量・`0x22` バランス・`0x23` サイドトーン・`0x24` マイク・`0x04` バッテリー、イベントのアドレス
バイトの `0x14`、そして `0xA0`。HeadsetControl の INZONE H5 ドライバも「Sony vendor HCI COMMAND
を組み立てて対応する EVENT を待つ」と書いており、同じフレーミングが 3 度目に出てきます。

違いは中身ではなく一般性の度合いです。zoneout はコマンドごとに値のオフセット、チェックサムの
オフセット、定数を挙げます。`docs/PROTOCOL.md` はそのオフセットが導かれる元のフレーミングを記述
しており、結果としてそれらの定数は各コマンドの固定ヘッダ部分の総和になります。別々に読まれた
2 つが一致しているというのは、ソニー自身が認めない限り得られる中で最も強い裏付けです。

---

## ライセンス

GPL-3.0-only。`LICENSE` を参照してください。

このプログラムは有用であることを願って配布されますが、**いかなる保証もありません**。商品性や特定
目的への適合性についての暗黙の保証さえありません。文書化されていないチャンネル越しにハードウェア
を操作するものであり、利用は自己責任でお願いします。

## 商標と適用範囲

OpenInzone は独立した非営利のプロジェクトです。ソニーグループ株式会社およびその関連会社から承認・
後援・推奨を受けたものではなく、関連もありません。

「Sony」「INZONE」「INZONE Hub」はソニーグループ株式会社またはその関連会社の商標です。本リポジト
リでこれらを使うのは、対象のハードウェアと、その挙動を再現する対象であるベンダーアプリケーション
を指し示すためだけであり、その目的に必要な範囲を超えて使用していません。ソニーのロゴ、製品写真、
書体、INZONE Hub の意匠は一切使用も再配布もしていません。

本プロジェクトの目的は相互運用性です。自分が所有するハードウェアを、自分が選んだソフトウェアから
使えるようにすることです。`docs/PROTOCOL.md` のワイヤフォーマットは観測した挙動の記述であり、実機
で確認したものです。INZONE Hub から取得したコード・リソース・アセットは含みません。

以下は意図的に対象外であり、コントリビューションも受け付けません。

- ファームウェアの更新・抽出、およびソニーのファームウェアイメージの再配布
- 何らかの保護・ライセンスチェック・制限の回避
- INZONE Hub の一部の再配布（逆コンパイラや逆アセンブラの出力を含む）
- 本プロジェクトを公式またはソニー公認の製品であるかのように見せること

ファームウェアが想定しない値を書き込むのは、何が起きるかを身をもって知る方法です。ここで扱う範囲
は INZONE Hub 自身が送っている値に合わせてあります。
