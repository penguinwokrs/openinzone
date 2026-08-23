![OpenInzone — INZONE Hub なしでソニーの INZONE ヘッドセットを操作する](docs/images/banner.png)

# OpenInzone

[English](README.md) · 日本語

INZONE Hub のデバイス制御を独自に実装しなおした、非公式のオープンソース実装です。Windows 向けの
常駐トレイアプリケーションと、コマンドラインツールが入っています。

> **ソニーとは無関係です。** OpenInzone は独立したプロジェクトであり、ソニーグループ株式会社およ
> びその関連会社から承認・後援・推奨を受けたものではなく、関連もありません。「Sony」「INZONE」
> 「INZONE Hub」はソニーグループ株式会社またはその関連会社の商標であり、本プロジェクトが対象と
> するハードウェアおよびベンダーアプリケーションを指し示す目的でのみ使用しています。

INZONE Hub を起動せずに、ソニーの INZONE ヘッドセットを通知領域・物理キー・コマンドラインから操作
できます。

INZONE Hub でもヘッドホン音量とゲーム/チャットバランスは変えられますが、操作は Hub のウィンドウ
からだけです。本ツールはドングルと同じ HID チャンネルで直接やり取りするので、同じ設定を通知領域の
パネルから触ったり、キーに割り当てたり、スクリプトから叩いたり、ステータスバーに読み出したりでき
ます。

プログラムは 2 つあり、どちらのダウンロードにも両方が入っています。

| | |
|---|---|
| `inzonetray.exe` | トレイアプリケーション。通知領域のアイコン、パネル、グローバルホットキー。**ほとんどの人が欲しいのはこちらです。** |
| `inzone.exe` | コマンドラインツール。同じ設定をターミナルから、JSON 出力でスクリプトからも |

トレイアイコンを左クリックすると、こう開きます。

![OpenInzone のパネル。ヘッドホン音量・マイクレベル・ゲーム/チャットバランスの 3 つのスライダーと、その下に左右のイヤホンとケースのバッテリー残量](docs/images/flyout.png)

**INZONE Buds**（`VID_054C` / `PID_0EC2`）で実装・動作確認しています。プロトコルは INZONE
シリーズで共通なので他のモデルでも動く見込みですが、検証済みなのは INZONE Buds だけです。

## 目次

- [できること](#できること)
- [動作環境](#動作環境)
- [インストール](#インストール)
- [トレイアプリを使う](#トレイアプリを使う)
- [ホットキーと設定](#ホットキーと設定)
- [困ったときは](#困ったときは)
- [コマンドライン](#コマンドライン)
- [スクリプトから使う](#スクリプトから使う)
- [開発者向け](#開発者向け)
- [関連プロジェクト](#関連プロジェクト)
- [ライセンス](#ライセンス)
- [商標と適用範囲](#商標と適用範囲)

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

ターミナルも管理者権限も要りません。ファイルを 1 つダウンロードして実行すれば、通知領域にアイコン
が出ます。

### 1. インストーラーをダウンロードする

[最新リリース](https://github.com/penguinwokrs/openinzone/releases/latest)を開き、下のほうにある
**Assets** から**`OpenInzone-<version>-setup.exe`** をクリックします。現在のリリースなら
`OpenInzone-0.1.0-setup.exe` です。ふつうのダウンロードと同じように、ダウンロードフォルダーに
入ります。

同じ場所にある `OpenInzone-<version>-win-x64.zip` は、同じ 2 つのプログラムをインストーラー
なしで配ったものです。展開にターミナルを使うので、[コマンドライン](#コマンドライン)で説明します。

### 2. 実行して、警告を通り抜ける

ダウンロードしたファイルをダブルクリックします。**「WindowsによってPCが保護されました」**という
青い画面が出て止められます。

これは SmartScreen です。この配布物にコード署名をしていない（署名証明書は有料で、これは無償の
プロジェクトです）ために出るもので、ファイルに問題があるという意味ではありません。**詳細情報**を
クリックし、下に現れる**実行**ボタンを押してください。

インストーラーは最初に言語を尋ねます。日本語を選んでください。

### 3. 既定のまま進める

マシン全体ではなくユーザー単位で入るので、管理者権限は要求されません。プログラムは
`%LOCALAPPDATA%\Programs\OpenInzone` に置かれ、スタートメニューの項目が作られます。

途中のチェックボックスは 2 つです。

| チェックボックス | |
|---|---|
| Windows の起動時に常駐する | 既定でオン。Windows と一緒にトレイが立ち上がります |
| デスクトップにショートカットを作成する | 既定でオフ |

最後のページで、そのまま OpenInzone を起動するか尋ねられます。オンのままにして**完了**を押して
ください。

### 4. これで完了

タスクバー右端の通知領域にヘッドホンのアイコンが出ます。Windows は新しいアイコンを **^** の中に
隠すことが多いので、その矢印をクリックし、アイコンをタスクバーの上へドラッグして出しておくと
便利です。

左クリックしてみてください。モデル名と数値が出ていれば動いています。続きは
[トレイアプリを使う](#トレイアプリを使う)へ。「未接続」と出る場合は、ドングルが挿さっているか、
イヤホンがケースから出ているかを確かめてから、[困ったときは](#困ったときは)を参照してください。

削除するときは**設定 → アプリ → インストールされているアプリ → OpenInzone → アンインストール**
です。`%APPDATA%\openinzone` はそのまま残すので、選んだキー割り当ては入れ直しても失われません。

### winget でインストールする

```
winget install penguinwokrs.OpenInzone
```

> **まだ利用できません。** OpenInzone は公開の winget リポジトリにまだ登録されていないため、この
> コマンドは今のところ失敗します。それまでは上記のインストーラーをお使いください。

メンテナー向け: リリースのたびに、`packaging/winget/` のテンプレートからそのリリースのバージョン・
ダウンロード URL・SHA-256 を埋め込んだ 3 つの winget マニフェストファイル
（`penguinwokrs.OpenInzone.yaml`、`.installer.yaml`、`.locale.en-US.yaml`）が **Assets** に添付
されます。新しいバージョンを登録するには、ダウンロードしたフォルダーに対して `wingetcreate submit`
を実行するか、その 3 ファイルを `manifests/p/penguinwokrs/OpenInzone/<version>/` に置いて
[microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs) へ手動でプルリクエストを送るか
のどちらかです。どちらの方法も、そのリポジトリをフォークしてプルリクエストを送る権限を持つ
GitHub アカウントが必要で、ここまでは自動化していません。

## トレイアプリを使う

まず起動するのは `inzonetray.exe` です。通知領域にアイコンを出したまま常駐します。インストーラー
の**Windows の起動時に常駐する**タスクを使えば Windows と一緒に立ち上がりますし、zip の場合は実行
ファイルをそのまま起動します。同時に動くのは 1 つだけで、2 つめを起動しても即座に終了するので、
ホットキーは先に起動したほうが持ち続けます。

**アイコンを左クリック**すると、通知領域のあるモニターの右下にパネルが開きます。3 つのスライダー
とバッテリー残量が並びます（このページの冒頭に載せたパネルです）。

| スライダー | 何を動かすか |
|---|---|
| ヘッドホン音量 | ヘッドセット自身の音量 0–30。Windows の再生音量ではありません |
| マイクレベル | ヘッドセットに対応する Windows のキャプチャ端点 0–100 |
| ゲーム/チャットバランス | 0–100。表示は INZONE Hub と同じ -5.0 〜 +5.0 のスケール |

マイクのアイコンをクリックすると、ヘッドセット自身のマイクミュートが切り替わり、ミュート中は
アイコンに赤い斜線が入ります。スピーカーとゲーム/チャットのアイコンはボタンではなく、何のための
行かを示す表示です。ヘッドホンのミュートはコマンドライン側にあり、`inzone volume mute` です。

マイクだけが意図的に分かれていて、スライダーは Windows の端点、ミュートはヘッドセット自身の
フラグです。INZONE Hub の挙動もこれと同じで、ワイヤに乗っているのはミュートだけだからです。理由は
`docs/PROTOCOL.md` に書いてあります。[どのボリュームがどれか](#どのボリュームがどれか)も参照して
ください。

スライダーをドラッグしても HID チャンネルが溢れることはありません。書き込みは 100 ms のタイマーに
まとめられ、指を離した時点の値は必ず送られます。

スライダーの下は、左右のイヤホンとケースのバッテリーです。ケースの数値はコマンドラインが返すのと
同じスナップショットです。何時間も動かないことがある理由と、充電中かどうかが分からないことは
[バッテリー](#バッテリー)を参照してください。

パネルはフォーカスを失うと閉じます。**アイコンを右クリック**すると「設定」と「終了」のメニューが
出ます。アイコンにマウスを乗せれば、モデル名・音量・バッテリーが分かります。

## ホットキーと設定

トレイは 8 つのグローバルホットキーを持っています。どのアプリケーションからでも——フルスクリーンの
ゲーム中でも——効きます。

| コマンド | 既定のキー |
|---|---|
| 音量を上げる / 下げる | `Ctrl+Alt+Right` / `Ctrl+Alt+Left` |
| バランスをゲーム寄りに / チャット寄りに | `Ctrl+Alt+Up` / `Ctrl+Alt+Down` |
| バランスを中央に | `Ctrl+Alt+Home` |
| マイクミュート切り替え | `Ctrl+Alt+Shift+M` |
| マイクレベルを上げる / 下げる | `Ctrl+Alt+PageUp` / `Ctrl+Alt+PageDown` |

右クリックメニューの**設定**を選ぶと、8 つすべてと、それぞれに割り当たっているキーが並んだ
ウィンドウが開きます。行を選んで組み合わせを押せば割り当て、`Esc` を押せば未割り当てに戻ります。
他のアプリケーションが既に取得している組み合わせは、押した時点で「使用中」と表示されるので、
あとで押してみて無反応で気づく、ということになりません。**既定に戻す**を押せば全部の行が既定の
キーに戻り、保存するとその場でホットキーを登録しなおすので、再起動は要りません。

表の下には、チェックボックスが 2 つと、このコピーのバージョン、それに更新のボタンがあります。
**Windows の起動時に常駐する**は、その名のとおりトレイを Windows と一緒に起動します。**起動時に
更新を確認する**は、ログインのたびに 1 回だけ、新しいリリースがあるかを GitHub に問い合わせます。
あるときだけ通知するので、なければ何も出ません。既定はオフです。**更新を確認**を押すと、その場で
同じ問い合わせをして、結果をそのまま伝えます。最新である場合だけでなく、新しいリリースはあるが
インストーラーが添付されていない場合や、GitHub の応答を読み取れなかった場合も、そう表示します。
更新があるとボタンは**更新**に変わり、押すとそのリリースのインストーラーをダウンロードし、GitHub
が併せて公開している SHA-256 と照合してから実行します。トレイは自分を置き換えてもらうために終了
し、インストーラーが起動しなおします。

起動時にどれかの組み合わせを登録できなかったときは——別のアプリケーションが先に取っている場合
です——バルーンが該当するコマンド名を知らせます。残りのホットキーはそのまま動きます。

トレイはデバイスを開いたまま現在値をキャッシュするので、キーを押しっぱなしにしても 1 回の押下に
つき読み出し＋書き込みではなく書き込み 1 回で済み、連打に対する反応が鈍りません。

割り当ての保存先は `%APPDATA%\openinzone\hotkeys.json` で、コマンド ID をキーにしています。

```json
{
  "bindings": {
    "volume-up": "Ctrl+Alt+Right",
    "volume-down": "Ctrl+Alt+Left",
    "balance-game": "Ctrl+Alt+Up",
    "balance-chat": "Ctrl+Alt+Down",
    "balance-centre": "Ctrl+Alt+Home",
    "mic-mute": "Ctrl+Alt+Shift+M",
    "mic-up": "Ctrl+Alt+PageUp",
    "mic-down": "Ctrl+Alt+PageDown"
  },
  "autostart": false,
  "checkForUpdatesAtStartup": false
}
```

手で編集しても構いません。値を空文字列にすると、そのコマンドは未割り当てになります。**キー**は
修飾キー `Ctrl`、`Alt`、`Shift`、`Win` と、通常キー 1 つの組み合わせです。使えるのは英数字、
`F1`–`F24`、方向キー、`Home`、`End`、`PageUp`、`PageDown`、`Insert`、`Delete`、`Space`、`Enter`、
`Tab`、`Escape`、`Backspace`、テンキーの演算子キー、およびメディアキー `VolumeUp`、`VolumeDown`、
`VolumeMute`、`MediaNext`、`MediaPrev`、`MediaStop`、`MediaPlayPause` です。

以前のバージョンが残した設定ファイルは形式が異なりますが、トレイが読み込むときに移行するので、
アップグレードしても選んであったキーはそのまま引き継がれます。

Windows の起動時に常駐する設定は、`HKCU` の `Run` エントリです。設定ウィンドウのチェックボックス
からでも、インストーラーの任意タスクからでも書かれます。どちらも同じものを指しているので、片方で
入れてもう片方で外しても矛盾は起きません。

## 困ったときは

**パネルに「未接続」と出る、または `No INZONE dongle found.` と出る**
ドングルが挿さっていないか、本ツールが知らないプロダクト ID で認識されています。ターミナルから
`inzone devices` を実行すると、見つかったものが並びます。

```console
PS> inzone devices
VID_054C&PID_0EC2 UsagePage=0xFF04 Usage=0x0001 In=64 Out=64 "Hid Interface"
  \\?\hid#vid_054c&pid_0ec2&mi_05&col03#8&29ddaaec&0&0002#{4d1e55b2-f16f-11cf-88cb-001111000030}
```

これも空なら、ベンダー `054C`・ユーセージページ `0xFF04` のデバイスが存在するか確認してください。
探索は固定のプロダクト ID ではなく能力で照合するので、別の INZONE モデルでも見つかるはずです。

**`The headset did not answer ... within 1500 ms.` と出る**
ドングルはあるがイヤホンに届いていません。ケースに入ったまま、通信範囲外、または電源が切れていま
す。ケースから出してもう一度試してください。

**「WindowsによってPCが保護されました」が出る、exe が起動しない**
コード署名をしていないので、インストーラーには SmartScreen が出ます。**詳細情報 → 実行**を選んで
ください。zip を展開した場合は、ダウンロード時の印がファイルに残っているので、置いた場所に対して
`Unblock-File` を再帰的に実行してください。手順は[コマンドライン](#コマンドライン)にあります。

**`inzone` がコマンドとして認識されない**
PATH の手順を飛ばしたか、ターミナルがそれより前から開いています。新しいターミナルを開くか、パスを
指定して実行してください: インストーラーなら
`& "$env:LOCALAPPDATA\Programs\OpenInzone\inzone.exe" status`、zip 展開なら
`& "$env:LOCALAPPDATA\OpenInzone\inzone.exe" status` です。

**ホットキーを登録できなかった、とバルーンが出る**
別の何かがその組み合わせを先に登録しています。グラフィックスドライバやチャットアプリがよくある
原因です。**設定**で別の組み合わせを選んでください。残りのホットキーはそのまま動きます。

**`inzone mic` でミュート状態は出るがレベルが出ない**
Windows がそのヘッドセットのキャプチャ端点を公開していません。ミュートフラグはヘッドセット側にあ
るので動き続けますが、レベルは Windows 側の設定なのでその端点が必要です。

## コマンドライン

`inzone.exe` からは、トレイなしで同じ設定に 1 つずつ触れます。スクリプトやステータスバーの出発点
はこちらです。ここから先はターミナルを使います。スタートボタンを右クリック →**ターミナル**で
開きます。

### inzone.exe を用意する

インストーラーを使ったなら、トレイと同じ `%LOCALAPPDATA%\Programs\OpenInzone` に既に入っています。
[PATH を通す](#path-を通す)へ進んでください。

インストーラーを使いたくない場合は、[最新リリース](https://github.com/penguinwokrs/openinzone/releases/latest)
の `OpenInzone-<version>-win-x64.zip` に同じ 2 つのプログラム——`inzone.exe` と
`inzonetray.exe`、それに `LICENSE` と動作に必要な .NET ランタイム——が入っており、何もインストール
しません。展開して、消さない場所に置きます。

```powershell
$dir = "$env:LOCALAPPDATA\OpenInzone"
$zip = (Get-Item "$env:USERPROFILE\Downloads\OpenInzone-*-win-x64.zip").FullName
Expand-Archive $zip -DestinationPath $dir -Force
Get-ChildItem $dir -Recurse | Unblock-File
```

`Unblock-File` は、インターネットから取得したファイルに Windows が付ける印を外します。これをしな
いと初回起動時に**「WindowsによってPCが保護されました」**が出ます。どちらの配布物もコード署名は
していないので、この表示はインストーラーでも出ることがあります。その場合は**詳細情報 → 実行**を
選んでください。

zip を展開しただけでは、スタートメニューの項目も自動起動の設定も付きません。Windows の起動時に
常駐させたい場合は、トレイの**設定**ウィンドウのチェックボックスを使ってください。インストーラー
が書くのと同じレジストリエントリが書かれます。

### PATH を通す

そのフォルダ以外からも `inzone` で呼べるようにします。

```powershell
$dir = "$env:LOCALAPPDATA\Programs\OpenInzone"      # zip の場合は展開先
[Environment]::SetEnvironmentVariable(
    "Path",
    [Environment]::GetEnvironmentVariable("Path", "User") + ";$dir",
    "User")
```

反映にはターミナルを閉じて開きなおす必要があります。

この手順は任意です。省いた場合は、以下の例で `inzone` と書いてあるところを、フォルダ内で
`.\inzone.exe` と読み替えてください。

### ヘッドセットが見つかるか確認する

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

### バッテリー

`inzone battery` で両耳とケースの残量を表示します。

ケースの残量はライブな値ではありません。ケース自体に無線を持たないため、値がドングルに届くのは
イヤホンを入れた瞬間だけです。ケースだけを充電器に挿して放置しても、いくら待っても同じ数字を
報告し続けます。実測では、充電器に挿したまま 37 分間 36% のまま動かず、イヤホンを入れた瞬間に
42% へ跳びました。両耳ともケースに入っていると応答自体が返らず、`inzone battery` はその旨を
表示して終了コード 1 を返します。

**充電中かどうかは分かりません。** ヘッドセットは充電中と非充電中を区別する情報を送ってこないので、
本ツールでも INZONE Hub でも表示できません。ケースに入れていたイヤホンは、単に残量が増えた状態で
戻ってきます。

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

### コマンド一覧

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
inzone watch battery          指定したイベントの変更だけを出力
                              （battery, balance, volume, mic, sidetone）

--json                        任意のコマンドの結果を JSON オブジェクトで出力
                              （watch は 1 行 1 オブジェクト）
--raw                         battery の出力に生バイトを添える
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

## スクリプトから使う

`--json` は `battery` に限らずどのコマンドでも使えます。付けると、整列した列表示の代わりに JSON
オブジェクト 1 個が標準出力に出ます。`watch --json` は 1 行 1 オブジェクトで出すので、1 行だけで
そのまま完結したレコードになります。

### 出力例

```console
$ inzone battery --json
{"left":51,"right":71,"case":34,"detail":{"left_state":"reporting","right_state":"reporting","case_state":"reporting","case_is_snapshot":true}}
```

```console
$ inzone status --json
{"device":"INZONE Buds","serial":{"left":"3015430","right":"3015430","dongle":"3015430"},"battery":{"left":51,"right":71,"case":34,"detail":{…}},"balance":{"value":50,"notch":0},"volume":{"value":15,"max":30,"muted":false},"mic":{"muted":false,"level":100,"level_available":true},"sidetone":{"value":0}}
```

```console
$ inzone watch battery --json
{"time":"05:21:11","event":"battery","left":57,"right":78,"case":34,"detail":{…}}
{"time":"05:23:21","event":"battery","left":56,"right":78,"case":34,"detail":{…}}
```

語彙は意図的に揃えてあります。`inzone watch battery` と `jq 'select(.event=="battery")'` は、片方
はサーバー側、片方はクライアント側で同じものを選び出しています。

```console
$ inzone watch --json | jq -c 'select(.event=="battery")'
```

### バッテリーの各キーの意味

`left`・`right`・`case` はパーセンテージか、その部分が応答していなければ `null` です。イヤホンが
ケースに入っている場合や、ケースの残量が一度も中継されていない場合が該当します。

ヘッドセットモデルには右イヤホンもケースも別個には存在しないので、そのモデルではこれらのキーが
`null` ではなく**オブジェクトから丸ごと欠落**します。`null` は「その部分は存在するが今は応答して
いない」、キーが無いことは「このモデルにはそもそもその部分が無い」という意味です。

`detail.case_is_snapshot` はイヤホンでは常に `true` です。ケース自体には無線が無く、ドックした
イヤホンがその瞬間の残量を中継するだけなので、この数値はライブな値ではなく、その瞬間のスナップ
ショットです。

`detail.raw` は `--raw` を付けたときだけ現れ、生の未解析バイト列を持ちます。

### 終了コード

| コード | 意味 |
|---|---|
| 0 | 成功 |
| 1 | デバイス側の失敗 — ドングルが無い、イヤホンがケースの中、応答が無い |
| 2 | コマンド自体が誤り — 未知のコマンド、未知の watch フィルタ、数値でない値 |

この区別は、タイマーでポーリングするものが「打ち間違えた」のか「充電中で応答がない」のかを見分け、
リトライする価値があるかを判断できるようにするためのものです。

### エラーも JSON になる

テキストモードでは、これまでどおりエラーは stderr に出ます。`--json` を付けると、成功・失敗を
問わずすべて stdout に出るので、stdout を読む側は成功でも失敗でもオブジェクト 1 個だけを受け取れ
ます。

```console
$ inzone battery --json          # 両耳ともケースに入っている場合
{"error":"unreachable","message":"The earbuds did not answer. They are in the case, out of range, or off."}
```

### watch のフィルタ

`inzone watch` はフィルタ語を渡すと、その 1 種類の変更だけを出力します: `battery`、`balance`、
`volume`、`mic`、`sidetone`。上の `event` フィールドと同じ語です。

---

## 開発者向け

ここから先は、ソースからビルドして開発するための説明です。配布物を使うだけなら必要ありません。

### 必要なもの

- .NET 8 SDK
- プロトコルのテスト以外を触るなら、ドングルとイヤホン本体
- 実行には Windows。ビルド自体は Linux や WSL からもできます

`OpenInzone.Core`・`OpenInzone.Control`・CLI のターゲットは `net8.0` で、Windows には P/Invoke と
COM を通してしか触れません。トレイは `net8.0-windows` の WPF アプリケーションですが、
`EnableWindowsTargeting` を有効にしてあるので、これも Windows 以外からビルドできます。つまり
ソリューション全体が SDK の動く環境でコンパイルでき、Windows 専用なのはできあがった `.exe` だけ
です。

### Windows でビルドする

```powershell
winget install Microsoft.DotNet.SDK.8
git clone https://github.com/penguinwokrs/openinzone.git
cd openinzone

dotnet publish src\OpenInzone.Cli  -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
dotnet publish src\OpenInzone.Tray -c Release -r win-x64 --self-contained true -o publish\tray
```

`publish\` に `inzone.exe`、`publish\tray\` に `inzonetray.exe` ができます。実行する PC には何も
インストールしなくて構いません（リリースの配布物と同じものです）。トレイは単一ファイルではなく
フォルダとして publish されますが、これはインストーラーと zip が配っている形と同じです。.NET 8
ランタイムが既に入っているなら `--self-contained true` を外すとバイナリはずっと小さくなります。

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
dotnet publish src/OpenInzone.Cli  -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
dotnet publish src/OpenInzone.Tray -c Release -r win-x64 --self-contained true -o publish/tray
```

できあがった `.exe` は WSL のパスからそのまま相互運用で実行できるので、リポジトリのディレクトリで
`./publish/inzone.exe status` が通ります。ドングルには Windows 側を経由して届くので、USB の
パススルー設定などは要りません。

例外はトレイです。グローバルホットキーは Windows のセッションに対して登録され、パネルも Windows
のデスクトップウィンドウなので、WSL は動作確認の場所として向いていません。ビルドも起動もできます
が、確認は Windows から行ってください。

インストーラーも `installer/build.sh 0.1.0` でここから作れます。両方のプログラムを `dist/` へ
publish し、それを Windows 側の Inno Setup コンパイラーに渡します。コンパイラーは Windows 側に
入れておく必要があります（`winget install --id JRSoftware.InnoSetup`）。

この受け渡しは `\\wsl.localhost` 共有を通ります。この共有は、Linux 側では既に揃って見えている
ディレクトリが Windows 側からは一部しか見えない、という状態をまれに起こします。そうなるとコンパ
イルが `Error on line 48 ... No files found matching "...\dist\tray\*"` で中断することがあり
ますが、再実行すれば直ります。また、このスクリプトは完成したインストーラーのサイズも確認している
ため、コンパイラーがペイロードの一部しか見えていないまま実行された場合でも、サイズ不足のインス
トーラーが成功として素通りすることはありません。

### テスト

プロトコル層とコントロール層には単体テストがあります。実機を使わない純粋な managed コードなので、
SDK が動く環境ならどこでも、WSL でも実行できます。

```sh
dotnet test
```

期待値は `docs/PROTOCOL.md` の実機キャプチャに基づく worked example から取っています。フレーミン
グ、アドレスのニブル、リトルエンディアンのトランザクション ID、そして各チェックサムの開始位置を
固定します。最後のものはコマンドとイベントで異なり、間違いが再び混入しやすい箇所です。

デバイス探索、レポート I/O、Windows のオーディオ端点、そしてトレイのウィンドウは、実機かデスク
トップが必要なため対象外です。デバイスの状態・ホットキーのカタログ・設定は、UI を持たない
`OpenInzone.Control` に置いてあり、それがテストできる形を保っています。

### 構成

```
src/OpenInzone.Core       プロトコルとトランスポート
  Native/                 P/Invoke と COM の宣言
  Hid/                    デバイス探索とレポート I/O
  Protocol/               パケットのコーデックと要求/応答セッション
  Audio/                  ヘッドセットの Windows キャプチャ端点
  Model/                  各設定の型付きの値
src/OpenInzone.Control    デバイスの状態、ホットキーのカタログと設定。UI は持たない
src/OpenInzone.Cli        inzone.exe
src/OpenInzone.Tray       inzonetray.exe。アイコン、パネル、設定ウィンドウ
tests/OpenInzone.Core.Tests
  Protocol/               docs/PROTOCOL.md と突き合わせたパケットコーデックのテスト
  Model/                  バッテリーの値とその表示形式
  Output/                 CLI のテキスト出力と JSON 出力
  Control/                デバイスの状態、キーの解析、設定（移行を含む）
installer/                Inno Setup のスクリプトと、それをコンパイルするスクリプト
assets/                   アプリケーションアイコンと、それを生成するスクリプト
docs/PROTOCOL.md          解析したワイヤフォーマット
config/                   ホットキー設定の例
```

Visual Studio や Rider 用に、5 つのプロジェクトを `OpenInzone.sln` がまとめています。

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
