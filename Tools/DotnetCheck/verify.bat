@echo off
rem VOW Phase 1 verification (double-click to run). Needs Git for Windows (bash) and .NET 8 SDK.
setlocal
set "BASH=%ProgramFiles%\Git\bin\bash.exe"
if not exist "%BASH%" set "BASH=bash"
"%BASH%" "%~dp0verify.sh"
echo.
pause
