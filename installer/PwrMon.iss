; Inno Setup script for PwrMon
; Build:  1) tools\publish.ps1 -Only standalone  (see README — produces publish\standalone\PwrMon.exe)
;         2) iscc installer\PwrMon.iss
; Output: installer\Output\PwrMon-Setup.exe

; Keep AppVersion in step with <Version> in src\PwrMon\PwrMon.csproj.
#define AppName "PwrMon"
#define AppVersion "1.6.2"
#define AppExe "PwrMon.exe"
#define AppPublisher "Brett Cherry"
#define AppURL "https://github.com/brettkcherry/PwrMon"

[Setup]
AppId={{B7C61B0E-9A2D-4F63-8A0B-3D5E1C7F4A29}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppSupportURL={#AppURL}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
LicenseFile=..\LICENSE
UninstallDisplayIcon={app}\{#AppExe}
; Without this, Inno falls back to its built-in "%1 version %2" template for the Windows
; Settings > Installed apps entry — that's where "PwrMon version 1.5.0" came from. The actual
; version is still tracked (AppVersion above still populates the separate Version column).
UninstallDisplayName={#AppName}
; app offers in-place elevation when the user wants CPU/iGPU sensors
PrivilegesRequired=admin
OutputBaseFilename=PwrMon-Setup
OutputDir=Output
SetupIconFile=..\src\PwrMon\Assets\app.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Files]
; self-contained build: runs on any 64-bit Windows 10/11 with no runtime install
; (swap Source to ..\publish\portable\ for the small framework-dependent build)
Source: "..\publish\standalone\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional icons:"; Flags: unchecked

[Registry]
; Autostart belongs to the app's Settings, and only to it. Setup used to offer its own
; "start with Windows" checkbox that wrote this HKLM value — but the app can only see and
; write HKCU + its scheduled task, so the two disagreed: Settings showed autostart off while
; an invisible machine-wide entry kept launching PwrMon anyway, and turning the Settings
; toggle off couldn't clear it. One owner is worth more than the extra install-time option.
; Deleting the value here heals machines that carry it from an older install.
Root: HKLM; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; \
    ValueName: "{#AppName}"; Flags: deletevalue
; Best-effort cleanup of the app's own per-user entry at uninstall, so a removed PwrMon
; doesn't leave a Run value pointing at a deleted exe. Best-effort because Setup runs
; elevated: on a machine with several admins, UAC can hand control to a different account
; than the one that uses PwrMon, and HKCU would then resolve to that admin's hive instead.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; \
    ValueName: "{#AppName}"; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; remove the elevated autostart task if the user created one from Settings
Filename: "schtasks"; Parameters: "/Delete /TN ""PwrMon Autostart"" /F"; \
    Flags: runhidden skipifdoesntexist; RunOnceId: "DelSchedTask"

; No [UninstallDelete] entry for user data: it's handled in [Code] below, so the user is
; asked rather than having the decision made for them either way.

[Code]
// Uninstalling should be able to mean "remove all of it" — settings, battery history, logs —
// but that data is the one part of PwrMon that can't be regenerated, and someone uninstalling
// to troubleshoot or roll back a version expects to reinstall onto their history. Neither
// silent default is right, so ask.
//
// Defaults to No: the prompt appears mid-uninstall when attention is low, and the cost of
// guessing wrong is asymmetric — keeping data the user wanted gone is untidy, deleting data
// they wanted kept is unrecoverable.
//
// Note for editors: these are // comments on purpose. Inno's Pascal treats { } as a comment
// delimiter, so a braced constant like the one expanded below would close a { } comment early
// and the rest of the sentence would be compiled as code.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  // usPostUninstall: the app's files are already gone, so a Yes here removes what remains
  // rather than racing the uninstaller for the same directory.
  if CurUninstallStep <> usPostUninstall then
    Exit;

  // A silent uninstall has nobody to answer the prompt. Keeping the data is the reversible
  // choice, so unattended removal never destroys history on its own.
  if UninstallSilent then
    Exit;

  // Setup runs elevated (PrivilegesRequired=admin), so the local-appdata constant resolves to
  // whichever admin account UAC handed control to. On a single-user machine that's the right
  // person; on a shared one it may not be, and the folder simply won't exist — hence the
  // guard, and hence this being best-effort the same way the HKCU cleanup above is.
  DataDir := ExpandConstant('{localappdata}\PwrMon');
  if not DirExists(DataDir) then
    Exit;

  if MsgBox('Also delete PwrMon''s settings and battery history?' + #13#10#13#10 +
            DataDir + #13#10#13#10 +
            'Your recorded battery history cannot be recovered afterwards. ' +
            'Choose No to keep it for a future reinstall.',
            mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
    DelTree(DataDir, True, True, True);
end;
