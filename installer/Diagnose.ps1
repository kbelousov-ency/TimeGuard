$ErrorActionPreference = "Continue"
$out = "C:\Users\Public\TimeGuard-diag.txt"
$L = @()
function A($t){ $script:L += $t }

A "=== TimeGuard журнал/диагностика $(Get-Date) ==="
A "Запущено под: $(whoami)"
A ""
A "=== Служба ==="
A ((sc.exe query TimeGuard 2>&1) -join "`n")
A ""
A "=== Процессы TimeGuard ==="
A ((Get-Process TimeGuard.Service,TimeGuard.Agent -ErrorAction SilentlyContinue | Select-Object Name,Id,SessionId | Out-String).Trim())
A ""
A "=== Активные сессии (query session) ==="
A ((query session 2>&1) -join "`n")
A ""
A "=== ЛОГ АГЕНТА (C:\Users\Public\TimeGuard-agent.log) ==="
$ap = "C:\Users\Public\TimeGuard-agent.log"
if (Test-Path $ap) {
  A "изменён: $((Get-Item $ap).LastWriteTime)"
  A ((Get-Content $ap -Tail 80 -Encoding UTF8 -ErrorAction SilentlyContinue) -join "`n")
} else {
  A "Файл не найден — код агента не дошёл даже до записи лога."
}
A ""
A "=== Версии файлов службы/агента ==="
foreach ($f in "TimeGuard.Service.dll","TimeGuard.Agent.dll") {
  $p = Join-Path "$Env:ProgramFiles\TimeGuard" $f
  if (Test-Path $p) { A ("{0}  {1}" -f (Get-Item $p).LastWriteTime, $f) }
}
A ""
A "=== Журнал службы TimeGuard (последние 14, полные) ==="
try {
  Get-EventLog -LogName Application -Source TimeGuard -Newest 14 -ErrorAction Stop | ForEach-Object {
    A ("--- {0} [{1}] ---" -f $_.TimeGenerated,$_.EntryType); A ($_.Message); A ""
  }
} catch {
  try {
    $ev2 = Get-WinEvent -FilterHashtable @{ LogName='Application'; ProviderName='TimeGuard' } -MaxEvents 14 -ErrorAction Stop
    A (($ev2 | Select-Object TimeCreated,LevelDisplayName,Message | Out-String).Trim())
  } catch { A "нет записей (или источник TimeGuard не зарегистрирован)" }
}
A ""
A "=== .NET Runtime события об ошибках приложения (Agent), 10 ==="
try {
  A ((Get-WinEvent -FilterHashtable @{ LogName='Application'; ProviderName='.NET Runtime','Application Error' } -MaxEvents 10 -ErrorAction Stop |
      Where-Object { $_.Message -match 'TimeGuard' } | Select-Object TimeCreated,Message | Out-String).Trim())
} catch { A "нет" }

$L | Out-File -FilePath $out -Encoding utf8
Write-Host "Отчёт: $out" -ForegroundColor Green
Start-Process notepad.exe $out
