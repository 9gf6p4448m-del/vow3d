@echo off
rem Build the VOW WebGL preview and deploy it to GitHub Pages (double-click to run).
rem Needs Git for Windows (bash) and Unity 2022.3.62f1 with WebGL Build Support.
setlocal
set "BASH=%ProgramFiles%\Git\bin\bash.exe"
if not exist "%BASH%" set "BASH=bash"
"%BASH%" "%~dp0deploy-webgl.sh"
echo.
echo Exit code: %ERRORLEVEL%
pause
