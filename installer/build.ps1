<#
.SYNOPSIS
  Публикует клиентские компоненты (служба + агент) в одну папку.
  Запускать на машине разработки. Результат из .\publish копируется на клиент.
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutDir = "$PSScriptRoot\publish"
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent

if (Test-Path $OutDir) { Remove-Item $OutDir -Recurse -Force }
New-Item -ItemType Directory -Path $OutDir | Out-Null

$common = @("-c", $Configuration, "-r", $Runtime, "--self-contained", "true",
            "-p:PublishSingleFile=false", "-o", $OutDir)

Write-Host "Публикация службы..." -ForegroundColor Cyan
dotnet publish "$root\src\TimeGuard.Service\TimeGuard.Service.csproj" @common
Write-Host "Публикация агента..." -ForegroundColor Cyan
dotnet publish "$root\src\TimeGuard.Agent\TimeGuard.Agent.csproj" @common

Write-Host "Готово: $OutDir" -ForegroundColor Green
Write-Host "Родительское приложение публикуйте отдельно (на ПК родителя):" -ForegroundColor Yellow
Write-Host "  dotnet publish src\TimeGuard.Parent\TimeGuard.Parent.csproj -c Release -r win-x64 --self-contained true"
