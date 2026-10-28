; iProfiler Desktop installer (Inno Setup 6). Build with installer\build-installer.ps1, which
; publishes the self-contained app first and passes its folder in as PublishDir.

#define AppName "iProfiler Desktop"
#define AppExe "IProfiler.App.exe"
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish\win-x64"
#endif
; Version comes from the published assembly (Directory.Build.props <Version>).
#define AppVersion GetVersionNumbersString(AddBackslash(PublishDir) + AppExe)

[Setup]
AppId={{6E0B8C2E-5B7A-4F51-9C1D-3A7E2B9F4D10}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=iProfiler
VersionInfoVersion={#AppVersion}
; Per-machine install to Program Files (x64 only, per D2).
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
DefaultDirName={autopf}\{#AppName}
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
OutputDir=Output
OutputBaseFilename=iProfilerDesktop-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; Unsigned until the code-signing certificate arrives (see README).

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
; No WorkingDir on purpose: the app must not depend on it (AppPaths, CLAUDE.md rule 6).
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"

[Run]
Filename: "{app}\{#AppExe}"; Description: "Start {#AppName}"; Flags: nowait postinstall skipifsilent runasoriginaluser

[Code]
// User data (%LOCALAPPDATA%\iProfiler: scans, database, logs) is kept on uninstall unless the user
// says otherwise. Interactive uninstall asks (default: keep); silent uninstall keeps it unless
// /REMOVEDATA=1 is passed. Only the uninstalling user's data folder is touched.
function ShouldRemoveUserData(): Boolean;
begin
  if UninstallSilent then
    Result := ExpandConstant('{param:REMOVEDATA|0}') = '1'
  else
    Result := MsgBox('Also remove your {#AppName} data (scans, database and logs)?' + #13#10 + #13#10 +
      ExpandConstant('{localappdata}\iProfiler') + #13#10 + #13#10 +
      'Choose No to keep it for a later reinstall.', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    DataDir := ExpandConstant('{localappdata}\iProfiler');
    if DirExists(DataDir) and ShouldRemoveUserData() then
    begin
      if DelTree(DataDir, True, True, True) then
        Log('Removed user data: ' + DataDir)
      else
        Log('Could not fully remove user data: ' + DataDir);
    end
    else
      Log('Kept user data: ' + DataDir);
  end;
end;
