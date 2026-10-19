; Inno Setup script for ScreenFX
; Требует Inno Setup 6

#define MyAppName "ScreenFX"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "ScreenFX Team"
#define MyAppURL "https://github.com/flare-award/FreestyleFilters"
#define MyAppExeName "ScreenFX.exe"

[Setup]
AppId={{3B4F8C2A-7E1D-4A9B-8F0C-1D2E3F4A5B6C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
DefaultDirName={autopf}\ScreenFX
DefaultGroupName=ScreenFX
AllowNoIcons=yes
OutputDir=..\artifacts
OutputBaseFilename=ScreenFX-Setup-{#MyAppVersion}-x64
Compression=lzma
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\src\ScreenFX.App\Assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
ArchitecturesInstallIn64BitMode=x64
ArchitecturesAllowed=x64
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
CloseApplications=yes
RestartApplications=no
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription=Системные экранные фильтры реального времени
VersionInfoProductName=ScreenFX
MinVersion=10.0.19041

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startupicon"; Description: "Запускать вместе с Windows"; GroupDescription: "Автозапуск:"; Flags: unchecked

[Files]
Source: "..\publish\ScreenFX\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; Шейдеры уже внутри publish

[Icons]
Name: "{group}\ScreenFX"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"; Comment: "ScreenFX — системные фильтры"
Name: "{group}\{cm:UninstallProgram,ScreenFX}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\ScreenFX"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon; IconFilename: "{app}\{#MyAppExeName}"

[Registry]
Root: HKCU; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "ScreenFX"; ValueData: """{app}\{#MyAppExeName}"" --minimized"; Flags: uninsdeletevalue; Tasks: startupicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,ScreenFX}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}\logs"

[Code]
var
  StartupCheckBox: TNewCheckBox;

procedure InitializeWizard;
begin
  // Дополнительная проверка HDR и т.п. не требуется
end;

function InitializeUninstall(): Boolean;
begin
  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ProfilesPath: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    // Не удаляем профили без согласия — оставляем в %APPDATA%\ScreenFX
    // Пользователь может удалить вручную
  end;
end;
