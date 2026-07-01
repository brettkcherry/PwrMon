; Inno Setup script for PowerMonitor
; Build:  1) dotnet publish (see README — produces publish\portable\PowerMonitor.exe)
;         2) iscc installer\PowerMonitor.iss
; Output: installer\Output\PowerMonitor-Setup.exe

#define AppName "PowerMonitor"
#define AppVersion "1.1.0"
#define AppExe "PowerMonitor.exe"
#define AppPublisher "Brett"
#define AppURL "https://github.com/"

[Setup]
AppId={{7A3E9C41-5B84-4F1D-9E27-PowerMon0001}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppSupportURL={#AppURL}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
UninstallDisplayIcon={app}\{#AppExe}
; app offers in-place elevation when the user wants CPU/iGPU sensors
PrivilegesRequired=admin
OutputBaseFilename=PowerMonitor-Setup
OutputDir=Output
SetupIconFile=..\src\PowerMonitor\Assets\app.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Files]
; framework-dependent single-file build (requires .NET 8 Desktop Runtime;
; swap the Source to publish\standalone\ for the zero-dependency build)
Source: "..\publish\portable\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

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
Filename: "schtasks"; Parameters: "/Delete /TN ""PowerMonitor Autostart"" /F"; \
    Flags: runhidden skipifdoesntexist; RunOnceId: "DelSchedTask"

[UninstallDelete]
; user data stays by default (history/settings survive reinstall); uncomment to purge:
; Type: filesandordirs; Name: "{localappdata}\PowerMonitor"
