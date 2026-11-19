$nativePowerShell = Join-Path $env:WINDIR 'Sysnative\WindowsPowerShell\v1.0\powershell.exe'
& $nativePowerShell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'probe-native.ps1')
exit $LASTEXITCODE
