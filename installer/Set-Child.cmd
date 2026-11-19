@echo off
setlocal EnableExtensions
cd /d "%~dp0"

rem ==== укажи точное имя учётки ребёнка ====
set "CHILD=Roma"
rem =========================================

net session >nul 2>&1
if %errorlevel% neq 0 (
  powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)
"%ProgramFiles%\TimeGuard\TimeGuard.Service.exe" --set-user "%CHILD%"
echo.
echo Перезапускаю службу...
sc.exe stop TimeGuard >nul 2>&1
timeout /t 2 /nobreak >nul
sc.exe start TimeGuard >nul 2>&1
echo Готово. Ограничение теперь только для учётки: %CHILD%
echo.
pause
