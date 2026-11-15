; Inno Setup 6 script for OBS Turbo Mute Alarm.
; Build with installer\build.ps1 (publishes the app, builds the ICP plugin, then runs ISCC).

#define AppName "OBS Turbo Mute Alarm"
#define AppExe "ObsTurboMuteAlarm.exe"
#define AppUrl "https://github.com/turbomursu/obs-turbo-mute-alarm"
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef ArtifactsDir
  #define ArtifactsDir "..\artifacts"
#endif

[Setup]
AppId={{BD016B3F-22FA-420A-93B4-E3E00F245B98}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Mursu-soft
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
VersionInfoVersion={#AppVersion}
VersionInfoCompany=Mursu-soft
VersionInfoDescription={#AppName} Setup
; Per-user install by default (no admin needed); the user can choose "all users" in a dialog.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
UsedUserAreasWarning=no
DefaultDirName={autopf}\{#AppName}
DisableDirPage=no
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#ArtifactsDir}
OutputBaseFilename=ObsTurboMuteAlarm-Setup-{#AppVersion}
SetupIconFile=..\src\ObsTurboMuteAlarm\Resources\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
WizardStyle=modern
WizardImageFile=wizard-large-1x.bmp,wizard-large-2x.bmp
WizardSmallImageFile=wizard-small-1x.bmp,wizard-small-2x.bmp
Compression=lzma2/max
SolidCompression=yes
CloseApplications=force
RestartApplications=no

[Types]
Name: "popup"; Description: "Monitor popups only"
Name: "withicp"; Description: "Monitor popups and WinWing ViperAce ICP display"
Name: "custom"; Description: "Custom installation"; Flags: iscustom

[Components]
Name: "main"; Description: "{#AppName} (flashing popup on your monitors)"; Types: popup withicp custom; Flags: fixed
Name: "icp"; Description: "WinWing ViperAce ICP support (flashes the warning on the ICP's DED display)"; Types: withicp

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked
Name: "startup"; Description: "Start {#AppName} automatically when &Windows starts"; GroupDescription: "Startup:"; Flags: unchecked

[Files]
Source: "{#ArtifactsDir}\app\{#AppExe}"; DestDir: "{app}"; Components: main; Flags: ignoreversion
Source: "..\INSTRUCTIONS.md"; DestDir: "{app}"; DestName: "Instructions.txt"; Components: main; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Components: main; Flags: ignoreversion
Source: "{#ArtifactsDir}\icp\ObsTurboMuteAlarm.Icp.dll"; DestDir: "{app}"; Components: icp; Flags: ignoreversion
Source: "{#ArtifactsDir}\icp\HidSharp.dll"; DestDir: "{app}"; Components: icp; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; DestName: "THIRD-PARTY-NOTICES.txt"; Components: icp; Flags: ignoreversion

[InstallDelete]
; ICP support was installed before but is not selected now: remove it.
Type: files; Name: "{app}\ObsTurboMuteAlarm.Icp.dll"; Components: not icp
Type: files; Name: "{app}\HidSharp.dll"; Components: not icp
Type: files; Name: "{app}\THIRD-PARTY-NOTICES.txt"; Components: not icp

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; Same value the app's own "Start with Windows" setting uses.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "ObsTurboMuteAlarm"; ValueData: """{app}\{#AppExe}"""; Tasks: startup; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#AppExe}"; Description: "Start {#AppName} now"; Flags: nowait postinstall skipifsilent runasoriginaluser
Filename: "{app}\Instructions.txt"; Description: "Show the instructions"; Flags: nowait postinstall skipifsilent shellexec unchecked runasoriginaluser

[Code]
const
  AppMutex = 'Local\ObsTurboMuteAlarm';

{ Asks a running copy to exit cleanly (it blanks the ICP display on exit) and waits for it. }
procedure StopRunningApp();
var
  Exe: String;
  ResultCode: Integer;
begin
  Exe := ExpandConstant('{app}\{#AppExe}');
  if CheckForMutexes(AppMutex) and FileExists(Exe) then
    Exec(Exe, '--exit', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopRunningApp();
  Result := '';
end;

function InitializeUninstall(): Boolean;
begin
  StopRunningApp();
  Result := True;
  if CheckForMutexes(AppMutex) then
  begin
    MsgBox('{#AppName} is still running. Exit it (right-click the tray icon, then Exit) and run the uninstaller again.',
      mbError, MB_OK);
    Result := False;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    { Also set by the app's own "Start with Windows" option. }
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'ObsTurboMuteAlarm');

  if (CurUninstallStep = usPostUninstall) and not UninstallSilent then
    if MsgBox('Also delete your {#AppName} settings and log?', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
    begin
      DelTree(ExpandConstant('{userappdata}\ObsTurboMuteAlarm'), True, True, True);
      DelTree(ExpandConstant('{localappdata}\ObsTurboMuteAlarm'), True, True, True);
    end;
end;
