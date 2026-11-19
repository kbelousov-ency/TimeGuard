[Setup]
SetupIconFile=..\assets\app.ico
AppId=TimeGuard.Client
AppName=TimeGuard Client
AppVersion=1.2.3
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
  FindData: TFindRec;
procedure InitializeWizard;
begin
  PairPage := CreateInputFilePage(wpSelectDir, 'Сопряжение с родителем', 'Выберите файл, экспортированный из приложения родителя.', 'Ограничение будет привязано к текущей учётной записи Windows. При обновлении поле можно оставить пустым.');
  PairPage.Add('Файл сопряжения:', 'Файлы сопряжения (*.tgpair.json;*.json)|*.tgpair.json;*.json|Все файлы (*.*)|*.*', '.json');
  if FindFirst(ExpandConstant('{src}\*.tgpair.json'), FindData) then begin
    PairPage.Values[0] := ExpandConstant('{src}\') + FindData.Name;
    FindClose(FindData);
  end;
end;
function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
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
    '-NoProfile -ExecutionPolicy Bypass -File "' + ExpandConstant('{tmp}\configure-client.ps1') + '" -InstallDir "' + ExpandConstant('{app}') + '" -StopOnly', '', SW_HIDE, ewWaitUntilTerminated, Code) then
    Result := 'Не удалось остановить предыдущую версию.'
  else if Code <> 0 then Result := 'Проверка установки не пройдена. Проверьте текущую учётную запись и сообщение об ошибке.';
end;
procedure CurStepChanged(CurStep: TSetupStep);
var Code: Integer; PairArgument: String;
begin
  if CurStep = ssPostInstall then begin
    PairArgument := '';
    if Trim(PairPage.Values[0]) <> '' then PairArgument := ' -PairingFile "' + PairPage.Values[0] + '"';
      if not Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
        '-NoProfile -ExecutionPolicy Bypass -File "' + ExpandConstant('{app}\configure-client.ps1') + '" -InstallDir "' + ExpandConstant('{app}') + '"' + PairArgument, '', SW_HIDE, ewWaitUntilTerminated, Code) then
        RaiseException('Не удалось запустить настройку клиента.');
      if Code <> 0 then RaiseException('Настройка клиента не завершена. Исправьте данные и повторите установку.');
  end;
end;
