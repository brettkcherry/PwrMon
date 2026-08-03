; Inno Setup script for PwrMon
; Build:  1) dotnet publish (see README — produces publish\portable\PwrMon.exe)
;         2) iscc installer\PwrMon.iss
; Output: installer\Output\PwrMon-Setup.exe

; Keep AppVersion in step with <Version> in src\PwrMon\PwrMon.csproj.
#define AppName "PwrMon"
#define AppVersion "1.3.1"
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
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional icons:"
Name: "autostart"; Description: "Start {#AppName} when Windows starts"; GroupDescription: "Startup:"; Flags: unchecked

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; \
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
