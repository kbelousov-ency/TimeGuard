@echo off
setlocal EnableExtensions
cd /d "%~dp0"

rem ===== настройки ночного запрета (меняй тут) =====
rem START = час начала (0..23), END = час конца (0..23)
rem ENABLED = on (включить) или off (выключить)
set "START=21"
set "END=9"
set "ENABLED=on"
rem =================================================

net session >nul 2>&1
if %errorlevel% neq 0 (
  powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)

"%ProgramFiles%\TimeGuard\TimeGuard.Service.exe" --set-curfew %START% %END% %ENABLED%
echo.
echo Перезапускаю службу...
sc.exe stop TimeGuard >nul 2>&1
timeout /t 2 /nobreak >nul
sc.exe start TimeGuard >nul 2>&1
echo Готово.
echo.
pause
