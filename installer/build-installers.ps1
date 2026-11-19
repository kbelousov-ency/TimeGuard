[CmdletBinding()]
param([string]$Compiler)
$ErrorActionPreference = 'Stop'
if (!$Compiler) { $Compiler = Join-Path $PSScriptRoot 'tools\Inno\ISCC.exe' }
$root = Split-Path $PSScriptRoot -Parent
foreach ($item in @(@('Parent','parent'),@('Service','client'),@('Agent','client'))) {
    dotnet publish "$root\src\TimeGuard.$($item[0])\TimeGuard.$($item[0]).csproj" -c Release -r win-x64 --self-contained true -o "$PSScriptRoot\stage\$($item[1])"
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
}
foreach ($name in @('parent','client')) {
    & $Compiler "$PSScriptRoot\$name.iss"
    if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed' }
}

Compress-Archive -LiteralPath "$PSScriptRoot\dist\TimeGuard-Parent-Setup.exe" `
    -DestinationPath "$PSScriptRoot\dist\TimeGuard-Parent-Setup.zip" -CompressionLevel Optimal -Force
Compress-Archive -LiteralPath "$PSScriptRoot\dist\TimeGuard-Client-Setup.exe" `
    -DestinationPath "$PSScriptRoot\dist\TimeGuard-Client-Setup.zip" -CompressionLevel Optimal -Force
