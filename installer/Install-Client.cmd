@echo off
setlocal EnableExtensions
cd /d "%~dp0"

rem ==== settings you may change ====
rem CHILD = exact account name to restrict (must match the child's login name)
set "CHILD=Kid"
rem CREATE = 1 to create that standard account, 0 if it already exists
set "CREATE=0"
rem =================================

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

if "%CREATE%"=="1" (
  powershell -NoProfile -ExecutionPolicy Bypass -File ".\install.ps1" -SourceDir ".\publish" -PairingFile "%PAIR%" -CreateChildAccount -ChildUserName "%CHILD%"
) else (
  powershell -NoProfile -ExecutionPolicy Bypass -File ".\install.ps1" -SourceDir ".\publish" -PairingFile "%PAIR%" -ChildUserName "%CHILD%"
)

echo.
pause
