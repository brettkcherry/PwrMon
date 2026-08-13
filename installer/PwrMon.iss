; Inno Setup script for PwrMon
; Build:  1) tools\publish.ps1 -Only standalone  (see README — produces publish\standalone\PwrMon.exe)
;         2) iscc installer\PwrMon.iss
; Output: installer\Output\PwrMon-Setup.exe

; Keep AppVersion in step with <Version> in src\PwrMon\PwrMon.csproj.
#define AppName "PwrMon"
#define AppVersion "1.6.0"
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
Name: "autostart"; Description: "Start {#AppName} when Windows starts"; GroupDescription: "Startup:"; Flags: unchecked

[Registry]
; HKLM, not HKCU. PrivilegesRequired=admin means Setup itself always runs elevated, and
; "current user" during an elevated process is not reliably the person who launched it — on
; a machine with more than one admin account, a UAC prompt can hand control to a *different*
; admin, and an HKCU write here would land in that account's hive instead of the installing
; user's. The install would look successful and the checkbox would silently do nothing for
; whoever actually uses the machine. HKLM's Run key has no such ambiguity: it's one location,
; already reachable because admin is required either way, and it starts PwrMon for whichever
; account logs in — which is also the more standard behavior for a machine-wide installer.
Root: HKLM; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; \
    ValueName: "{#AppName}"; ValueData: """{app}\{#AppExe}"" --minimized"; \
    Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; remove the elevated autostart task if the user created one from Settings
Filename: "schtasks"; Parameters: "/Delete /TN ""PwrMon Autostart"" /F"; \
    Flags: runhidden skipifdoesntexist; RunOnceId: "DelSchedTask"

[UninstallDelete]
; user data stays by default (history/settings survive reinstall); uncomment to purge:
; Type: filesandordirs; Name: "{localappdata}\PwrMon"
