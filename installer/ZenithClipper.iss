; ZenithClipper installer (Inno Setup 6). Built by .github/workflows/release.yml:
;   ISCC.exe /DAppVersion=1.0.0 /DSourceRoot=<package root containing windows\ and editor\> /DOutputDir=<dist> installer\ZenithClipper.iss
; Per-user install (no admin prompt). Settings and clips live in %LOCALAPPDATA%\ZenithClipper and survive updates.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceRoot
  #define SourceRoot "..\dist\ZenithClipper"
#endif
#ifndef OutputDir
  #define OutputDir "..\dist"
#endif

[Setup]
AppId={{6F5C2E7A-3B1D-4C8E-9A57-2E9D1B0C7F41}
AppName=ZenithClipper
AppVersion={#AppVersion}
AppVerName=ZenithClipper {#AppVersion}
AppPublisher=glitchthekillah
AppPublisherURL=https://github.com/glitchthekillah/ZenithClipper
AppSupportURL=https://github.com/glitchthekillah/ZenithClipper/issues
DefaultDirName={localappdata}\Programs\ZenithClipper
DefaultGroupName=ZenithClipper
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir={#OutputDir}
OutputBaseFilename=ZenithClipper_{#AppVersion}_x64-setup
SetupIconFile=..\assets\zenithclipper.ico
UninstallDisplayIcon={app}\windows\ZenithClipper.Windows.exe
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=force
RestartApplications=no

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"
Name: "startup"; Description: "Start the recorder when I sign in"; GroupDescription: "Startup:"

[Files]
; portable.flag would keep data beside the app; installed copies use %LOCALAPPDATA% instead.
Source: "{#SourceRoot}\windows\*"; DestDir: "{app}\windows"; Excludes: "portable.flag,*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#SourceRoot}\editor\*"; DestDir: "{app}\editor"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#SourceRoot}\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceRoot}\THIRD_PARTY.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\ZenithClipper"; Filename: "{app}\windows\ZenithClipper.Windows.exe"; Parameters: "panel"; WorkingDir: "{app}\windows"
Name: "{autoprograms}\ZenithClipper Editor"; Filename: "{app}\windows\ZenithClipper.Windows.exe"; Parameters: "editor"; WorkingDir: "{app}\windows"
Name: "{autodesktop}\ZenithClipper"; Filename: "{app}\windows\ZenithClipper.Windows.exe"; Parameters: "panel"; WorkingDir: "{app}\windows"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "ZenithClipper"; ValueData: """{app}\windows\ZenithClipper.Windows.exe"" --background"; Tasks: startup; Flags: uninsdeletevalue

[Run]
Filename: "{app}\windows\ZenithClipper.Windows.exe"; Parameters: "panel"; WorkingDir: "{app}\windows"; Description: "Open ZenithClipper"; Flags: nowait postinstall skipifsilent
; In-app updates run this installer with /SILENT /relaunch=1: reopen the panel (which restarts recording) afterwards.
Filename: "{app}\windows\ZenithClipper.Windows.exe"; Parameters: "panel"; WorkingDir: "{app}\windows"; Flags: nowait; Check: Relaunch
; Daily update check: a few seconds of work, shows a notification only when a new version is out.
Filename: "{sys}\schtasks.exe"; Parameters: "/Create /F /SC DAILY /ST 12:00 /TN ""ZenithClipper update check"" /TR ""\""{app}\windows\ZenithClipper.Windows.exe\"" --update-check"""; Flags: runhidden

[UninstallRun]
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /F /TN ""ZenithClipper update check"""; Flags: runhidden; RunOnceId: "RemoveUpdateTask"
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM ZenithClipper.Recorder.exe"; Flags: runhidden; RunOnceId: "StopRecorder"
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM zenithclipper.exe"; Flags: runhidden; RunOnceId: "StopEngine"
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM ZenithClipper.Windows.exe"; Flags: runhidden; RunOnceId: "StopPanel"
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM ZenithClipper.Editor.exe"; Flags: runhidden; RunOnceId: "StopEditor"

[Code]
function Relaunch: Boolean;
begin
  Result := WizardSilent and (ExpandConstant('{param:relaunch|0}') = '1');
end;
