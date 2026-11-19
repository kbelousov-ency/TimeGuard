$ErrorActionPreference = "Stop"
$dir = $PSScriptRoot
$dst = "$Env:ProgramFiles\TimeGuard"

Write-Host "Останавливаю службу..." -ForegroundColor Cyan
sc.exe stop TimeGuard | Out-Null

# Ждём реального завершения процесса службы (до 20 c)
for ($i = 0; $i -lt 40; $i++) {
    if (-not (Get-Process TimeGuard.Service -ErrorAction SilentlyContinue)) { break }
    Start-Sleep -Milliseconds 500
}
# Принудительно снимаем всё, что осталось
Get-Process TimeGuard.Service -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Get-Process TimeGuard.Agent   -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1

Write-Host "Копирую новые файлы в $dst..." -ForegroundColor Cyan
Copy-Item -Path (Join-Path $dir "publish\*") -Destination $dst -Recurse -Force

Write-Host ""
Write-Host "Проверка версий скопированных файлов:" -ForegroundColor Green
Get-Item "$dst\TimeGuard.Service.dll","$dst\TimeGuard.Agent.dll" |
    ForEach-Object { Write-Host ("  {0}  {1}" -f $_.LastWriteTime, $_.Name) }

Write-Host ""
Write-Host "Запускаю службу..." -ForegroundColor Cyan
sc.exe start TimeGuard | Out-Null
Start-Sleep -Seconds 1
Write-Host ("Служба: " + (Get-Service TimeGuard).Status) -ForegroundColor Green
Write-Host ""
Write-Host "Теперь зайди под учёткой ребёнка и подожди несколько секунд." -ForegroundColor Yellow
