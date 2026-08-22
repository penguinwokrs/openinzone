# README restructure — report

Files touched: `README.md`, `README.ja.md`. Nothing else.

## Section order

### Before (top-level `##`, both files)

| # | README.md | README.ja.md |
|---|---|---|
| 1 | What it can do | できること |
| 2 | Requirements | 動作環境 |
| 3 | Install (1. Download / 2. Install it / 3. Put it on PATH / 4. Check the headset is found) | インストール（同構成） |
| 4 | Using it (Use it from the tray / Change something / Battery / Watch changes as they happen / Settings and hotkeys) | 使い方（同構成） |
| 5 | Command reference (+ Which volume is which) | コマンド一覧（+ どのボリュームがどれか） |
| 6 | Troubleshooting | 困ったときは |
| 7 | Scripting | スクリプトから使う |
| 8 | Developer guide | 開発者向け |
| 9 | Related projects | 関連プロジェクト |
| 10 | License | ライセンス |
| 11 | Trademarks and scope | 商標と適用範囲 |

There was no table of contents in either file.

### After

| # | README.md | README.ja.md |
|---|---|---|
| 1 | Contents | 目次 |
| 2 | What it can do | できること |
| 3 | Requirements | 動作環境 |
| 4 | Install | インストール |
| 5 | Using the tray | トレイアプリを使う |
| 6 | Hotkeys and settings | ホットキーと設定 |
| 7 | Troubleshooting | 困ったときは |
| 8 | Command line | コマンドライン |
| 9 | Scripting | スクリプトから使う |
| 10 | Developer guide | 開発者向け |
| 11 | Related projects | 関連プロジェクト |
| 12 | License | ライセンス |
| 13 | Trademarks and scope | 商標と適用範囲 |

The table of contents lists exactly those top-level sections (all but the ToC heading itself), no
nested subsections.

## Images

| Image | Where | Why |
|---|---|---|
| `docs/images/banner.png` | line 1, above the `# OpenInzone` title, full width, no HTML sizing | Standard project-banner position; GitHub already scales it to the content width, so an HTML `width` would only make it smaller than the page allows. |
| `docs/images/flyout.png` | in the intro, immediately after the "there are two programs" table and the line "One left click on the tray icon opens this:" — before Requirements and before Install | The brief was "where the reader first meets the tray application, so they can see what they are installing before they install it". The intro is the first meeting, and it is above the install steps. |

Alt text is descriptive in both languages: the banner names the project and what it does; the
flyout names the three sliders and the battery row.

The image is referenced once per file rather than twice — "Using the tray" points back to it
("the panel pictured at the top of this page" / 「このページの冒頭に載せたパネルです」) instead of
repeating a 640 px screenshot a screen and a half later.

## What moved out of the install section

Install is now four short steps, all mouse-driven: download `OpenInzone-<version>-setup.exe` from
the release's Assets, double-click it, get past SmartScreen (**More info → Run anyway**, explained
as "not code-signed" rather than "something is wrong"), take the default checkboxes, and find the
icon in the notification area — including the hint that Windows hides new tray icons behind the
`^` arrow. It ends with how to uninstall.

Moved into `## Command line` (nothing deleted):

- the zip download `OpenInzone-<version>-win-x64.zip` and the `Expand-Archive` / `Unblock-File`
  PowerShell block → `### Getting inzone.exe`
- the `Unblock-File` explanation and the SmartScreen note that went with it → same subsection
- the `PATH` block and the "close and reopen the terminal" note → `### Put it on PATH`
- the `inzone status` transcript that used to be install step 4 → `### Check the headset is found`

Also moved, because they were CLI content sitting under "Using it": `Change something`, `Battery`
and `Watch changes as they happen` are now subsections of `Command line`, next to
`Command reference` and `Which volume is which` (both of which were top-level sections before).

New in that section: a note that unpacking the zip gives no Start menu entry and no autostart
task, and that the tray's own 設定 window writes the same registry entry the installer would.

## Heading-sequence comparison

Compared programmatically: both files have 39 headings (including `# OpenInzone`), and every
index has the same level in both files. Verified with a script that strips fenced code blocks and
pairs the two lists index by index — result: correspond, no level or count mismatch. Every
`](#…)` link in each file resolves to a heading that exists in that same file (0 broken anchors in
either). The Japanese anchors were checked as their own set rather than assumed to mirror the
English ones; the only non-obvious one is `### PATH を通す` → `#path-を通す` (ASCII lowercased,
space to hyphen, Japanese kept), which is what the file links to.

## Claims that were not true of the code

**Fixed.** The old text said, of the tray panel: "Clicking a row's speaker or microphone icon
toggles that mute." Only the microphone icon is a button. In `src/OpenInzone.Tray/FlyoutWindow.xaml`
the speaker (`SpeakerGeometry`) and the game/chat icons are bare `<Path>` elements with a `ToolTip`
and no handler; only `MicMuteButton` has `Click` (`FlyoutWindow.xaml.cs:40`). There is no headphone
mute anywhere in the tray — not in the panel and not in the eight hotkeys
(`OpenInzone.Control/HotkeyCommand.cs`). Both files now say the microphone icon toggles the
headset's mic mute and gets a red slash while muted, that the other icons are labels, and that the
headphone mute is `inzone volume mute` on the command line.

Everything else was checked and holds:

- per-user install, no elevation (`PrivilegesRequired=lowest`), `%LOCALAPPDATA%\Programs\OpenInzone`
  (`DefaultDirName={autopf}` under lowest privileges), Start menu entry
- autostart task ticked by default, desktop shortcut task `Flags: unchecked`
- uninstall deletes `{app}` only, leaving `%APPDATA%\openinzone`
- `[Run]` offers to launch the tray on the last page
- single instance via a named mutex (`App.xaml.cs:24`), 設定 / 終了 menu (`TrayIcon.cs:26,28`),
  tooltip carries model / volume / battery (`TrayIcon.cs:59-62`), panel hides on deactivate
- the eight default hotkeys and their ids match `HotkeyCommand.All` exactly
- 既定に戻す button, autostart checkbox, `Esc` clears, 使用中 conflict marking — all in
  `SettingsWindow.xaml`/`.cs`

Small truthful additions: the installer shows a language dialog (two languages are declared) while
its optional-task checkboxes are hard-coded Japanese — said in the English file so an English
reader is not stopped by it; the Japanese file just says to pick 日本語. The first Troubleshooting
entry now also covers the tray's own 未接続 state, since a reader who never opens a terminal will
see that rather than `No INZONE dongle found.`

## Deliberately not changed

- Console transcripts, JSON samples, file paths, key names, the hotkey JSON and the layout tree —
  verbatim, identical in both files.
- Prose that was already correct and clear was moved, not reworded: Battery, Watch, Scripting, the
  whole Developer guide, Related projects, License and Trademarks are the same text under the same
  (or newly nested) headings.
- The banner is a plain Markdown image with no HTML `width`. GitHub scales it to the content width
  already, and 2560×640 has no detail that needs holding back.
- `## Scripting` was left as a top-level section after `## Command line` rather than folded into
  it, per the requested order.
- Troubleshooting stays ahead of the command line, per the requested order, even though two of its
  entries are about `inzone.exe`; both link forward to the section that explains the terminal.
