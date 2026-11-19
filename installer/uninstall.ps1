<#
.SYNOPSIS
  Удаляет клиент TimeGuard: снимает политики, останавливает и удаляет службу,
  удаляет файлы. Данные состояния в %ProgramData%\TimeGuard удаляются по флагу.
.NOTES
  Запускать от имени администратора на клиентском ПК.
#>
[CmdletBinding()]
param(
    [string]$InstallDir = "$Env:ProgramFiles\TimeGuard",
    [string]$ServiceName = "TimeGuard",
    [switch]$PurgeData
)
$ErrorActionPreference = "SilentlyContinue"

# Снять политику DisableTaskMgr для всех загруженных кустов пользователей
Get-ChildItem "Registry::HKEY_USERS" | ForEach-Object {
    $key = "Registry::$($_.Name)\Software\Microsoft\Windows\CurrentVersion\Policies\System"
    if (Test-Path $key) { Remove-ItemProperty -Path $key -Name "DisableTaskMgr" -ErrorAction SilentlyContinue }
}

if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    sc.exe stop $ServiceName | Out-Null
    Start-Sleep -Seconds 2
    sc.exe delete $ServiceName | Out-Null
}

netsh.exe advfirewall firewall delete rule name="TimeGuard History" | Out-Null
netsh.exe advfirewall firewall delete rule name="TimeGuard Discovery" | Out-Null

if (Test-Path $InstallDir) { Remove-Item $InstallDir -Recurse -Force }
if ($PurgeData) { Remove-Item "$Env:ProgramData\TimeGuard" -Recurse -Force }

Write-Host "TimeGuard удалён." -ForegroundColor Green
