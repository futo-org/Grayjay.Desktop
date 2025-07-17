[Setup]
AppId=Grayjay
AppName=Grayjay
AppVersion=1.0
AppVerName=Grayjay
AppPublisher=FUTO
VersionInfoCompany=FUTO
VersionInfoDescription=Grayjay Desktop Installer
VersionInfoVersion=1.0
AppCopyright=© 2025 FUTO

AppPublisherURL=https://futo.org
AppSupportURL=https://grayjay.app/support
AppUpdatesURL=https://grayjay.app/download
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=win64
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
AppMutex=GrayjayInstallerMutex
CloseApplications=yes

DefaultDirName={code:GetInstallDir}
DefaultGroupName=Grayjay
DisableProgramGroupPage=yes

LicenseFile=Metadata\LICENSE.rtf
WizardImageFile=Metadata\grayjay_background.bmp
WizardSmallImageFile=Metadata\grayjay.bmp
SetupIconFile=Metadata\grayjay.ico

Compression=lzma2
SolidCompression=yes
OutputBaseFilename=Grayjay-1.0.0-Setup-x64
OutputDir=Output
UninstallDisplayIcon={app}\Grayjay.exe

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"

[Dirs]
Name: "{app}\cef"; Flags: uninsalwaysuninstall

[Files]
Source: "Files\FUTO.Updater.Client.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "Files\UpdaterConfig.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "Files\UpdaterOSConfig.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "Files\launch"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Grayjay"; Filename: "{app}\Grayjay.exe"; WorkingDir: "{app}"; Comment: "Grayjay Desktop"

[Registry]
Root: HKLM; Subkey: "Software\FUTO\Grayjay"; ValueType: string; ValueName: "InstallLocation"; ValueData: "{app}"; Flags: uninsdeletevalue; Check: IsAdminInstallMode
Root: HKCU; Subkey: "Software\FUTO\Grayjay"; ValueType: string; ValueName: "InstallLocation"; ValueData: "{app}"; Flags: uninsdeletevalue; Check: not IsAdminInstallMode

[Run]
Filename: "{app}\FUTO.Updater.Client.exe"; Parameters: "install"; Flags: waituntilterminated

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
function GetInstallDir(Param: string): string;
var
  PrevDir: string;
  ProgFiles: string;
  LocalProg: string;
begin
  ProgFiles := ExpandConstant('{pf64}\Grayjay');
  LocalProg := ExpandConstant('{localappdata}\Programs\Grayjay');

  { Existing machine‑wide install? }
  if RegQueryStringValue(HKLM, 'Software\FUTO\Grayjay', 'InstallLocation', PrevDir) and DirExists(PrevDir) then
  begin
    Result := PrevDir;
    exit;
  end;

  { Existing per‑user install? }
  if RegQueryStringValue(HKCU, 'Software\FUTO\Grayjay', 'InstallLocation', PrevDir) and DirExists(PrevDir) then
  begin
    Result := PrevDir;
    exit;
  end;

  { Fresh install }
  if IsAdminInstallMode then
    Result := ProgFiles
  else
    Result := LocalProg;
end;