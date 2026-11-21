; Inno Setup script for Asus Fan Control.
; Build with installer\build-installer.ps1, which builds the app and stages the files
; into installer\stage first. Requires Inno Setup 6.

#define StageDir "stage"
#define AppExe "AsusFanControlGUI.exe"
#define AppVersion GetVersionNumbersString(AddBackslash(StageDir) + AppExe)
#define AppVersionShort Copy(AppVersion, 1, RPos(".", AppVersion) - 1)

[Setup]
AppId={{6F1B3C1E-8B4D-4E7A-9A55-2C1F7D3E9B42}
AppName=Asus Fan Control
AppVersion={#AppVersionShort}
AppVerName=Asus Fan Control {#AppVersionShort}
AppPublisher=shahnshahmalik
AppPublisherURL=https://github.com/shahnshahmalik/AsusFanControl
AppSupportURL=https://github.com/shahnshahmalik/AsusFanControl/issues
AppUpdatesURL=https://github.com/shahnshahmalik/AsusFanControl/releases
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\Asus Fan Control
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
LicenseFile=..\LICENSE
SetupIconFile=..\AsusFanControlGUI\propeller.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName=Asus Fan Control
WizardStyle=modern
OutputDir=Output
OutputBaseFilename=AsusFanControl-Setup-v{#AppVersionShort}
Compression=lzma2
SolidCompression=yes
; The app is closed through its own --exit signal (see PrepareToInstall).
CloseApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "startup"; Description: "Start Asus Fan Control when I sign in (in the notification area)"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; Flags: unchecked

[Files]
Source: "{#StageDir}\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#StageDir}\AsusFanControlGUI.exe.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#StageDir}\AsusFanControl.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#StageDir}\AsusFanControl.exe.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#StageDir}\AsusWinIO64.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#StageDir}\PsExec.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#StageDir}\run.bat"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#StageDir}\README.txt"; DestDir: "{app}"; Flags: ignoreversion isreadme

[Icons]
; --as-system: the app restarts itself as SYSTEM through PsExec with no console
; window, like run.bat (some laptops only allow fan control from SYSTEM).
Name: "{autoprograms}\Asus Fan Control"; Filename: "{app}\{#AppExe}"; Parameters: "--as-system"; WorkingDir: "{app}"
Name: "{autodesktop}\Asus Fan Control"; Filename: "{app}\{#AppExe}"; Parameters: "--as-system"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Parameters: "--startup on"; StatusMsg: "Setting up Start with Windows..."; Flags: runhidden waituntilterminated; Tasks: startup
Filename: "{app}\{#AppExe}"; Parameters: "--startup off"; Flags: runhidden waituntilterminated; Tasks: not startup
Filename: "{app}\{#AppExe}"; Parameters: "--as-system"; WorkingDir: "{app}"; Description: "Start Asus Fan Control now"; Flags: postinstall nowait skipifsilent

[UninstallRun]
Filename: "{app}\{#AppExe}"; Parameters: "--exit"; RunOnceId: "ExitApp"; Flags: runhidden waituntilterminated
Filename: "{app}\{#AppExe}"; Parameters: "--startup off"; RunOnceId: "StartupOff"; Flags: runhidden waituntilterminated

[Code]
const
  DataDir = '{commonappdata}\AsusFanControl';

{ Closes a running copy (it hands the fans back to the BIOS) before files are
  replaced. Copies older than 1.4.0 do not answer the signal, so the user is
  asked to quit them from the tray. }
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
  Exe: String;
begin
  Result := '';
  ExtractTemporaryFile('{#AppExe}');
  ExtractTemporaryFile('AsusFanControl.exe');
  Exe := ExpandConstant('{tmp}\{#AppExe}');
  while True do
  begin
    if not Exec(Exe, '--exit', ExpandConstant('{tmp}'), SW_HIDE, ewWaitUntilTerminated, ResultCode) then
      ResultCode := 1;
    if ResultCode = 0 then
      Exit;
    if WizardSilent or (MsgBox('Asus Fan Control is running and could not be closed automatically.' + #13#10#13#10 +
        'Right-click its icon in the notification area and choose Exit, then click Retry.',
        mbError, MB_RETRYCANCEL) <> IDRETRY) then
    begin
      Result := 'Asus Fan Control is still running. Quit it from the notification area and run Setup again.';
      Exit;
    end;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Dir: String;
begin
  if CurUninstallStep <> usPostUninstall then
    Exit;
  Dir := ExpandConstant(DataDir);
  if UninstallSilent or not DirExists(Dir) then
    Exit;
  if MsgBox('Also delete your Asus Fan Control settings, profiles and log?' + #13#10#13#10 + Dir,
      mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
    DelTree(Dir, True, True, True);
end;
