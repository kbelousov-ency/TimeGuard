param([string]$InstallDir, [string]$PairingFile, [string]$ChildUser, [switch]$Remove, [switch]$StopOnly)
$ErrorActionPreference = 'Stop'
if ([Environment]::Is64BitOperatingSystem -and ![Environment]::Is64BitProcess) {
    $nativePowerShell = Join-Path $env:WINDIR 'Sysnative\WindowsPowerShell\v1.0\powershell.exe'
    $forward = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $PSCommandPath, '-InstallDir', $InstallDir)
    if ($PairingFile) { $forward += @('-PairingFile', $PairingFile) }
    if ($ChildUser) { $forward += @('-ChildUser', $ChildUser) }
    if ($Remove) { $forward += '-Remove' }
    if ($StopOnly) { $forward += '-StopOnly' }
    & $nativePowerShell @forward
    exit $LASTEXITCODE
}
function Native([string]$File, [string[]]$Arguments) {
    & $File @Arguments | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "$File failed: $LASTEXITCODE" }
}
try {
    Import-Module Microsoft.PowerShell.LocalAccounts -ErrorAction Stop
    if (!$Remove -and $ChildUser) {
        $user = Get-LocalUser -Name $ChildUser -ErrorAction SilentlyContinue
        if (!$user) { throw 'Учётная запись Windows не найдена. Укажите существующую учётку ребёнка. Новые учётные записи не создаются.' }
        if (!$user.Enabled) { throw 'Учётная запись Windows отключена.' }
        $admins = Get-LocalGroupMember -SID 'S-1-5-32-544'
        if ($admins.SID.Value -contains $user.SID.Value) { throw 'Укажите стандартную учётную запись ребёнка без прав администратора.' }
    }
    $service = Get-Service TimeGuard -ErrorAction SilentlyContinue
    if ($service) { Stop-Service TimeGuard; $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30)) }
    Get-Process TimeGuard.Agent -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq (Join-Path $InstallDir 'TimeGuard.Agent.exe') } | Stop-Process -Force
    if ($StopOnly) { exit 0 }
    if ($Remove) {
        if ($service) { Native 'sc.exe' @('delete','TimeGuard') }
        & netsh.exe advfirewall firewall delete rule name="TimeGuard History" | Out-Null
        $savedUser = Join-Path $InstallDir 'target-user.txt'
        if (Test-Path $savedUser) {
            $user = Get-LocalUser -Name (Get-Content $savedUser -Raw).Trim() -ErrorAction SilentlyContinue
            if ($user) {
                $key = "Registry::HKEY_USERS\$($user.SID.Value)\Software\Microsoft\Windows\CurrentVersion\Policies\System"
                if (Test-Path $key) { Remove-ItemProperty $key DisableTaskMgr -ErrorAction SilentlyContinue }
            }
        }
        exit 0
    }
    if (!$user) { throw 'Укажите существующую учётную запись ребёнка.' }
    $users = Get-LocalGroupMember -SID 'S-1-5-32-545'
    if ($users.SID.Value -notcontains $user.SID.Value) { Add-LocalGroupMember -SID 'S-1-5-32-545' -Member $user }
    $data = Join-Path $env:ProgramData 'TimeGuard'
    New-Item -ItemType Directory -Path $data -Force | Out-Null
    Native 'icacls.exe' @($data, '/inheritance:r', '/grant:r', '*S-1-5-18:(OI)(CI)F', '*S-1-5-32-544:(OI)(CI)F')
    if ($PairingFile) {
        $json = Get-Content -LiteralPath $PairingFile -Raw -Encoding UTF8
        $cfg = $json | ConvertFrom-Json
        if (!$cfg.MachineId -or [Convert]::FromBase64String($cfg.SecretBase64).Length -lt 16 -or [Convert]::FromBase64String($cfg.MasterHashBase64).Length -ne 32 -or [Convert]::FromBase64String($cfg.MasterSaltBase64).Length -ne 16) { throw 'Invalid pairing file.' }
        Native (Join-Path $InstallDir 'TimeGuard.Service.exe') @('--import',$PairingFile)
    } elseif (!(Test-Path (Join-Path $data 'config.dat'))) { throw 'Pairing file is required for a new installation.' }
    Native (Join-Path $InstallDir 'TimeGuard.Service.exe') @('--set-user',$ChildUser)
    [IO.File]::WriteAllText((Join-Path $InstallDir 'target-user.txt'),$ChildUser)
    $exe = '"' + (Join-Path $InstallDir 'TimeGuard.Service.exe') + '"'
    if (!$service) { Native 'sc.exe' @('create','TimeGuard','binPath=',$exe,'start=','auto','obj=','LocalSystem') }
    Native 'sc.exe' @('failure','TimeGuard','reset=','86400','actions=','restart/5000/restart/5000/restart/5000')
    & netsh.exe advfirewall firewall add rule name="TimeGuard History" dir=in action=allow protocol=TCP localport=38765 profile=private | Out-Null
    Start-Service TimeGuard
    (Get-Service TimeGuard).WaitForStatus('Running',[TimeSpan]::FromSeconds(30))
    exit 0
} catch {
    Add-Type -AssemblyName System.Windows.Forms
    [Windows.Forms.MessageBox]::Show($_.Exception.Message,'TimeGuard setup') | Out-Null
    exit 1
}
