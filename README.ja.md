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

## 必要なもの

- Windows 10 1809 以降、x64
- INZONE の USB ドングル（接続済み）と、ケースから出して接続済みのイヤホン
- ビルドには .NET 8 SDK

INZONE Hub を閉じる必要はありません。制御インターフェースは共有を許可して開くので、両方を同時に
接続できます。Hub のスライダーが追従して動くのが見えるので、試すときはむしろ開いておくと便利です。

## クイックスタート

### 1. ビルド

```sh
dotnet publish src/OpenInzone.Cli    -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
dotnet publish src/OpenInzone.Daemon -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

`publish/` に単独で動く実行ファイルが 2 つできます。実行する PC には何もインストールしなくて
構いません。.NET 8 ランタイムが既に入っているなら `--self-contained true` を外すとバイナリは
ずっと小さくなります。

<details>
<summary>WSL からビルドする場合</summary>

追加の設定なしに WSL からクロスビルドできます。プロジェクトのターゲットは `net8.0` で、Windows
には P/Invoke と COM を通してしか触れないためです。SDK をパッケージマネージャではなく
`dotnet-install.sh` で入れた場合は、先にパスを通してください。

```sh
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$HOME/.dotnet:$PATH"
```

できあがった `.exe` は WSL のパスからそのまま相互運用で実行できるので、リポジトリのディレクトリで
`./publish/inzone.exe status` が通ります。
</details>

### 2. ドングルが見つかるか確認する

```console
$ ./publish/inzone.exe devices
VID_054C&PID_0EC2 UsagePage=0xFF04 Usage=0x0001 In=64 Out=64 "Hid Interface"
  \\?\hid#vid_054c&pid_0ec2&mi_05&col03#8&29ddaaec&0&0002#{4d1e55b2-f16f-11cf-88cb-001111000030}
```

何も出ないときは、ドングルが挿さっていないか、そのモデルのプロダクト ID をフィルタが認識できて
いません。「困ったときは」を参照してください。

### 3. 現在の設定を読む

```console
$ ./publish/inzone.exe status
Device       INZONE Buds
Serial       L 3015430 / R 3015430 / dongle 3015430
Battery      L 97%  R 97%  case 34%
Balance      50 (0.0)
Volume       15/30
Microphone   unmuted, level 100%
Sidetone     0
```

ここで数値が出れば、以降の操作はすべて動きます。

### 4. 何か変えてみる

ターミナルの隣に INZONE Hub を開いて、スライダーが動くのを見ながら実行してみてください。

```console
$ ./publish/inzone.exe balance +10
60 (+1.0)

$ ./publish/inzone.exe balance +10
70 (+2.0)

$ ./publish/inzone.exe balance centre
50 (0.0)
```

括弧の中は INZONE Hub が表示する -5.0 〜 +5.0 のスケールです。

逆方向も見てみましょう。次を起動したまま、INZONE Hub かイヤホン本体からバランスを変えてみてくだ
さい。

```console
$ ./publish/inzone.exe watch
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

### 5. キーに割り当てる

```console
$ ./publish/inzoned.exe config/hotkeys.example.json
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

`Ctrl+Alt+Up` を押せば、どのアプリケーションからでもバランスが動きます。変更はコンソールに出るの
で、「押したが何も起きなかったホットキー」と「そもそも届いていないホットキー」を区別できます。

```
  balance  60 (+1.0)
  mic      level 95%
```

引数なしで起動すると `%APPDATA%\openinzone\hotkeys.json` を使います。初回はそのファイルを上記の
既定値で書き出します。

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

### どのボリュームがどれか

名前から受ける印象と実体がずれているので、はっきり書いておきます。

| コマンド | 何を動かすか |
|---|---|
| `inzone volume` | **ヘッドセット自身**の音量 0–30。INZONE Hub が見せているスライダーと同じもの |
| `inzone mic` のレベル | ヘッドセットに対応する **Windows のキャプチャ端点** 0–100 |
| `inzone mic mute` | **ヘッドセット自身**のマイクミュート |

`inzone volume` は Windows の再生音量には触れません。INZONE Hub も触れていません。マイクだけは
INZONE Hub が 2 つの世界にまたがって扱っており、本ツールもそれに合わせています。

## ホットキーデーモン

`inzoned.exe` は接続を開いたまま保持し、グローバルホットキーを待ち受けます。デバイスを開いたまま
現在値をキャッシュするので、キーを押しっぱなしにしても 1 回の押下につき読み出し＋書き込みではなく
書き込み 1 回で済み、連打に対する反応が鈍りません。

```sh
inzoned                       # %APPDATA%\openinzone\hotkeys.json を使う
inzoned C:\path\to\keys.json  # 別の場所を指定する
```

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

### Windows の起動時に立ち上げる

`shell:startup` で開くフォルダに `inzoned.exe` のショートカットを置いてください。コンソール
ウィンドウを出したくない場合は `src/OpenInzone.Daemon/OpenInzone.Daemon.csproj` に
`<OutputType>WinExe</OutputType>` を足してビルドしなおします。ただしこれをすると上記のメッセージ
も見えなくなるので、先にバインドが動くことを確認してからにしてください。

## ライブラリとして使う

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

## 困ったときは

**`No INZONE dongle found.` と出る**
ドングルが挿さっていないか、本ツールが知らないプロダクト ID で認識されています。`inzone devices`
を実行し、それも空なら、ベンダー `054C`・ユーセージページ `0xFF04` のデバイスが存在するか確認して
ください。探索は固定のプロダクト ID ではなく能力で照合するので、別の INZONE モデルでも見つかる
はずです。

**`The headset did not answer ... within 1500 ms.` と出る**
ドングルはあるがイヤホンに届いていません。ケースに入ったまま、通信範囲外、または電源が切れていま
す。ケースから出して `inzone status` をもう一度試してください。

**ホットキーが「既に取得済み」と報告される**
別の何かがその組み合わせを先に登録しています。グラフィックスドライバやチャットアプリがよくある
原因です。設定で別の組み合わせを選んでください。残りのバインドはそのまま動きます。

**`inzone mic` でミュート状態は出るがレベルが出ない**
Windows がそのヘッドセットのキャプチャ端点を公開していません。ミュートフラグはヘッドセット側にあ
るので動き続けますが、レベルは Windows 側の設定なのでその端点が必要です。

**デーモンの出力をパイプすると何も出てこない**
プロセスを kill すると、シェルがバッファしていた分は捨てられます。デーモンは 1 行ごとにフラッシュ
しているので、`inzoned | tee log.txt` ならリアルタイムに出力が見えます。

## 仕組み

ドングルはゲーム音声とチャット音声を 2 つの独立した USB オーディオ端点として公開し、ハードウェア
側でミックスしています。バランス、ヘッドホン音量、各ミュートフラグはヘッドセット側の設定であり、
ユーセージページ `0xFF04` のベンダー HID コレクション越しに、ソニー独自のパケット形式でやり取り
します。

`docs/PROTOCOL.md` にワイヤフォーマットの全容を、各項目を INZONE Hub のどこから読み取ったか、
どの部分を実機で確認したかも含めてまとめてあります。

## テスト

プロトコル層には単体テストがあります。実機を使わない純粋な managed コードなので、SDK が動く環境
ならどこでも、WSL でも実行できます。

```sh
dotnet test
```

期待値は `docs/PROTOCOL.md` の実機キャプチャに基づく worked example から取っています。フレーミン
グ、アドレスのニブル、リトルエンディアンのトランザクション ID、そして各チェックサムの開始位置を
固定します。最後のものはコマンドとイベントで異なり、間違いが再び混入しやすい箇所です。

デバイス探索、レポート I/O、Windows のオーディオ端点は実機が必要なため対象外です。

## 構成

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
