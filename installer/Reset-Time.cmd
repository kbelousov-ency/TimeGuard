@echo off
setlocal EnableExtensions
cd /d "%~dp0"

net session >nul 2>&1
if %errorlevel% neq 0 (
  powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)

if not exist "%ProgramData%\TimeGuard\" mkdir "%ProgramData%\TimeGuard"
echo reset> "%ProgramData%\TimeGuard\reset.flag"
echo Время сброшено в 0. Экран ребёнка заблокируется в течение секунды.
echo.
pause
