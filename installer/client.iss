[Setup]
SetupIconFile=..\assets\app.ico
AppId=TimeGuard.Client
AppName=TimeGuard Client
AppVersion=1.1.0
DefaultDirName={autopf}\TimeGuard
PrivilegesRequired=admin
OutputDir=dist
OutputBaseFilename=TimeGuard-Client-Setup
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\TimeGuard.Agent.exe
[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
[Files]
Source: "stage\client\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "configure-client.ps1"; DestDir: "{app}"
Source: "configure-client.ps1"; Flags: dontcopy
[UninstallRun]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\configure-client.ps1"" -InstallDir ""{app}"" -Remove"; Flags: runhidden waituntilterminated; RunOnceId: "RemoveTimeGuardService"
[Code]
var
  PairPage: TInputFileWizardPage;
  AccountPage: TInputQueryWizardPage;
procedure InitializeWizard;
begin
  AccountPage := CreateInputQueryPage(wpSelectDir, 'Учётная запись ребёнка', 'Ограничение действует только для этой учётной записи.', 'Укажите существующую стандартную учётную запись Windows. Новые учётные записи не создаются.');
  AccountPage.Add('Имя учётной записи:', False);
  AccountPage.Values[0] := '';
  PairPage := CreateInputFilePage(AccountPage.ID, 'Сопряжение с родителем', 'Выберите файл, экспортированный из приложения родителя.', 'При обновлении можно оставить поле пустым, чтобы сохранить текущую пару.');
  PairPage.Add('Файл сопряжения:', 'Файл сопряжения (*.json)|*.json', '.json');
end;
function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if CurPageID = AccountPage.ID then begin
    Result := (Trim(AccountPage.Values[0]) <> '') and (Pos('"', AccountPage.Values[0]) = 0);
    if not Result then MsgBox('Укажите имя учётной записи без кавычек.', mbError, MB_OK);
  end;
  if (CurPageID = PairPage.ID) and (Trim(PairPage.Values[0]) = '') and not FileExists(ExpandConstant('{commonappdata}\TimeGuard\config.dat')) then begin
    MsgBox('Для новой установки нужен файл сопряжения.', mbError, MB_OK);
    Result := False;
  end;
  if (CurPageID = PairPage.ID) and (Trim(PairPage.Values[0]) <> '') and not FileExists(PairPage.Values[0]) then begin
    MsgBox('Выбранный файл не найден.', mbError, MB_OK);
    Result := False;
  end;
end;
function PrepareToInstall(var NeedsRestart: Boolean): String;
var Code: Integer;
begin
  Result := '';
  ExtractTemporaryFile('configure-client.ps1');
  if not Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    '-NoProfile -ExecutionPolicy Bypass -File "' + ExpandConstant('{tmp}\configure-client.ps1') + '" -InstallDir "' + ExpandConstant('{app}') + '" -ChildUser "' + AccountPage.Values[0] + '" -StopOnly', '', SW_HIDE, ewWaitUntilTerminated, Code) then
    Result := 'Не удалось остановить предыдущую версию.'
  else if Code <> 0 then Result := 'Проверка установки не пройдена. Проверьте имя учётной записи и сообщение об ошибке.';
end;
procedure CurStepChanged(CurStep: TSetupStep);
var Code: Integer; PairArgument: String;
begin
  if CurStep = ssPostInstall then begin
    PairArgument := '';
    if Trim(PairPage.Values[0]) <> '' then PairArgument := ' -PairingFile "' + PairPage.Values[0] + '"';
      if not Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
        '-NoProfile -ExecutionPolicy Bypass -File "' + ExpandConstant('{app}\configure-client.ps1') + '" -InstallDir "' + ExpandConstant('{app}') + '"' + PairArgument + ' -ChildUser "' + AccountPage.Values[0] + '"', '', SW_HIDE, ewWaitUntilTerminated, Code) then
        RaiseException('Не удалось запустить настройку клиента.');
      if Code <> 0 then RaiseException('Настройка клиента не завершена. Исправьте данные и повторите установку.');
  end;
end;
