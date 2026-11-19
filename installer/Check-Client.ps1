$ErrorActionPreference = "SilentlyContinue"
$out = Join-Path ([Environment]::GetFolderPath("Desktop")) "TimeGuard-проверка.txt"
$L = @()
function A($t){ $script:L += $t }

A "=== Проверка TimeGuard $(Get-Date) ==="
A "Сессия пользователя: $(whoami)"
A ""

$svc = Get-Service TimeGuard
$svcState = if ($svc) { "$($svc.Status)" } else { "НЕ УСТАНОВЛЕНА" }
A "Служба: $svcState"

$proc = Get-Process TimeGuard.Service,TimeGuard.Agent
A "Процессы:"
if ($proc) { foreach($p in $proc){ A ("  {0}  PID={1}  Сессия={2}" -f $p.Name,$p.Id,$p.SessionId) } } else { A "  нет" }

$cfg = Test-Path "$Env:ProgramData\TimeGuard\config.dat"
A "config.dat (сопряжение): $cfg"
A "state.dat: $(Test-Path "$Env:ProgramData\TimeGuard\state.dat")"
A "Файлы в Program Files\TimeGuard: $(if (Test-Path "$Env:ProgramFiles\TimeGuard") { (Get-ChildItem "$Env:ProgramFiles\TimeGuard" -Filter *.exe | Select -Expand Name) -join ', ' } else { 'ПАПКИ НЕТ' })"
A "Моя сессия: $((Get-Process -Id $PID).SessionId)"
A ""

# ---- вывод ----
A "=== ВЫВОД ==="
if (-not $svc) {
  A "УСТАНОВКА НЕ ВЫПОЛНЕНА: службы нет."
  A "Нужно запустить Install-Client.cmd с правами администратора клиентского ПК."
} elseif ($svc.Status -ne "Running") {
  A "Служба установлена, но НЕ запущена (Status=$($svc.Status))."
} elseif (-not $cfg) {
  A "Служба работает, но НЕТ файла сопряжения (config.dat)."
  A "Импорт pairing.json не прошёл — переустановить с файлом pairing.json рядом."
} elseif (-not ($proc | Where-Object Name -eq 'TimeGuard.Agent')) {
  A "Служба и сопряжение на месте, но заглушка (TimeGuard.Agent) НЕ запущена в этой сессии."
  A "Нужен журнал службы (запусти Diagnose.cmd от администратора)."
} else {
  A "Всё на месте: служба работает, сопряжение есть, агент запущен."
  A "Если заглушки не видно — напиши об этом."
}

$L | Out-File -FilePath $out -Encoding utf8
Start-Process notepad.exe $out
