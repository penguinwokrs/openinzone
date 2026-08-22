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
PrivilegesRequired=admin

[Languages]
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "autostart"; Description: "Windows の起動時に常駐する"; GroupDescription: "追加のタスク:"
Name: "desktopicon"; Description: "デスクトップにショートカットを作成する"; GroupDescription: "追加のタスク:"; Flags: unchecked

[Files]
; Published self-contained, so there is no runtime to install: everything it needs is here.
Source: "..\dist\tray\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs
Source: "..\dist\cli\inzone.exe"; DestDir: "{app}"; Flags: ignoreversion

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

[UninstallDelete]
; %APPDATA%\openinzone is left alone so settings survive a reinstall.
Type: filesandordirs; Name: "{app}"

[Code]
{ The tray owns the hotkey registrations, so it has to be gone before files are replaced. }
function InitializeSetup(): Boolean;
var
  ResultCode: Integer;
begin
  { If the tray is not running, taskkill returns non-zero; ignore it. }
  Exec('taskkill.exe', '/IM inzonetray.exe /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := True;
end;
