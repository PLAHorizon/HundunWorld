; GengDi Game Center - Inno Setup Installer Script
; Usage:
;   1. Run build-installer.ps1 first (or dotnet publish manually)
;   2. Open this file with Inno Setup Compiler and compile
;   Output: dist\GengDi-Setup-<version>.exe

#define MyAppName      "GengDi"
#define MyAppVersion   "1.0.0"
#define MyAppPublisher "HundunWorld"
#define MyAppURL       "https://github.com/PLAHorizon/HundunWorld-UE"
#define MyAppExeName   "GengDi.exe"
#define MyAppId        "{{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}"

#define PayloadDir     ".\publish\GengDi"

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}

DefaultDirName={localappdata}\GengDi
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes

OutputDir=.\dist
OutputBaseFilename=GengDi-Setup-{#MyAppVersion}
SetupIconFile=Horizon.Game.GengDi.PC\Application.ico

Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes
LZMADictionarySize=65536
LZMANumBlockThreads=4

VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} Game Center Installer
VersionInfoVersion={#MyAppVersion}
VersionInfoTextVersion={#MyAppVersion}

PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

WizardStyle=modern
WizardResizable=no

UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName} Game Center

MinVersion=10.0

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "chs"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[CustomMessages]
chs.DesktopIcon=Create desktop shortcut
chs.AdditionalIcons=Additional shortcuts
en.DesktopIcon=Create a desktop shortcut
en.AdditionalIcons=Additional shortcuts
chs.WelcomeMsg=Welcome to install
en.WelcomeMsg=Welcome to install
chs.DotNetMissing=.NET 10 runtime is not installed.
en.DotNetMissing=.NET 10 runtime is not installed.
chs.DotNetDownload=Click Yes to download and install automatically (requires internet), or No to cancel.
en.DotNetDownload=Click Yes to download and install automatically (requires internet), or No to cancel.
chs.AlreadyInstalled=You already have version {#MyAppVersion} installed. Reinstall?
en.AlreadyInstalled=You already have version {#MyAppVersion} installed. Reinstall?
chs.DownloadingDotNet=Downloading .NET runtime...
en.DownloadingDotNet=Downloading .NET runtime...
chs.InstallingDotNet=Installing .NET runtime...
en.InstallingDotNet=Installing .NET runtime...
chs.DotNetDownloadFailed=.NET 10 download failed. Please check your network.
en.DotNetDownloadFailed=.NET 10 download failed. Please check your network.
chs.DotNetInstallFailed=.NET 10 installation failed (code: %d).
en.DotNetInstallFailed=.NET 10 installation failed (code: %d).
chs.LaunchApp=Launch {#MyAppName} now
en.LaunchApp=Launch {#MyAppName} now

[TASKS]
Name: "desktopicon"; Description: "{cm:DesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs replacesameversion; Excludes: "*.pdb,*.log,*.bak,*.tmp"

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{commondesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchApp}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: dirifempty; Name: "{app}"

[Code]
const
  DotNet10DownloadUrl = 'https://aka.ms/dotnet/10.0/dotnet-runtime-win-x64.exe';
  DotNet10Sha256 = '';
  DotNet10TempFile = '{tmp}\dotnet-runtime-10-win-x64.exe';

function IsDotNet10Installed(): Boolean;
var
  RuntimeDir: string;
  FindRec: TFindRec;
begin
  Result := False;
  RuntimeDir := ExpandConstant('{pf}\dotnet\shared\Microsoft.NETCore.App');
  if not DirExists(RuntimeDir) then Exit;
  if FindFirst(RuntimeDir + '\10.*', FindRec) then
  begin
    try
      repeat
        if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
        begin
          Result := True;
          Break;
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

function InitializeSetup(): Boolean;
var
  PrevVersion: String;
begin
  Result := True;
  
  if RegQueryStringValue(HKEY_CURRENT_USER,
      'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#MyAppId}_is1',
      'DisplayVersion', PrevVersion) then
  begin
    Log('Previous version detected: ' + PrevVersion);
    if PrevVersion = '{#MyAppVersion}' then
    begin
      if MsgBox(ExpandConstant('{cm:AlreadyInstalled}'), mbConfirmation, MB_YESNO) = IDNO then
      begin
        Result := False;
        Exit;
      end;
    end
    else
    begin
      Log('Upgrading: ' + PrevVersion + ' -> {#MyAppVersion}');
    end;
  end;
  
  if not IsDotNet10Installed() then
  begin
    if MsgBox(ExpandConstant('{cm:DotNetMissing}') + #13#10 + ExpandConstant('{cm:DotNetDownload}'),
      mbConfirmation, MB_YESNO) = IDNO then
      Result := False;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
  Msg: String;
begin
  if CurStep = ssInstall then
  begin
    if not IsDotNet10Installed() then
    begin
      WizardForm.StatusLabel.Caption := ExpandConstant('{cm:DownloadingDotNet}');
      try
        DownloadTemporaryFile(DotNet10DownloadUrl, ExpandConstant(DotNet10TempFile), DotNet10Sha256, '');
      except
        MsgBox(ExpandConstant('{cm:DotNetDownloadFailed}') + #13#10 + GetExceptionMessage, mbError, MB_OK);
        Abort();
      end;

      if DotNet10Sha256 = '' then
        Log('Warning: DotNet10Sha256 not configured, skipping integrity check.');

      WizardForm.StatusLabel.Caption := ExpandConstant('{cm:InstallingDotNet}');
      if not Exec(ExpandConstant(DotNet10TempFile), '/install /quiet /norestart', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
      begin
        Msg := Format(ExpandConstant('{cm:DotNetInstallFailed}'), [IntToStr(ResultCode)]);
        MsgBox(Msg, mbError, MB_OK);
        Abort();
      end;
    end;
  end;
end;
