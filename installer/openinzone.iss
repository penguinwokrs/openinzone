; SPDX-License-Identifier: GPL-3.0-only
; Copyright (C) 2026 penguinwokrs

#define AppName "OpenInzone"
#define AppVersion GetEnv("OPENINZONE_VERSION")
#if AppVersion == ""
  #define AppVersion "0.0.0"
#endif

[Setup]
AppId={{8E1C6B4A-3F2D-4A77-9C55-1B7E9D0A6F31}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=penguinwokrs
AppSupportURL=https://github.com/penguinwokrs/openinzone
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
OutputDir=..\dist
OutputBaseFilename=OpenInzone-{#AppVersion}-setup
SetupIconFile=..\assets\openinzone.ico
UninstallDisplayIcon={app}\inzonetray.exe
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; The HID and audio paths this drives need Windows 10 1809 or later.
MinVersion=10.0.17763
LicenseFile=..\LICENSE
; This installs per-user, not machine-wide, because the [Registry] autostart entry is written to
; HKCU. If PrivilegesRequired were admin, a standard user installing via an administrator's
; credentials would run the installer as that administrator, making HKCU the administrator's
; hive: the autostart entry would land on an account nobody logs into with the headset, and the
; person who actually installed the tray would get no autostart at all. Keep this at "lowest" so
; the installer never elevates and HKCU always belongs to the person running it.
PrivilegesRequired=lowest

; Setup holds this while it runs, and the daemon launcher refuses to start one while it exists.
; Without it, killing the daemon is not enough: any client still running - a Stream Deck plugin,
; most likely - notices the channel has gone and starts a new daemon within seconds, which is then
; holding its own executable open when setup reaches the file it is replacing. Observed exactly
; that way: an upgrade that removed the tray and the CLI and then failed on inzoned.exe.
SetupMutex=OpenInzone.Setup

[Languages]
; English first on purpose: Inno falls back to the first entry when the operating system's
; language matches none of them, and "anything we do not have becomes English" is the rule this
; installer is meant to follow. With Japanese first, a French machine got a Japanese wizard.
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"
Name: "chinesesimplified"; MessagesFile: "lang\ChineseSimplified.isl"

[CustomMessages]
english.AutostartTask=Run when Windows starts
english.DesktopIconTask=Create a desktop shortcut
english.AdditionalTasks=Additional tasks:
japanese.AutostartTask=Windows の起動時に常駐する
japanese.DesktopIconTask=デスクトップにショートカットを作成する
japanese.AdditionalTasks=追加のタスク:
chinesesimplified.AutostartTask=开机时随 Windows 启动
chinesesimplified.DesktopIconTask=创建桌面快捷方式
chinesesimplified.AdditionalTasks=附加任务：

[Tasks]
Name: "autostart"; Description: "{cm:AutostartTask}"; GroupDescription: "{cm:AdditionalTasks}"
Name: "desktopicon"; Description: "{cm:DesktopIconTask}"; GroupDescription: "{cm:AdditionalTasks}"; Flags: unchecked

[Files]
; Published self-contained, so there is no runtime to install: everything it needs is here.
Source: "..\dist\tray\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs
Source: "..\dist\cli\inzone.exe"; DestDir: "{app}"; Flags: ignoreversion
; GPL-3.0 requires a copy of the licence to travel with the binaries, not just be shown by the
; wizard during install (that's what LicenseFile above is for), so it is installed alongside them.
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\inzonetray.exe"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\inzonetray.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; \
  ValueName: "OpenInzone"; ValueData: """{app}\inzonetray.exe"""; \
  Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\inzonetray.exe"; Description: "{cm:LaunchProgram,{#AppName}}"; \
  Flags: nowait postinstall skipifsilent
; skipifsilent above is right for an unattended install nobody is watching - winget running this in
; the background must not pop a GUI open unasked. But the tray's own self-update also installs with
; /SILENT, and that is the one case that most needs the application to come back: nothing else will
; start it. UpdateInstaller.Run passes /RELAUNCHAFTERUPDATE alongside /SILENT /NOCANCEL to say so,
; and RelaunchRequested below tests for it, so this entry launches only for that caller and stays
; silent for everyone else's silent install.
Filename: "{app}\inzonetray.exe"; Flags: nowait; Check: RelaunchRequested

[UninstallDelete]
; %APPDATA%\openinzone is left alone so settings survive a reinstall.
Type: filesandordirs; Name: "{app}"

[Code]
{ Backs the second [Run] entry: true only when the command line carries /RELAUNCHAFTERUPDATE, which
  UpdateInstaller.Run in the tray adds specifically so its own /SILENT install gets the tray back
  afterwards. A plain /SILENT install - winget, or anyone else scripting this - leaves the switch
  off and this returns false, so [Run]'s skipifsilent entry and this one agree: no GUI starts
  itself in the background uninvited. There is no built-in CmdLineParamExists in Inno's Pascal
  Scripting, so this walks ParamStr/ParamCount itself - the same pair /SILENT and /NOCANCEL above
  are parsed from. }
function RelaunchRequested(): Boolean;
var
  I: Integer;
begin
  Result := False;
  for I := 1 to ParamCount do
  begin
    if CompareText(ParamStr(I), '/RELAUNCHAFTERUPDATE') = 0 then
    begin
      Result := True;
      Exit;
    end;
  end;
end;

{ The tray owns the hotkey registrations, so it has to be gone before files are replaced. inzone.exe
  ships into the same directory: it is a short-lived command-line tool, so the odds of it still
  running are low, but if it is, it would lock its own file exactly like the tray would, so it gets
  the same treatment. }
function InitializeSetup(): Boolean;
var
  ResultCode: Integer;
begin
  { If a process is not running, taskkill returns non-zero; ignore it. }
  Exec('taskkill.exe', '/IM inzonetray.exe /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec('taskkill.exe', '/IM inzone.exe /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  { The daemon outlives the tray by design - it stops half a minute after the last client - so an
    upgrade started right after closing the tray still finds it holding its own executable. }
  Exec('taskkill.exe', '/IM inzoned.exe /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := True;
end;

{ Runs immediately before any file is written, which is the last moment anything can be holding one
  open. InitializeSetup already cleared the field, but a wizard the user left sitting there gives a
  client all the time it needs to have brought something back - and SetupMutex only stops the
  daemon, not a tray someone started by hand in the meantime. }
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Exec('taskkill.exe', '/IM inzonetray.exe /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec('taskkill.exe', '/IM inzone.exe /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec('taskkill.exe', '/IM inzoned.exe /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  { Killing returns before the handles are all closed. A moment here costs nothing and is the
    difference between replacing a file and failing on it. }
  Sleep(700);
  Result := '';
end;

{ True when Dir holds files that mark it as a previous OpenInzone install: our own executable, or
  an Inno-generated uninstaller. Anything else -- an empty directory, or a path the user repointed
  the install to that happens to hold unrelated files -- must not match, because ClearOldInstall
  below deletes on the strength of this check. }
function DirLooksLikePreviousInstall(const Dir: String): Boolean;
var
  FindRec: TFindRec;
begin
  Result := FileExists(Dir + '\inzonetray.exe');
  if not Result and FindFirst(Dir + '\unins*.exe', FindRec) then
  begin
    Result := True;
    FindClose(FindRec);
  end;
end;

{ Removes everything inside Dir, but not Dir itself, so a directory the user chose in the wizard
  keeps whatever permissions were set on it. }
procedure ClearOldInstall(const Dir: String);
var
  FindRec: TFindRec;
  FullPath: String;
begin
  if FindFirst(Dir + '\*', FindRec) then
  begin
    try
      repeat
        if (FindRec.Name <> '.') and (FindRec.Name <> '..') then
        begin
          FullPath := Dir + '\' + FindRec.Name;
          if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
            DelTree(FullPath, True, True, True)
          else
            DeleteFile(FullPath);
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

{ Inno replaces a file by renaming the old copy aside, then moving the new one into place; a file
  left behind by an earlier, incomplete uninstall sits in the way of that rename and it fails with
  error 183 (ERROR_ALREADY_EXISTS) -- exactly what happened when 0.1.0 was uninstalled with the
  tray still running, its files survived, and every install after it piled a new [unins0xx.*] pair
  on top instead of replacing the old one. ssInstall fires after the wizard but before Inno writes
  any payload, and Inno does not create its own uninstaller until the very end of installation, so
  clearing the directory here wipes stale files -- including old unins*.* -- without touching the
  uninstaller for the install now in progress. This is safe only because the payload is
  self-contained: the app directory holds nothing but what we put there, so once it is recognisable
  as a previous OpenInzone install, we own everything in it. }
{ What the wizard settled on, in the tags UiLanguage.Resolve understands. The application never
  asks Windows what language it is in - it reads this - so an installation is the only thing that
  ever decides, and the zip, which has no such file, stays English. }
function LanguageTag(): String;
begin
  if CompareText(ActiveLanguage(), 'japanese') = 0 then
    Result := 'ja'
  else if CompareText(ActiveLanguage(), 'chinesesimplified') = 0 then
    Result := 'zh-Hans'
  else
    Result := 'en';
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  AppDir: String;
begin
  if CurStep = ssInstall then
  begin
    AppDir := ExpandConstant('{app}');
    if DirExists(AppDir) and DirLooksLikePreviousInstall(AppDir) then
      ClearOldInstall(AppDir);
  end;
  if CurStep = ssPostInstall then
    SaveStringToFile(ExpandConstant('{app}\default-language'), LanguageTag(), False);
end;

{ Uninstall is when the tray is most likely to be running: autostart is a checked task and setup
  offers to launch it. A running executable cannot be deleted, so [UninstallDelete] would leave
  the directory behind and the tray would keep holding the hotkeys. }
function InitializeUninstall(): Boolean;
var
  ResultCode: Integer;
begin
  { If the tray is not running, taskkill returns non-zero; ignore it. }
  Exec('taskkill.exe', '/IM inzonetray.exe /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec('taskkill.exe', '/IM inzoned.exe /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := True;
end;
