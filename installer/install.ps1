<#
.SYNOPSIS
  Устанавливает клиент TimeGuard: копирует файлы, регистрирует службу с
  авто-запуском и перезапуском при сбое, импортирует файл сопряжения и, по
  желанию, создаёт стандартную (не-админ) учётную запись ребёнка.
.NOTES
  Запускать в PowerShell ОТ ИМЕНИ АДМИНИСТРАТОРА на клиентском ПК.
.EXAMPLE
  .\install.ps1 -SourceDir .\publish -PairingFile .\pairing.json -CreateChildAccount -ChildUserName Kid
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$SourceDir,
    [string]$InstallDir = "$Env:ProgramFiles\TimeGuard",
    [string]$PairingFile,
    [switch]$CreateChildAccount,
    [string]$ChildUserName = "Kid",
    [string]$ServiceName = "TimeGuard"
)

$ErrorActionPreference = "Stop"

function Assert-Admin {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    $p = New-Object Security.Principal.WindowsPrincipal($id)
    if (-not $p.IsInRole([Security.Principal.WindowsBuiltinRole]::Administrator)) {
        throw "Запустите скрипт от имени администратора."
    }
}
Assert-Admin

function Get-InteractiveUserName {
    $qualified = $null
    try { $qualified = (Get-CimInstance Win32_ComputerSystem -ErrorAction Stop).UserName } catch {}
    if ([string]::IsNullOrWhiteSpace($qualified)) { $qualified = [Environment]::UserName }
    if ($qualified -match '\\') { return ($qualified -split '\\')[-1] }
    if ($qualified -match '@') { return ($qualified -split '@')[0] }
    return $qualified
}

# Привязка всегда идёт к текущей интерактивной учётной записи.
$ChildUserName = Get-InteractiveUserName

$exe = Join-Path $InstallDir "TimeGuard.Service.exe"

# 1. Остановить и удалить старую службу, если была
if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    Write-Host "Останавливаю существующую службу..." -ForegroundColor Cyan
    sc.exe stop $ServiceName | Out-Null
    Start-Sleep -Seconds 2
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 1
}

# 2. Скопировать файлы
Write-Host "Копирую файлы в $InstallDir..." -ForegroundColor Cyan
New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
Copy-Item -Path (Join-Path $SourceDir '*') -Destination $InstallDir -Recurse -Force

# 3. Импортировать сопряжение (до старта службы)
if ($PairingFile) {
    Write-Host "Импортирую файл сопряжения..." -ForegroundColor Cyan
    & $exe --import $PairingFile
    if ($LASTEXITCODE -ne 0) { throw "Импорт сопряжения не удался." }
}

# 3a. Привязать ограничение к учётке ребёнка (иначе блокируется любой вошедший)
Write-Host "Привязываю ограничение к учётке '$ChildUserName'..." -ForegroundColor Cyan
& $exe --set-user $ChildUserName
if ($LASTEXITCODE -ne 0) { Write-Warning "Не удалось задать целевую учётку — проверьте вручную." }

# 4. Зарегистрировать службу: авто-старт, запуск под LocalSystem
Write-Host "Регистрирую службу $ServiceName..." -ForegroundColor Cyan
sc.exe create $ServiceName binPath= "`"$exe`"" start= auto obj= LocalSystem DisplayName= "TimeGuard" | Out-Null
sc.exe description $ServiceName "Ограничение времени работы (родительский контроль)." | Out-Null

# 5. Восстановление при сбое: перезапуск через 5 сек, счётчик сбросов раз в сутки
sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/5000/restart/5000 | Out-Null
sc.exe failureflag $ServiceName 1 | Out-Null

# История доступна родительскому ПК только в частной локальной сети.
netsh.exe advfirewall firewall add rule name="TimeGuard History" dir=in action=allow protocol=TCP localport=38765 profile=private | Out-Null

# 6. Учётная запись ребёнка (стандартная, без прав администратора)
if ($CreateChildAccount) {
    if (-not (Get-LocalUser -Name $ChildUserName -ErrorAction SilentlyContinue)) {
        Write-Host "Создаю стандартную учётку '$ChildUserName' (без пароля)..." -ForegroundColor Cyan
        New-LocalUser -Name $ChildUserName -NoPassword -AccountNeverExpires `
            -Description "TimeGuard: ограниченная учётка" | Out-Null
        Set-LocalUser -Name $ChildUserName -PasswordNeverExpires $true
    }
    # гарантировать, что НЕ администратор
    try { Remove-LocalGroupMember -Group "Администраторы" -Member $ChildUserName -ErrorAction SilentlyContinue } catch {}
    try { Remove-LocalGroupMember -Group "Administrators" -Member $ChildUserName -ErrorAction SilentlyContinue } catch {}
    try { Add-LocalGroupMember -Group "Пользователи" -Member $ChildUserName -ErrorAction SilentlyContinue } catch {}
    try { Add-LocalGroupMember -Group "Users" -Member $ChildUserName -ErrorAction SilentlyContinue } catch {}
    Write-Host "Учётка готова. Убедитесь, что ребёнок входит именно под ней." -ForegroundColor Green
}

# 7. Запустить службу
Write-Host "Запускаю службу..." -ForegroundColor Cyan
sc.exe start $ServiceName | Out-Null

Write-Host "`nУстановка завершена." -ForegroundColor Green
Write-Host "Проверьте: Get-Service $ServiceName; журнал событий (источник TimeGuard)."
