; Chango installer (Inno Setup 6). Per-user install: no administrator rights required.
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
AppId={{A321D14F-0DA3-4859-B664-7F0638D93666}
AppName=Chango
AppVersion={#AppVersion}
AppVerName=Chango {#AppVersion}
AppPublisher=Ramesh Tiwari
AppCopyright=© 2026 Ramesh Tiwari · MIT License
VersionInfoVersion={#NumericVersion}
VersionInfoProductName=Chango
VersionInfoDescription=Chango Setup
DefaultDirName={autopf}\Chango
DefaultGroupName=Chango
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir={#OutputDir}
OutputBaseFilename=Chango-{#AppVersion}-win-x64-setup
SetupIconFile=..\..\src\Chango.UI\Assets\chango.ico
UninstallDisplayIcon={app}\Chango.exe
UninstallDisplayName=Chango
LicenseFile=..\..\LICENSE
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; A running Chango is closed through Windows' Restart Manager (and restarted after silent updates, see [Run]).
; No AppMutex: in a silent update its "Chango is running" check can only abort, never wait, so it made updates fail.
CloseApplications=yes
; Only ever offer to close Chango itself: File Explorer keeps the right-click DLL loaded, and must never be closed.
CloseApplicationsFilter=Chango.exe
; Ends with SHChangeNotify(SHCNE_ASSOCCHANGED) so Explorer and Start drop cached icons. Without it, an upgrade
; that changes the app icon keeps showing the old one (the cache is keyed by the unchanged exe path).
ChangesAssociations=yes

[Tasks]
Name: "startup"; Description: "Start Chango when I sign in to Windows"; GroupDescription: "Options:"
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Options:"; Flags: unchecked

[Files]
; The right-click menu entry ships as ChangoShell-<version>.dll: File Explorer keeps the DLL it has loaded open, and
; a per-user install can't replace an open file, so every version gets a new name. Chango points the menu at its
; own and deletes the old ones once Explorer has let go of them.
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[UninstallDelete]
; Right-click DLLs from earlier versions (any File Explorer still has open stay until it lets go).
Type: files; Name: "{app}\ChangoShell*.dll"

[Icons]
Name: "{autoprograms}\Chango"; Filename: "{app}\Chango.exe"; Comment: "Convert, edit and manage files without opening an app"
Name: "{autodesktop}\Chango"; Filename: "{app}\Chango.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Chango"; \
  ValueData: """{app}\Chango.exe"" --background"; Tasks: startup; Flags: uninsdeletevalue

[Run]
Filename: "{app}\Chango.exe"; Description: "Start Chango now"; Flags: nowait postinstall skipifsilent
; In-app updates run Setup silently with /RELAUNCH=1: start the new version once its files are in place.
Filename: "{app}\Chango.exe"; Parameters: "--updated"; Flags: nowait; Check: IsUpdateRelaunch

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/im Chango.exe /f"; Flags: runhidden; RunOnceId: "StopChango"

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
  again, so Chango never just disappears. It will offer the update again later. }
procedure DeinitializeSetup();
var
  Exe, Dir: String;
  Code: Integer;
begin
  if not IsUpdateRelaunch or Installed then Exit;
  Exe := '';
  try
    Exe := ExpandConstant('{app}\Chango.exe');
  except
    Exe := '';
  end;
  if ((Exe = '') or not FileExists(Exe)) and
     RegQueryStringValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{A321D14F-0DA3-4859-B664-7F0638D93666}_is1', 'InstallLocation', Dir) then
    Exe := AddBackslash(Dir) + 'Chango.exe';
  if (Exe <> '') and FileExists(Exe) then
    Exec(Exe, '--background', '', SW_SHOWNORMAL, ewNoWait, Code);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    { The right-click menu entry Chango registers for the current user (see ContextMenuRegistration.cs). }
    RegDeleteKeyIncludingSubkeys(HKEY_CURRENT_USER, 'Software\Classes\AllFilesystemObjects\shell\Chango');
    RegDeleteKeyIncludingSubkeys(HKEY_CURRENT_USER, 'Software\Classes\CLSID\{29BADCB7-5BF2-4D63-84F8-67D64C5194F5}');
    RegDeleteKeyIncludingSubkeys(HKEY_CURRENT_USER, 'Software\Chango');
  end;
  if CurUninstallStep = usPostUninstall then
  begin
    { The app can enable "launch at sign-in" itself, so always remove the value. }
    RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', 'Chango');
    { Logs are machine-local and only useful to the installed copy. }
    DelTree(ExpandConstant('{localappdata}\Chango'), True, True, True);
    { Settings are kept unless the user asks (silent uninstalls keep them). }
    if (not UninstallSilent) and
       (MsgBox('Also remove your Chango settings?', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES) then
      DelTree(ExpandConstant('{userappdata}\Chango'), True, True, True);
  end;
end;
