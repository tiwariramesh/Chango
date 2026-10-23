; OrbitDrop installer (Inno Setup 6). Per-user install: no administrator rights required.
; Built by build/package.ps1, which passes AppVersion, NumericVersion, SourceDir and OutputDir.

#ifndef AppVersion
  #define AppVersion "0.3.4-alpha"
#endif
#ifndef NumericVersion
  #define NumericVersion "0.3.4.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\..\artifacts\publish\win-x64"
#endif
#ifndef OutputDir
  #define OutputDir "..\..\artifacts"
#endif

[Setup]
AppId={{8F3C2E5A-7B1D-4C9E-A6F2-0D4B9E7C1A35}
AppName=OrbitDrop
AppVersion={#AppVersion}
AppVerName=OrbitDrop {#AppVersion}
AppPublisher=OrbitDrop contributors
AppCopyright=© 2026 Mohammed Essam and OrbitDrop contributors · MIT License
VersionInfoVersion={#NumericVersion}
VersionInfoProductName=OrbitDrop
VersionInfoDescription=OrbitDrop Setup
DefaultDirName={autopf}\OrbitDrop
DefaultGroupName=OrbitDrop
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir={#OutputDir}
OutputBaseFilename=OrbitDrop-{#AppVersion}-win-x64-setup
SetupIconFile=..\..\src\OrbitDrop.UI\Assets\orbitdrop.ico
UninstallDisplayIcon={app}\OrbitDrop.exe
UninstallDisplayName=OrbitDrop
LicenseFile=..\..\LICENSE
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; A running OrbitDrop is closed through Windows' Restart Manager (and restarted after silent updates, see [Run]).
; No AppMutex: in a silent update its "OrbitDrop is running" check can only abort, never wait, so it made updates fail.
CloseApplications=yes
; Only ever offer to close OrbitDrop itself: File Explorer keeps the right-click DLL loaded, and must never be closed.
CloseApplicationsFilter=OrbitDrop.exe
; Ends with SHChangeNotify(SHCNE_ASSOCCHANGED) so Explorer and Start drop cached icons. Without it, an upgrade
; that changes the app icon keeps showing the old one (the cache is keyed by the unchanged exe path).
ChangesAssociations=yes

[Tasks]
Name: "startup"; Description: "Start OrbitDrop when I sign in to Windows"; GroupDescription: "Options:"
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Options:"; Flags: unchecked

[Files]
; The right-click menu entry ships as OrbitDropShell-<version>.dll: File Explorer keeps the DLL it has loaded open, and
; a per-user install can't replace an open file, so every version gets a new name. OrbitDrop points the menu at its
; own and deletes the old ones once Explorer has let go of them.
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[UninstallDelete]
; Right-click DLLs from earlier versions (any File Explorer still has open stay until it lets go).
Type: files; Name: "{app}\OrbitDropShell*.dll"

[Icons]
Name: "{autoprograms}\OrbitDrop"; Filename: "{app}\OrbitDrop.exe"; Comment: "Convert, edit and manage files without opening an app"
Name: "{autodesktop}\OrbitDrop"; Filename: "{app}\OrbitDrop.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "OrbitDrop"; \
  ValueData: """{app}\OrbitDrop.exe"" --background"; Tasks: startup; Flags: uninsdeletevalue

[Run]
Filename: "{app}\OrbitDrop.exe"; Description: "Start OrbitDrop now"; Flags: nowait postinstall skipifsilent
; In-app updates run Setup silently with /RELAUNCH=1: start the new version once its files are in place.
Filename: "{app}\OrbitDrop.exe"; Parameters: "--updated"; Flags: nowait; Check: IsUpdateRelaunch

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/im OrbitDrop.exe /f"; Flags: runhidden; RunOnceId: "StopOrbitDrop"

[Code]
var
  Installed: Boolean;

function IsUpdateRelaunch: Boolean;
begin
  Result := WizardSilent and (ExpandConstant('{param:RELAUNCH|0}') = '1');
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then Installed := True;
end;

{ An in-app update that didn't go through (Setup rolled back, nothing changed): start the copy that was running
  again, so OrbitDrop never just disappears. It will offer the update again later. }
procedure DeinitializeSetup();
var
  Exe, Dir: String;
  Code: Integer;
begin
  if not IsUpdateRelaunch or Installed then Exit;
  Exe := '';
  try
    Exe := ExpandConstant('{app}\OrbitDrop.exe');
  except
    Exe := '';
  end;
  if ((Exe = '') or not FileExists(Exe)) and
     RegQueryStringValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{8F3C2E5A-7B1D-4C9E-A6F2-0D4B9E7C1A35}_is1', 'InstallLocation', Dir) then
    Exe := AddBackslash(Dir) + 'OrbitDrop.exe';
  if (Exe <> '') and FileExists(Exe) then
    Exec(Exe, '--background', '', SW_SHOWNORMAL, ewNoWait, Code);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    { The right-click menu entry OrbitDrop registers for the current user (see ContextMenuRegistration.cs). }
    RegDeleteKeyIncludingSubkeys(HKEY_CURRENT_USER, 'Software\Classes\AllFilesystemObjects\shell\OrbitDrop');
    RegDeleteKeyIncludingSubkeys(HKEY_CURRENT_USER, 'Software\Classes\CLSID\{EF221BF9-D0CA-4698-95F2-5452942E7A5E}');
    RegDeleteKeyIncludingSubkeys(HKEY_CURRENT_USER, 'Software\OrbitDrop');
  end;
  if CurUninstallStep = usPostUninstall then
  begin
    { The app can enable "launch at sign-in" itself, so always remove the value. }
    RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', 'OrbitDrop');
    { Logs are machine-local and only useful to the installed copy. }
    DelTree(ExpandConstant('{localappdata}\OrbitDrop'), True, True, True);
    { Settings are kept unless the user asks (silent uninstalls keep them). }
    if (not UninstallSilent) and
       (MsgBox('Also remove your OrbitDrop settings?', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES) then
      DelTree(ExpandConstant('{userappdata}\OrbitDrop'), True, True, True);
  end;
end;
