$ErrorActionPreference = "Continue"
$out = "C:\Users\Public\TimeGuard-diag.txt"
$L = @()
function A($t){ $script:L += $t }

A "=== TimeGuard журнал/диагностика $(Get-Date) ==="
A "Запущено под: $(whoami)"
A ""
A "=== Служба (sc query) ==="
A ((sc.exe query TimeGuard 2>&1) -join "`n")
A ""
A "=== Процессы ==="
A ((Get-Process TimeGuard.Service,TimeGuard.Agent -ErrorAction SilentlyContinue | Select-Object Name,Id,SessionId | Out-String).Trim())
A ""
A "=== Активные сессии (query session) ==="
A ((query session 2>&1) -join "`n")
A ""
A "=== Журнал событий: источник TimeGuard (30 последних) ==="
$logged = $false
try {
  $ev = Get-EventLog -LogName Application -Source TimeGuard -Newest 30 -ErrorAction Stop
  if ($ev) { A (($ev | Select-Object TimeGenerated,EntryType,Message | Out-String).Trim()); $logged = $true }
} catch {}
if (-not $logged) {
  try {
    $ev2 = Get-WinEvent -FilterHashtable @{ LogName='Application'; ProviderName='TimeGuard' } -MaxEvents 30 -ErrorAction Stop
    A (($ev2 | Select-Object TimeCreated,LevelDisplayName,Message | Out-String).Trim()); $logged = $true
  } catch {}
}
if (-not $logged) { A "Записей от источника TimeGuard нет (или источник не зарегистрирован)." }
A ""
A "=== .NET Runtime события об ошибках приложения (Agent), 10 ==="
try {
  A ((Get-WinEvent -FilterHashtable @{ LogName='Application'; ProviderName='.NET Runtime','Application Error' } -MaxEvents 10 -ErrorAction Stop |
      Where-Object { $_.Message -match 'TimeGuard' } | Select-Object TimeCreated,Message | Out-String).Trim())
} catch { A "нет" }

$L | Out-File -FilePath $out -Encoding utf8
Write-Host "Отчёт: $out" -ForegroundColor Green
Start-Process notepad.exe $out
