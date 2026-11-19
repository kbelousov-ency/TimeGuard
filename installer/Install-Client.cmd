@echo off
setlocal EnableExtensions
cd /d "%~dp0"

rem ---- self-elevate to administrator ----
net session >nul 2>&1
if %errorlevel% neq 0 (
  echo Requesting administrator rights...
  powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)

rem ---- find pairing file ----
set "PAIR="
for %%F in (pairing*.json *.tgpair.json) do if not defined PAIR set "PAIR=%%F"
if not defined PAIR (
  echo.
  echo [ERROR] Pairing file not found. Put pairing.json next to this file.
  echo.
  pause
  exit /b 1
)

echo Using pairing file: %PAIR%
echo.

powershell -NoProfile -ExecutionPolicy Bypass -File ".\install.ps1" -SourceDir ".\publish" -PairingFile "%PAIR%"

echo.
pause
