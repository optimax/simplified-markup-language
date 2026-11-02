@echo off
rem Pulls the latest main from GitHub, then publishes smlxml for Windows:
rem publish\smlxml\win-x64\smlxml.exe (64-bit) and publish\smlxml\win-x86\smlxml.exe (32-bit).
rem The pull only fast-forwards: if main has local commits, or was rewritten on GitHub, it stops
rem instead of merging, and nothing is published.
setlocal
set "SCRIPT=%~nx0"
cd /d "%~dp0"

where git >nul 2>nul || (echo git was not found on PATH. & goto :failed)
where dotnet >nul 2>nul || (echo dotnet was not found on PATH; install the .NET 10 SDK. & goto :failed)

set "BRANCH="
for /f "delims=" %%b in ('git rev-parse --abbrev-ref HEAD') do set "BRANCH=%%b"
if /i not "%BRANCH%"=="main" (
    echo This script publishes main, but the current branch is "%BRANCH%".
    echo Switch with: git switch main
    goto :failed
)

echo Pulling the latest main...
git pull --ff-only origin main
if errorlevel 1 (
    echo.
    echo The pull did not fast-forward. If main has local commits, push or move them first.
    echo If main was rewritten on GitHub, reset to it: git fetch origin, then git reset --hard origin/main
    goto :failed
)

echo.
echo Publishing smlxml...
dotnet publish Source\SmlXml\SmlXml.csproj -p:PublishProfile=windows
if errorlevel 1 goto :failed

echo.
echo Published:
echo   publish\smlxml\win-x64\smlxml.exe   64-bit
echo   publish\smlxml\win-x86\smlxml.exe   32-bit
call :pause_if_double_clicked
exit /b 0

:failed
echo.
echo pull-and-publish stopped.
call :pause_if_double_clicked
exit /b 1

rem Keeps the window open when the script was started by double-clicking it in Explorer.
:pause_if_double_clicked
echo "%cmdcmdline%" | find /i "%SCRIPT%" >nul && timeout 10
exit /b 0
